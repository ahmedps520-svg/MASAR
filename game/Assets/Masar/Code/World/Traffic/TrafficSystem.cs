using System.Collections.Generic;
using UnityEngine;

namespace Masar.World.Traffic
{
    /// <summary>
    /// AI traffic on a <see cref="CityGraph"/> (Month 2 v1).
    /// - Longitudinal control: Intelligent Driver Model (Treiber et al.) per car, with a per-driver
    ///   desired-speed factor. Leaders: the next car on the lane or the lanes ahead, the player's car
    ///   when it is in the path, and virtual stops at junction stop lines.
    /// - Junctions: signal state from <see cref="SignalClock"/> (the same state the signal heads show);
    ///   a car enters a connector only when no crossing connector is occupied, the exit lane has room,
    ///   and, at unsignalized junctions, no higher-class car is about to cross.
    /// - Lane changes (Month 3, M3-5): MOBIL (Kesting, Treiber and Helbing 2007). A car moves to the
    ///   lane beside it when its own gain in acceleration, plus a politeness share of what the cars
    ///   behind gain or lose, beats a threshold, and the new follower would not have to brake harder
    ///   than 3.5 m/s2. A small keep-right bias. The indicator blinks for a second first, the move
    ///   takes 2.5 to 4 s, and the car occupies both lanes until it is across.
    /// - Cars pick a random turn at each junction.
    /// - Cars spawn out of view in a ring around the player and are removed far away.
    /// Kinematic rigidbodies: the player can hit them; they never push the player on their own.
    /// </summary>
    public sealed class TrafficSystem : MonoBehaviour
    {
        public CityGraph graph;
        public TrafficCar[] carPrefabs;

        /// <summary>0 off, 1 light, 2 normal, 3 heavy. Set from the settings.</summary>
        public static int Density = 2;
        /// <summary>The player's car (set by the drive session).</summary>
        public static Rigidbody Player;
        public static TrafficSystem Instance { get; private set; }
        static readonly int[] Targets = { 0, 16, 34, 60 };

        public float spawnMin = 90f, spawnMax = 340f, despawnDistance = 430f;

        // IDM parameters (urban): max acceleration, comfortable deceleration, jam gap, time headway
        const float AMax = 1.5f, BComf = 2.4f, S0 = 2.2f, Headway = 1.35f, CarLength = 4.8f;

        sealed class Car
        {
            public TrafficCar view;
            public int prefab;
            public int lane, next = -1;
            public float s, v, desire, stuck;
            public bool braking;
            // lane change: signalling (pending >= 0), then moving (from >= 0, t 0..1)
            public int lcPending = -1, lcFrom = -1, lcDir;
            public float lcSignal, lcT, lcDur, nextDecision;
        }

        readonly List<Car> _cars = new List<Car>();
        List<Car>[] _onLane;
        List<int>[] _pred;
        readonly List<int> _roadLanes = new List<int>();
        Stack<TrafficCar>[] _pool;
        float _nextSpawn;
        bool _initialFill = true;
        System.Random _rnd = new System.Random(7);

        public int CarCount => _cars.Count;
        /// <summary>Lane changes completed since the map loaded.</summary>
        public int LaneChanges { get; private set; }
        /// <summary>Times two AI cars' bodies overlapped (should stay 0).</summary>
        public int Overlaps { get; private set; }
        /// <summary>Road lanes with a same-direction lane beside them (left, right).</summary>
        public int AdjacentLanes { get; private set; }

        // MOBIL parameters
        const float Politeness = 0.3f, ChangeThreshold = 0.25f, KeepRightBias = 0.12f, SafeDecel = 3.5f;
        int[] _left, _right;
        readonly HashSet<long> _overlapping = new HashSet<long>();
        float _nextOverlapCheck;

