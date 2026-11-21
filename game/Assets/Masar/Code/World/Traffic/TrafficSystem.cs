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
    /// - Cars keep their lane (no lane changes yet) and pick a random turn at each junction.
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
                c.v = Mathf.Max(0f, c.v + acc * dt);
                c.braking = acc < -0.8f || (c.v < 0.3f && gap < 8f);
                c.s += c.v * dt;
                c.stuck = c.v < 0.2f ? c.stuck + dt : 0f;

                // Lane transitions
                while (c.s >= graph.lanes[c.lane].length)
                {
                    float over = c.s - graph.lanes[c.lane].length;
                    if (c.next < 0) { Remove(i); goto nextCar; }
                    _onLane[c.lane].Remove(c);
                    c.lane = c.next; c.s = over; c.next = -1;
                    _onLane[c.lane].Add(c);
                    if (graph.lanes[c.lane].nextCount > 0) c.next = ChooseNext(c.lane);
                }

                graph.Sample(c.lane, c.s, out var pos, out var tan);
                c.view.Pose(pos, tan, c.v, dt, c.braking);
                nextCar:;
            }

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

        float Weight(int lane) { int t = graph.lanes[lane].turn; return t == 0 ? 3f : t == 1 ? 1.5f : 1f; }

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
                    else if (!j.signal && graph.lanes[other].roadClass > lane.roadClass)
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
            Vector3 f = t.forward;
            float ahead = Vector3.Dot(d, f);
            if (ahead < 0f) return;
            float lateral = Mathf.Abs(Vector3.Dot(d, t.right));
            if (lateral > 2.3f) return;
            float g = ahead - CarLength;
            if (g < gap) { gap = g; leaderV = Mathf.Max(0f, Vector3.Dot(pv, f)); }
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
            var c = new Car { view = view, prefab = p, lane = lane, s = s, v = v, desire = 0.85f + (float)_rnd.NextDouble() * 0.3f };
            graph.Sample(lane, s, out var pos, out var tan);
            view.Place(pos, tan);
            _cars.Add(c);
            _onLane[lane].Add(c);
        }

        void Remove(int i)
        {
            var c = _cars[i];
            _onLane[c.lane].Remove(c);
            c.view.gameObject.SetActive(false);
            _pool[c.prefab].Push(c.view);
            _cars.RemoveAt(i);
        }
    }
}