        void Awake()
        {
            Instance = this;
            if (graph == null || graph.lanes.Length == 0) { enabled = false; return; }
            int n = graph.lanes.Length;
            _onLane = new List<Car>[n];
            _pred = new List<int>[n];
            for (int i = 0; i < n; i++) { _onLane[i] = new List<Car>(4); _pred[i] = new List<int>(2); }
            for (int i = 0; i < n; i++)
            {
                var l = graph.lanes[i];
                for (int k = 0; k < l.nextCount; k++) _pred[graph.next[l.nextStart + k]].Add(i);
                if (l.junction < 0 && l.length > 25f) _roadLanes.Add(i);
            }
            BuildAdjacency();
            _pool = new Stack<TrafficCar>[carPrefabs.Length];
            for (int i = 0; i < _pool.Length; i++) _pool[i] = new Stack<TrafficCar>();
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        // ------------------------------------------------------------------ simulation

        void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            Vector3 pp = Player != null ? Player.position : Vector3.zero;
            Vector3 pv = Player != null ? Player.linearVelocity : Vector3.zero;
            bool havePlayer = Player != null;

            foreach (var list in _onLane) if (list.Count > 1) list.Sort((a, b) => a.s.CompareTo(b.s));

            for (int i = _cars.Count - 1; i >= 0; i--)
            {
                var c = _cars[i];
                var lane = graph.lanes[c.lane];
                if (c.next < 0 && lane.nextCount > 0) c.next = ChooseNext(c.lane);

                float gap = float.MaxValue, leaderV = 0f;
                Leader(c, ref gap, ref leaderV);
                if (lane.junction < 0 && c.next >= 0) JunctionStop(c, ref gap, ref leaderV);
                if (havePlayer) PlayerObstacle(c, pp, pv, ref gap, ref leaderV);

                float v0 = Mathf.Max(1f, lane.speed * c.desire);
                // Slow early for a sharp connector ahead
                if (c.next >= 0)
                {
                    float vNext = graph.lanes[c.next].speed * c.desire;
                    float remain = lane.length - c.s;
                    float allowed = Mathf.Sqrt(vNext * vNext + 2f * BComf * Mathf.Max(0f, remain - 2f));
                    v0 = Mathf.Min(v0, allowed);
                }
                float acc = AMax * (1f - Mathf.Pow(c.v / v0, 4f));
                if (gap < 1e5f)
                {
                    float sStar = S0 + Mathf.Max(0f, c.v * Headway + c.v * (c.v - leaderV) / (2f * Mathf.Sqrt(AMax * BComf)));
                    float g = Mathf.Max(0.1f, gap);
                    acc -= AMax * (sStar / g) * (sStar / g);
                }
                acc = Mathf.Clamp(acc, -8f, AMax);
                LaneChangeStep(c, dt, pp, pv, havePlayer);
                c.v = Mathf.Max(0f, c.v + acc * dt);
                c.braking = acc < -0.8f || (c.v < 0.3f && gap < 8f);
                c.s += c.v * dt;
                c.stuck = c.v < 0.2f ? c.stuck + dt : 0f;

                // Lane transitions
                while (c.s >= graph.lanes[c.lane].length)
                {
                    float over = c.s - graph.lanes[c.lane].length;
                    if (c.next < 0) { Remove(i); goto nextCar; }
                    FinishChange(c);
                    _onLane[c.lane].Remove(c);
                    c.lane = c.next; c.s = over; c.next = -1;
                    _onLane[c.lane].Add(c);
                    if (graph.lanes[c.lane].nextCount > 0) c.next = ChooseNext(c.lane);
                }

                graph.Sample(c.lane, c.s, out var pos, out var tan);
                if (c.lcFrom >= 0)
                {
                    // Blend from the old lane to the new one along a smooth S
                    float sFrom = c.s * graph.lanes[c.lcFrom].length / Mathf.Max(1f, graph.lanes[c.lane].length);
                    graph.Sample(c.lcFrom, sFrom, out var p0, out var t0);
                    float u = c.lcT * c.lcT * (3f - 2f * c.lcT);
                    float du = 6f * c.lcT * (1f - c.lcT) / Mathf.Max(0.5f, c.lcDur * Mathf.Max(1f, c.v));   // per metre travelled
                    Vector3 lateral = pos - p0;
                    pos = Vector3.Lerp(p0, pos, u);
                    tan = (Vector3.Lerp(t0, tan, u) + lateral * du).normalized;
                }
                c.view.Pose(pos, tan, c.v, dt, c.braking);
                nextCar:;
            }

            if (Time.time >= _nextOverlapCheck) { _nextOverlapCheck = Time.time + 0.5f; CountOverlaps(); }
            if (Time.time >= _nextSpawn)
            {
                _nextSpawn = Time.time + (_initialFill ? 0f : 0.25f);
                ManagePopulation(pp, havePlayer);
            }
        }

        int ChooseNext(int lane)
        {
            var l = graph.lanes[lane];
            if (l.nextCount == 0) return -1;
            if (l.nextCount == 1) return graph.next[l.nextStart];
            // Prefer going straight, then right, then left
            float total = 0f;
            for (int k = 0; k < l.nextCount; k++) total += Weight(graph.next[l.nextStart + k]);
            float r = (float)_rnd.NextDouble() * total;
            for (int k = 0; k < l.nextCount; k++)
            {
                int cand = graph.next[l.nextStart + k];
                r -= Weight(cand);
                if (r <= 0f) return cand;
            }
            return graph.next[l.nextStart + l.nextCount - 1];
        }

        float Weight(int lane) { int t = graph.lanes[lane].turn; return t == 0 ? 3f : t == 1 ? 1.5f : t == 2 ? 1f : 0.35f; }

        void Leader(Car c, ref float gap, ref float leaderV)
        {
            var list = _onLane[c.lane];
            int idx = list.IndexOf(c);
            if (idx >= 0 && idx + 1 < list.Count)
            {
                var o = list[idx + 1];
                gap = o.s - c.s - CarLength; leaderV = o.v;
                return;
            }
            // Look ahead into the next two lanes
            float dist = graph.lanes[c.lane].length - c.s;
            int ln = c.next;
            for (int depth = 0; depth < 2 && ln >= 0 && dist < 120f; depth++)
            {
                var l2 = _onLane[ln];
                if (l2.Count > 0)
                {
                    var o = l2[0];
                    gap = dist + o.s - CarLength; leaderV = o.v;
                    return;
                }
                dist += graph.lanes[ln].length;
                var L = graph.lanes[ln];
                ln = L.nextCount == 1 ? graph.next[L.nextStart] : -1;
            }
        }

        void JunctionStop(Car c, ref float gap, ref float leaderV)
        {
            var lane = graph.lanes[c.lane];
            var conn = graph.lanes[c.next];
            if (conn.junction < 0) return;
            float remain = lane.length - c.s;
            if (remain > 70f) return;
            bool stop = false;
            var j = graph.junctions[conn.junction];
            if (j.signal)
            {
                var st = SignalClock.Get(j.greenTime, j.cycleOffset, conn.signalGroup, Time.time);
                if (st == SignalClock.State.Red) stop = true;
                else if (st == SignalClock.State.Amber && remain > c.v * c.v / (2f * 3.5f) + 1f) stop = true;
            }
            if (!stop)
            {
                // Crossing connectors occupied?
                for (int k = 0; k < conn.conflictCount && !stop; k++)
                {
                    int other = graph.conflicts[conn.conflictStart + k];
                    if (_onLane[other].Count > 0) stop = true;
                    else if (!j.signal && graph.lanes[other].roadClass > conn.roadClass)
                    {
                        // Unsignalized: yield to higher-class cars about to enter a crossing connector
                        foreach (int p in _pred[other])
                            foreach (var o in _onLane[p])
                                if (graph.lanes[p].length - o.s < 22f + o.v * 2f && o.v > 0.5f) { stop = true; break; }
                    }
                }
                // Exit lane must have room so the junction never blocks
                if (!stop && conn.nextCount == 1)
                {
                    var exit = _onLane[graph.next[conn.nextStart]];
                    if (exit.Count > 0 && exit[0].s < CarLength + 3f && exit[0].v < 2f) stop = true;
                }
            }
            // Already too close to stop comfortably: commit
            if (stop && remain < 0.5f) stop = false;
            if (stop && remain - 1f < gap) { gap = Mathf.Max(0.1f, remain - 1f); leaderV = 0f; }
        }

        void PlayerObstacle(Car c, Vector3 pp, Vector3 pv, ref float gap, ref float leaderV)
        {
            var t = c.view.transform;
            Vector3 d = pp - t.position;
            if (d.sqrMagnitude > 70f * 70f) return;
            if (Mathf.Abs(d.y) > 3f) return;   // on a flyover above or a road below
            Vector3 f = t.forward;
            float ahead = Vector3.Dot(d, f);
            if (ahead < 0f) return;
            float lateral = Mathf.Abs(Vector3.Dot(d, t.right));
            if (lateral > 2.3f) return;
            float g = ahead - CarLength;
            if (g < gap) { gap = g; leaderV = Mathf.Max(0f, Vector3.Dot(pv, f)); }
        }

        // ------------------------------------------------------------------ lane changes (MOBIL)

        /// <summary>
        /// Finds, for each road lane, the same-direction lane beside it: parallel (tangents within
        /// 14 degrees), 2.4 to 4.6 m to the side at the middle and still 2 to 5 m apart at a quarter
        /// and three quarters of the way, and of similar length. The graph has no lane-group data,
        /// so this is measured from the lane geometry.
        /// </summary>
        void BuildAdjacency()
        {
            int n = graph.lanes.Length;
            _left = new int[n]; _right = new int[n];
            var mid = new Vector3[n]; var tan = new Vector3[n];
            var grid = new Dictionary<Vector2Int, List<int>>();
            for (int i = 0; i < n; i++)
            {
                _left[i] = _right[i] = -1;
                var l = graph.lanes[i];
                if (l.junction >= 0 || l.length < 30f) continue;
                graph.Sample(i, l.length * 0.5f, out mid[i], out tan[i]);
                var k = new Vector2Int(Mathf.FloorToInt(mid[i].x / 20f), Mathf.FloorToInt(mid[i].z / 20f));
                if (!grid.TryGetValue(k, out var list)) grid[k] = list = new List<int>(4);
                list.Add(i);
            }
            int found = 0;
            for (int i = 0; i < n; i++)
            {
                var li = graph.lanes[i];
                if (li.junction >= 0 || li.length < 30f) continue;
                Vector3 right = new Vector3(tan[i].z, 0f, -tan[i].x).normalized;
                var k = new Vector2Int(Mathf.FloorToInt(mid[i].x / 20f), Mathf.FloorToInt(mid[i].z / 20f));
                float bestL = 9f, bestR = 9f;
                for (int dx = -1; dx <= 1; dx++)
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        if (!grid.TryGetValue(new Vector2Int(k.x + dx, k.y + dz), out var list)) continue;
                        foreach (int j in list)
                        {
                            if (j == i) continue;
                            var lj = graph.lanes[j];
                            float ratio = lj.length / li.length;
                            if (ratio < 0.8f || ratio > 1.25f) continue;
                            if (Vector3.Dot(tan[i], tan[j]) < 0.97f) continue;
                            Vector3 d = mid[j] - mid[i];
                            if (Mathf.Abs(d.y) > 1f || Mathf.Abs(Vector3.Dot(d, tan[i])) > li.length * 0.15f) continue;
                            float lat = Vector3.Dot(d, right);
                            if (Mathf.Abs(lat) < 2.4f || Mathf.Abs(lat) > 4.6f) continue;
                            if (!StaysBeside(i, j, 0.25f) || !StaysBeside(i, j, 0.75f)) continue;
                            if (lat > 0f && lat < bestR) { bestR = lat; _right[i] = j; }
                            if (lat < 0f && -lat < bestL) { bestL = -lat; _left[i] = j; }
                        }
                    }
                if (_left[i] >= 0 || _right[i] >= 0) found++;
            }
            AdjacentLanes = found;
            Debug.Log($"[MASAR] Traffic: {found} road lanes have a lane beside them for lane changes");
        }

        bool StaysBeside(int i, int j, float f)
        {
            graph.Sample(i, graph.lanes[i].length * f, out var a, out _);
            graph.Sample(j, graph.lanes[j].length * f, out var b, out _);
            float d = Vector3.Distance(a, b);
            return d > 2f && d < 5f;
        }

        static float Idm(float v, float v0, float gap, float leaderV)
        {
            float acc = AMax * (1f - Mathf.Pow(v / Mathf.Max(1f, v0), 4f));
            if (gap < 1e5f)
            {
                float sStar = S0 + Mathf.Max(0f, v * Headway + v * (v - leaderV) / (2f * Mathf.Sqrt(AMax * BComf)));
                float g = Mathf.Max(0.1f, gap);
                acc -= AMax * (sStar / g) * (sStar / g);
            }
            return Mathf.Clamp(acc, -9f, AMax);
        }

        /// <summary>Car ahead of and behind arc length s on a lane (excluding one car).</summary>
        void Neighbours(int lane, float s, Car self, out Car lead, out Car follow)
        {
            lead = follow = null;
            foreach (var o in _onLane[lane])
            {
                if (o == self) continue;
                if (o.s > s) { if (lead == null || o.s < lead.s) lead = o; }
                else if (follow == null || o.s > follow.s) follow = o;
            }
        }

        /// <summary>The player as a leader for a slot on a lane: the gap ahead, MaxValue if not in the way, or -1 if beside or just behind (unsafe).</summary>
        float PlayerGap(int lane, float s, Vector3 pp, Vector3 pv, bool havePlayer, out float leaderV)
        {
            leaderV = 0f;
            if (!havePlayer) return float.MaxValue;
            graph.Sample(lane, s, out var p, out var t);
            Vector3 d = pp - p;
            if (d.sqrMagnitude > 80f * 80f || Mathf.Abs(d.y) > 3f) return float.MaxValue;
            float along = Vector3.Dot(d, t);
            float lat = Mathf.Abs(Vector3.Dot(d, new Vector3(t.z, 0f, -t.x)));
            if (lat > 2.3f) return float.MaxValue;
            if (along < CarLength + 3f && along > -(CarLength + 10f)) return -1f;
            if (along < 0f) return float.MaxValue;
            leaderV = Mathf.Max(0f, Vector3.Dot(pv, t));
            return along - CarLength;
        }

        /// <summary>MOBIL incentive for moving c to lane t, minus the threshold (NaN if unsafe).</summary>
        float Incentive(Car c, int t, int dir, Vector3 pp, Vector3 pv, bool havePlayer)
        {
            var cur = graph.lanes[c.lane];
            var tl = graph.lanes[t];
            float sT = c.s * tl.length / Mathf.Max(1f, cur.length);
            if (tl.length - sT < 35f) return float.NaN;
            // own acceleration now (cars and the player only: junction stops are the same in both lanes)
            Neighbours(c.lane, c.s, c, out var curLead, out var oldFollow);
            float gapCur = curLead != null ? curLead.s - c.s - CarLength : float.MaxValue, vCur = curLead != null ? curLead.v : 0f;
            float pg = PlayerGap(c.lane, c.s, pp, pv, havePlayer, out float pvCur);
            if (pg >= 0f && pg < gapCur) { gapCur = pg; vCur = pvCur; }
            float v0c = Mathf.Max(1f, cur.speed * c.desire);
            float aCur = Idm(c.v, v0c, gapCur, vCur);
            // in the target lane
            Neighbours(t, sT, c, out var lead, out var follow);
            float gapF = lead != null ? lead.s - sT - CarLength : float.MaxValue, vF = lead != null ? lead.v : 0f;
            float gapB = follow != null ? sT - follow.s - CarLength : float.MaxValue;
            if (gapF < 3f || gapB < 3f) return float.NaN;
            float pgT = PlayerGap(t, sT, pp, pv, havePlayer, out float pvT);
            if (pgT < 0f) return float.NaN;
            if (pgT < gapF) { gapF = pgT; vF = pvT; }
            float aNew = Idm(c.v, Mathf.Max(1f, tl.speed * c.desire), gapF, vF);
            float folGain = 0f;
            if (follow != null)
            {
                float v0f = Mathf.Max(1f, tl.speed * follow.desire);
                float aFolNew = Idm(follow.v, v0f, gapB, c.v);
                if (aFolNew < -SafeDecel) return float.NaN;
                float gapOld = lead != null ? lead.s - follow.s - CarLength : float.MaxValue;
                folGain += aFolNew - Idm(follow.v, v0f, gapOld, lead != null ? lead.v : 0f);
            }
            if (oldFollow != null)
            {
                float v0o = Mathf.Max(1f, cur.speed * oldFollow.desire);
                float gapNow = c.s - oldFollow.s - CarLength;
                float gapAfter = curLead != null ? curLead.s - oldFollow.s - CarLength : float.MaxValue;
                folGain += Idm(oldFollow.v, v0o, gapAfter, curLead != null ? curLead.v : 0f) - Idm(oldFollow.v, v0o, gapNow, c.v);
            }
            float bias = dir > 0 ? KeepRightBias : -KeepRightBias;
            return aNew - aCur + Politeness * folGain + bias - ChangeThreshold;
        }

        void LaneChangeStep(Car c, float dt, Vector3 pp, Vector3 pv, bool havePlayer)
        {
            if (c.lcFrom >= 0)
            {
                c.lcT += dt / c.lcDur;
                if (c.lcT >= 1f) FinishChange(c);
                return;
            }
            if (c.lcPending >= 0)
            {
                c.lcSignal -= dt;
                if (c.lcSignal > 0f) return;
                int t = c.lcPending;
                c.lcPending = -1;
                if (graph.lanes[c.lane].junction >= 0 || float.IsNaN(Incentive(c, t, c.lcDir, pp, pv, havePlayer))) { c.view.SetIndicator(0); return; }
                // go: the car occupies both lanes until it is across
                float sT = c.s * graph.lanes[t].length / Mathf.Max(1f, graph.lanes[c.lane].length);
                c.lcFrom = c.lane;
                c.lane = t; c.s = sT; c.lcT = 0f;
                c.lcDur = Mathf.Clamp(4f - c.v * 0.08f, 2.5f, 4f);
                _onLane[t].Add(c);
                c.next = graph.lanes[t].nextCount > 0 ? ChooseNext(t) : -1;
                return;
            }
            if (Time.time < c.nextDecision) return;
            c.nextDecision = Time.time + 0.8f + (float)_rnd.NextDouble() * 0.7f;
            var lane = graph.lanes[c.lane];
            if (lane.junction >= 0 || c.s < 8f || lane.length - c.s < 45f) return;
            float best = 0f; int bestLane = -1, bestDir = 0;
            for (int dir = -1; dir <= 1; dir += 2)
            {
                int t = dir < 0 ? _left[c.lane] : _right[c.lane];
                if (t < 0) continue;
                float inc = Incentive(c, t, dir, pp, pv, havePlayer);
                if (!float.IsNaN(inc) && inc > best) { best = inc; bestLane = t; bestDir = dir; }
            }
            if (bestLane < 0) return;
            c.lcPending = bestLane; c.lcDir = bestDir; c.lcSignal = 1.1f;
            c.view.SetIndicator(bestDir);
        }

        void FinishChange(Car c)
        {
            if (c.lcPending >= 0) { c.lcPending = -1; c.view.SetIndicator(0); }
            if (c.lcFrom < 0) return;
            _onLane[c.lcFrom].Remove(c);
            c.lcFrom = -1;
            c.view.SetIndicator(0);
            LaneChanges++;
        }

        void CountOverlaps()
        {
            for (int a = 0; a < _cars.Count; a++)
            {
                var ta = _cars[a].view.transform;
                Vector3 pa = ta.position, fa = ta.forward, ra = ta.right;
                for (int b = a + 1; b < _cars.Count; b++)
                {
                    Vector3 d = _cars[b].view.transform.position - pa;
                    long key = ((long)_cars[a].view.GetInstanceID() << 32) ^ (uint)_cars[b].view.GetInstanceID();
                    bool hit = d.sqrMagnitude < 25f && Mathf.Abs(d.y) < 2f && Mathf.Abs(Vector3.Dot(d, fa)) < 3.8f && Mathf.Abs(Vector3.Dot(d, ra)) < 1.5f;
                    if (hit) { if (_overlapping.Add(key)) Overlaps++; }
                    else _overlapping.Remove(key);
                }
            }
        }

        // ------------------------------------------------------------------ population

        void ManagePopulation(Vector3 pp, bool havePlayer)
        {
            int target = Targets[Mathf.Clamp(Density, 0, 3)];
            if (!havePlayer) target = 0;
            var cam = Camera.main;
            // Remove far cars, long-stuck cars that nobody sees, and the excess
            for (int i = _cars.Count - 1; i >= 0; i--)
            {
                var c = _cars[i];
                float d = (c.view.transform.position - pp).magnitude;
                bool visible = cam != null && Visible(cam, c.view.transform.position);
                if (d > despawnDistance || (c.stuck > 45f && !visible) || (_cars.Count > target && !visible && d > 120f))
                    Remove(i);
            }
            int budget = _initialFill ? target : 2;
            for (int tries = 0; tries < budget * 6 && budget > 0 && _cars.Count < target; tries++)
            {
                int lane = _roadLanes[_rnd.Next(_roadLanes.Count)];
                var l = graph.lanes[lane];
                float s = (float)_rnd.NextDouble() * l.length;
                graph.Sample(lane, s, out var pos, out var tan);
                float d = (pos - pp).magnitude;
                float min = _initialFill ? 35f : spawnMin;
                if (d < min || d > spawnMax) continue;
                if (cam != null && Visible(cam, pos) && d < 240f) continue;
                bool free = true;
                foreach (var o in _onLane[lane]) if (Mathf.Abs(o.s - s) < 14f) { free = false; break; }
                if (!free) continue;
                Spawn(lane, s, Mathf.Min(l.speed * 0.6f, 9f));
                budget--;
            }
            _initialFill = false;
        }

        static bool Visible(Camera cam, Vector3 p)
        {
            var v = cam.WorldToViewportPoint(p + Vector3.up);
            return v.z > 0f && v.x > -0.1f && v.x < 1.1f && v.y > -0.1f && v.y < 1.1f;
        }

        void Spawn(int lane, float s, float v)
        {
            if (carPrefabs == null || carPrefabs.Length == 0) return;
            int p = _rnd.Next(carPrefabs.Length);
            TrafficCar view = _pool[p].Count > 0 ? _pool[p].Pop() : Instantiate(carPrefabs[p], transform);
            view.gameObject.SetActive(true);
            var c = new Car { view = view, prefab = p, lane = lane, s = s, v = v, desire = 0.85f + (float)_rnd.NextDouble() * 0.3f,
                              nextDecision = Time.time + 2f + (float)_rnd.NextDouble() * 2f };
            view.SetIndicator(0);
            graph.Sample(lane, s, out var pos, out var tan);
            view.Place(pos, tan);
            _cars.Add(c);
            _onLane[lane].Add(c);
        }

        void Remove(int i)
        {
            var c = _cars[i];
            _onLane[c.lane].Remove(c);
            if (c.lcFrom >= 0) _onLane[c.lcFrom].Remove(c);
            c.view.SetIndicator(0);
            c.view.gameObject.SetActive(false);
            _pool[c.prefab].Push(c.view);
            _cars.RemoveAt(i);
        }
    }
}
