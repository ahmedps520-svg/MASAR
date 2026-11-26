using System.Collections.Generic;
using Masar.World.Traffic;
using UnityEngine;

namespace Masar.EditorTools.City
{
    /// <summary>
    /// Builds the traffic lane graph and pedestrian walkways for a generated map.
    /// Roads are split at junctions into edges; each edge carries its lanes (KSA: right-hand
    /// traffic), trimmed back from junction centers. At junctions, connectors (cubic Bezier curves)
    /// join arriving lanes to departing lanes with lane discipline: the rightmost lane turns right,
    /// the leftmost turns left, all lanes go straight. U-turns at junctions are not generated (Jeddah
    /// U-turns are median openings, a later item). Connectors that cross or merge are listed as conflicts.
    /// Signal groups split approaches by axis (two phases).
    /// </summary>
    public sealed class LaneGraphBuilder
    {
        sealed class LaneTmp
        {
            public List<Vector2> P;
            public List<float> Y;   // height per point (flyovers, ramps, trenches)
            public float Speed;
            public int Junction = -1, Group;
            public byte Class, Turn;
            public List<int> Next = new List<int>();
            public List<int> Conflicts = new List<int>();
            // Road lanes only
            public long StartNode, EndNode;
            public int Order;   // 0 = rightmost lane of its direction
            public int Count;   // lanes in that direction
            public Vector2 DirIn, DirOut;
        }

        readonly RoadNetwork _net;
        readonly Rect _area;
        readonly List<LaneTmp> _lanes = new List<LaneTmp>();
        readonly List<RoadNetwork.Junction> _junctionList = new List<RoadNetwork.Junction>();
        readonly Dictionary<long, int> _junctionIndex = new Dictionary<long, int>();
        readonly Dictionary<int, int> _clusterSize = new Dictionary<int, int>(), _clusterRep = new Dictionary<int, int>();

        /// <summary>Multi-node intersections (divided roads) act as one junction: one key for all their nodes.</summary>
        long Key(long node) => _net.Junctions.TryGetValue(node, out var j) && _clusterSize[j.Cluster] > 1 ? -1_000_000L - j.Cluster : node;
        bool SameCluster(long a, long b) => _net.Junctions.TryGetValue(a, out var ja) && _net.Junctions.TryGetValue(b, out var jb) && ja.Cluster == jb.Cluster && _clusterSize[ja.Cluster] > 1;
        public int Connectors, RoadLanes, Signals, UTurns;

        public LaneGraphBuilder(RoadNetwork net, Rect area) { _net = net; _area = area; }

        /// <summary>
        /// The junction connectors at ground level (the paths cars take through intersections, U-turns
        /// included), as x/z polylines: the road surface is paved under them.
        /// </summary>
        public static List<Vector2[]> GroundConnectors(CityGraph g)
        {
            var paths = new List<Vector2[]>();
            foreach (var l in g.lanes)
            {
                if (l.junction < 0) continue;
                var p = new Vector2[l.pointCount];
                bool ground = true;
                for (int i = 0; i < l.pointCount; i++)
                {
                    var q = g.points[l.pointStart + i];
                    ground &= Mathf.Abs(q.y) <= 0.05f;   // ground level (heights within GroundTol, decks lifted 2 cm)
                    p[i] = new Vector2(q.x, q.z);
                }
                if (ground) paths.Add(p);
            }
            return paths;
        }

        public CityGraph Build(Vector3 spawn, float spawnYaw)
        {
            foreach (var j in _net.Junctions.Values) { _junctionIndex[j.NodeId] = _junctionList.Count; _junctionList.Add(j); }
            foreach (var j in _junctionList)
            {
                _clusterSize[j.Cluster] = _clusterSize.TryGetValue(j.Cluster, out var n) ? n + 1 : 1;
                if (!_clusterRep.ContainsKey(j.Cluster)) _clusterRep[j.Cluster] = _junctionIndex[j.NodeId];
            }
            BuildRoadLanes();
            BuildConnectors();
            BuildConflicts();

            var g = ScriptableObject.CreateInstance<CityGraph>();
            var pts = new List<Vector3>(); var lanes = new List<CityGraph.Lane>(); var next = new List<int>(); var conf = new List<int>();
            foreach (var l in _lanes)
            {
                var cl = new CityGraph.Lane
                {
                    pointStart = pts.Count, pointCount = l.P.Count, length = RoadNetwork.Length(l.P), speed = l.Speed,
                    junction = l.Junction, signalGroup = l.Group, nextStart = next.Count, nextCount = l.Next.Count,
                    conflictStart = conf.Count, conflictCount = l.Conflicts.Count, roadClass = l.Class, turn = l.Turn,
                };
                for (int i = 0; i < l.P.Count; i++) pts.Add(new Vector3(l.P[i].x, l.Y[i], l.P[i].y));
                float len3 = 0f;
                for (int i = cl.pointStart + 1; i < pts.Count; i++) len3 += Vector3.Distance(pts[i - 1], pts[i]);
                cl.length = len3;   // along the slope, as CityGraph.Sample measures it
                next.AddRange(l.Next); conf.AddRange(l.Conflicts);
                lanes.Add(cl);
            }
            g.points = pts.ToArray(); g.lanes = lanes.ToArray(); g.next = next.ToArray(); g.conflicts = conf.ToArray();
            var js = new CityGraph.Junction[_junctionList.Count];
            var rnd = new System.Random(3);
            for (int i = 0; i < js.Length; i++)
            {
                var j = _junctionList[i];
                // Nodes of one intersection share the clock: offset from the cluster id, green from its biggest road
                var cmax = j.MaxClass;
                foreach (var o in _junctionList) if (o.Cluster == j.Cluster && o.MaxClass > cmax) cmax = o.MaxClass;
                js[i] = new CityGraph.Junction { position = new Vector3(j.P.x, Lifted(j.H), j.P.y), signal = j.Signal, greenTime = cmax >= RoadClass.Primary ? 22f : 16f, cycleOffset = (float)new System.Random(j.Cluster * 7919).NextDouble() * 40f };
                if (j.Signal) Signals++;
            }
            g.junctions = js;
            g.spawnPosition = spawn; g.spawnYaw = spawnYaw;
            return g;
        }

        // ------------------------------------------------------------------ road lanes

        void BuildRoadLanes()
        {
            foreach (var road in _net.Roads)
            {
                // Split at junction nodes
                var cuts = new List<int> { 0 };
                for (int i = 1; i < road.NodeIds.Count - 1; i++) if (_net.Junctions.ContainsKey(road.NodeIds[i])) cuts.Add(i);
                cuts.Add(road.NodeIds.Count - 1);
                for (int c = 0; c + 1 < cuts.Count; c++)
                {
                    int a = cuts[c], b = cuts[c + 1];
                    var pts = road.P.GetRange(a, b - a + 1);
                    var hts = road.H.GetRange(a, b - a + 1);
                    long na = road.NodeIds[a], nb = road.NodeIds[b];
                    if (SameCluster(na, nb)) continue;   // inside an intersection (median crossing): covered by connectors
                    float len = RoadNetwork.Length(pts);
                    float trimA = _net.Junctions.TryGetValue(na, out var ja) ? ja.Radius : 0f;
                    float trimB = _net.Junctions.TryGetValue(nb, out var jb) ? jb.Radius : 0f;
                    if (len - trimA - trimB < 3f) continue;   // too short between two junctions: no lanes
                    var core = TrimH(pts, hts, trimA, len - trimB, out var coreH);
                    if (core.Count < 2) continue;
                    int n = road.LanesFwd + road.LanesBack;
                    float sh = road.HalfWidth - n * road.LaneWidth * 0.5f;
                    // Lane k from the left edge: center offset (right positive) = -hw + sh + lw (k + 0.5)
                    for (int k = 0; k < n; k++)
                    {
                        float u = -road.HalfWidth + sh + road.LaneWidth * (k + 0.5f);
                        bool forward = k >= road.LanesBack;
                        var line = RoadNetwork.Offset(core, u);
                        var lineH = new List<float>(coreH.Count);
                        foreach (float hh in coreH) lineH.Add(Lifted(hh));
                        if (!forward) { line.Reverse(); lineH.Reverse(); }
                        ClipToArea(line, out int c0, out int cn);
                        var lt = new LaneTmp
                        {
                            P = cn > 0 ? line.GetRange(c0, cn) : null, Y = cn > 0 ? lineH.GetRange(c0, cn) : null,
                            Speed = road.SpeedLimit, Class = (byte)road.Class,
                            StartNode = forward ? na : nb, EndNode = forward ? nb : na,
                            Order = forward ? n - 1 - k : k, Count = forward ? road.LanesFwd : road.LanesBack,
                        };
                        if (lt.P == null || lt.P.Count < 2 || RoadNetwork.Length(lt.P) < 2f) continue;
                        lt.DirOut = (lt.P[lt.P.Count - 1] - lt.P[lt.P.Count - 2]).normalized;
                        lt.DirIn = (lt.P[1] - lt.P[0]).normalized;
                        // A lane clipped at the map edge no longer reaches its end node
                        if (!_area.Contains(line[line.Count - 1])) lt.EndNode = long.MinValue;
                        if (!_area.Contains(line[0])) lt.StartNode = long.MinValue + 1;
                        _lanes.Add(lt);
                        RoadLanes++;
                    }
                }
            }
        }

        /// <summary>The longest run of points inside the map (start index and count; count 0 when none).</summary>
        void ClipToArea(List<Vector2> line, out int start, out int count)
        {
            start = 0; count = 0;
            int runStart = -1;
            for (int i = 0; i <= line.Count; i++)
            {
                bool inside = i < line.Count && _area.Contains(line[i]);
                if (inside) { if (runStart < 0) runStart = i; continue; }
                if (runStart >= 0 && i - runStart > count) { start = runStart; count = i - runStart; }
                runStart = -1;
            }
        }

        static float Lifted(float h) => h > 0f ? h + RoadStructures.Lift : h;

        /// <summary><see cref="RoadNetwork.Trim"/> that also interpolates the heights.</summary>
        static List<Vector2> TrimH(List<Vector2> pts, List<float> hs, float s0, float s1, out List<float> outH)
        {
            var o = new List<Vector2>(); outH = new List<float>();
            float s = 0f;
            for (int i = 1; i < pts.Count; i++)
            {
                Vector2 a = pts[i - 1], b = pts[i];
                float l = (b - a).magnitude;
                if (l < 1e-5f) continue;
                float e = s + l;
                if (e >= s0 && s <= s1)
                {
                    float ta = Mathf.Clamp01((s0 - s) / l), tb = Mathf.Clamp01((s1 - s) / l);
                    var pa = Vector2.Lerp(a, b, ta);
                    if (o.Count == 0 || (o[o.Count - 1] - pa).sqrMagnitude > 1e-6f) { o.Add(pa); outH.Add(Mathf.Lerp(hs[i - 1], hs[i], ta)); }
                    o.Add(Vector2.Lerp(a, b, tb)); outH.Add(Mathf.Lerp(hs[i - 1], hs[i], tb));
                }
                s = e;
            }
            return o;
        }

        // ------------------------------------------------------------------ connectors

        void BuildConnectors()
        {
            var arriving = new Dictionary<long, List<int>>();
            var departing = new Dictionary<long, List<int>>();
            for (int i = 0; i < _lanes.Count; i++)
            {
                var l = _lanes[i];
                long ek = Key(l.EndNode), sk = Key(l.StartNode);
                if (!arriving.TryGetValue(ek, out var a)) arriving[ek] = a = new List<int>();
                a.Add(i);
                if (!departing.TryGetValue(sk, out var d)) departing[sk] = d = new List<int>();
                d.Add(i);
            }
            int roadLaneCount = _lanes.Count;
            foreach (var kv in arriving)
            {
                long node = kv.Key;
                if (!departing.TryGetValue(node, out var outs)) continue;
                int jIdx;
                bool isJunction = node <= -1_000_000L && node > -1_000_000_000L ? (jIdx = _clusterRep[(int)(-1_000_000L - node)]) >= 0 : _junctionIndex.TryGetValue(node, out jIdx);
                // Group arriving lanes by approach direction (same road edge = same direction + same end)
                foreach (int ia in kv.Value)
                {
                    var A = _lanes[ia];
                    // Departing approaches: group departing lanes by edge (same StartNode and similar direction)
                    var exitsByDir = new List<List<int>>();
                    foreach (int io in outs)
                    {
                        var O = _lanes[io];
                        Vector2 gapV = O.P[0] - A.P[A.P.Count - 1];
                        if (Vector2.Dot(O.DirIn, -A.DirOut) > 0.94f && gapV.magnitude < 40f && isJunction)
                        {
                            // U-turn (Jeddah: from the leftmost lane through a median opening or at an
                            // intersection) onto the leftmost lane of the opposite carriageway. Only on
                            // divided roads: the carriageways must be at least 5 m apart.
                            bool left = Vector2.SignedAngle(A.DirOut, gapV) > 30f;
                            if (left && gapV.magnitude > 5f && A.Order == A.Count - 1 && O.Order == O.Count - 1 && A.Class >= (byte)RoadClass.Tertiary
                                && Mathf.Abs(A.Y[A.Y.Count - 1] - O.Y[0]) < 0.5f)
                            {
                                int ui = AddConnector(ia, io, jIdx, 3);
                                if (ui >= 0) { A.Next.Add(ui); UTurns++; }
                            }
                            continue;
                        }
                        bool placed = false;
                        foreach (var grp in exitsByDir)
                            if (Vector2.Dot(_lanes[grp[0]].DirIn, O.DirIn) > 0.97f && (_lanes[grp[0]].P[0] - O.P[0]).magnitude < 15f) { grp.Add(io); placed = true; break; }
                        if (!placed) exitsByDir.Add(new List<int> { io });
                    }
                    foreach (var grp in exitsByDir)
                    {
                        var O0 = _lanes[grp[0]];
                        float ang = Vector2.SignedAngle(A.DirOut, O0.DirIn);   // + = left turn (counter-clockwise)
                        byte turn = Mathf.Abs(ang) < 35f ? (byte)0 : ang < 0 ? (byte)1 : (byte)2;
                        if (Mathf.Abs(ang) > 150f) continue;
                        // Lane discipline
                        if (isJunction && A.Count > 1)
                        {
                            if (turn == 1 && A.Order != 0) continue;
                            if (turn == 2 && A.Order != A.Count - 1) continue;
                        }
                        grp.Sort((x, y) => _lanes[x].Order.CompareTo(_lanes[y].Order));
                        int target = turn == 1 ? grp[0] : turn == 2 ? grp[grp.Count - 1] : grp[Mathf.Clamp(Mathf.RoundToInt((float)A.Order / Mathf.Max(1, A.Count - 1) * (grp.Count - 1)), 0, grp.Count - 1)];
                        if (!isJunction)
                        {
                            // Way-to-way continuation: join directly if the ends meet, else a short connector
                            var O = _lanes[target];
                            if ((O.P[0] - A.P[A.P.Count - 1]).magnitude < 0.6f) { A.Next.Add(target); continue; }
                        }
                        int ci = AddConnector(ia, target, isJunction ? jIdx : -1, turn);
                        if (ci >= 0) A.Next.Add(ci);
                    }
                }
            }
            // Signal groups: approaches split by axis, one axis per intersection (cluster of nodes)
            var axis = new Dictionary<int, Vector2>();
            for (int i = roadLaneCount; i < _lanes.Count; i++)
            {
                var c = _lanes[i];
                if (c.Junction < 0) continue;
                int cl = _junctionList[c.Junction].Cluster;
                if (!axis.TryGetValue(cl, out var a0)) { a0 = c.DirIn; axis[cl] = a0; }
                c.Group = Mathf.Abs(Vector2.Dot(c.DirIn, a0)) > 0.707f ? 0 : 1;
            }
        }

        int AddConnector(int from, int to, int junction, byte turn)
        {
            var A = _lanes[from]; var B = _lanes[to];
            Vector2 p0 = A.P[A.P.Count - 1], p3 = B.P[0];
            float d = (p3 - p0).magnitude;
            if (d < 0.05f) return -1;
            bool uturn = turn == 3;
            // U-turns: control points for a half circle across the median (4/3 r, r = d / 2)
            float h = uturn ? Mathf.Max(3f, d * 0.67f) : d * 0.4f;
            Vector2 p1 = p0 + A.DirOut * h, p2 = p3 - B.DirIn * h;
            var pts = new List<Vector2>(); var ys = new List<float>();
            float y0 = A.Y[A.Y.Count - 1], y3 = B.Y[0];
            int steps = Mathf.Clamp(Mathf.CeilToInt(d / 2f), uturn ? 10 : 3, 16);
            for (int k = 0; k <= steps; k++)
            {
                float t = (float)k / steps, u = 1f - t;
                pts.Add(u * u * u * p0 + 3f * u * u * t * p1 + 3f * u * t * t * p2 + t * t * t * p3);
                ys.Add(Mathf.Lerp(y0, y3, t * t * (3f - 2f * t)));
            }
            // Curve speed: v = sqrt(a_lat * R), R from the heading change over the length
            float heading = Mathf.Abs(Vector2.Angle(A.DirOut, B.DirIn)) * Mathf.Deg2Rad;
            float len = RoadNetwork.Length(pts);
            float radius = heading > 0.05f ? len / heading : 1000f;
            float speed = Mathf.Min(Mathf.Min(A.Speed, B.Speed), Mathf.Sqrt(2.6f * radius));
            // U-turners have the lowest priority: they yield to everyone at unsignalized openings
            var c = new LaneTmp { P = pts, Y = ys, Speed = Mathf.Max(uturn ? 2.5f : 3f, speed), Junction = junction, Class = uturn ? (byte)0 : A.Class, Turn = turn, DirIn = A.DirOut, DirOut = B.DirIn };
            c.Next.Add(to);
            _lanes.Add(c);
            Connectors++;
            return _lanes.Count - 1;
        }

        void BuildConflicts()
        {
            var byJunction = new Dictionary<int, List<int>>();
            for (int i = 0; i < _lanes.Count; i++)
            {
                int j = _lanes[i].Junction;
                if (j < 0) continue;
                if (!byJunction.TryGetValue(j, out var l)) byJunction[j] = l = new List<int>();
                l.Add(i);
            }
            foreach (var list in byJunction.Values)
                for (int a = 0; a < list.Count; a++)
                    for (int b = a + 1; b < list.Count; b++)
                    {
                        var A = _lanes[list[a]]; var B = _lanes[list[b]];
                        bool sameStart = (A.P[0] - B.P[0]).sqrMagnitude < 0.25f;
                        bool sameEnd = (A.P[A.P.Count - 1] - B.P[B.P.Count - 1]).sqrMagnitude < 0.25f;
                        if (sameStart && !sameEnd) continue;   // diverging from one lane: sequential, never together
                        if (sameEnd || Cross(A.P, B.P)) { A.Conflicts.Add(list[b]); B.Conflicts.Add(list[a]); }
                    }
        }

        static bool Cross(List<Vector2> a, List<Vector2> b)
        {
            for (int i = 1; i < a.Count; i++)
                for (int k = 1; k < b.Count; k++)
                    if (SegmentsIntersect(a[i - 1], a[i], b[k - 1], b[k])) return true;
            // Lanes passing very close count as crossing (two cars cannot be there together)
            for (int i = 0; i < a.Count; i += 2)
                for (int k = 0; k < b.Count; k += 2)
                    if ((a[i] - b[k]).sqrMagnitude < 2.2f * 2.2f) return true;
            return false;
        }

        static bool SegmentsIntersect(Vector2 p1, Vector2 p2, Vector2 p3, Vector2 p4)
        {
            float d1 = Cross2(p4 - p3, p1 - p3), d2 = Cross2(p4 - p3, p2 - p3), d3 = Cross2(p2 - p1, p3 - p1), d4 = Cross2(p2 - p1, p4 - p1);
            return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
        }
        static float Cross2(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        // ------------------------------------------------------------------ walkways

        /// <summary>Pedestrian walkways along the sidewalks, once the road surfaces exist.</summary>
        public void AddWalks(CityGraph g, RoadSurface surf)
        {
            var pts = new List<Vector3>(); var walks = new List<CityGraph.Walk>();
            foreach (var road in _net.Roads)
            {
                if (road.Sidewalk < 2f || road.Class == RoadClass.Service) continue;
                foreach (int side in new[] { 1, -1 })
                {
                    var line = RoadNetwork.Offset(road.P, side * (road.HalfWidth + road.Sidewalk * 0.5f));
                    // Resample every 2 m and keep the runs that are on the sidewalk (raised, inside the sidewalk band)
                    var dense = Resample(line, 2f);
                    var run = new List<Vector2>();
                    void Flush()
                    {
                        if (run.Count >= 6)
                        {
                            walks.Add(new CityGraph.Walk { pointStart = pts.Count, pointCount = run.Count, length = RoadNetwork.Length(run) });
                            foreach (var p in run) pts.Add(new Vector3(p.x, RoadSurface.KerbHeight, p.y));
                        }
                        run = new List<Vector2>();
                    }
                    for (int i = 0; i < dense.Count; i++)
                    {
                        var p = dense[i];
                        // Not along a median: another carriageway close behind the sidewalk (outward = this side)
                        Vector2 t = dense[Mathf.Min(i + 1, dense.Count - 1)] - dense[Mathf.Max(i - 1, 0)];
                        Vector2 outward = t.sqrMagnitude > 1e-6f ? new Vector2(t.y, -t.x).normalized * side : Vector2.zero;
                        bool median = false;
                        for (float k = 1f; k <= 8f && !median && outward != Vector2.zero; k += 1f)
                            median = surf.Sample(surf.Rg, p + outward * (road.Sidewalk * 0.5f + k)) < 0f;
                        bool ok = !median && _area.Contains(p) && surf.Sample(surf.Rg, p) > 0.8f && surf.Sample(surf.Sg, p) < -0.3f
                                  && surf.Sample(surf.E, p) > 1f && surf.VoidField(surf.Index(p)) < 0f;
                        if (ok) run.Add(p); else Flush();
                    }
                    Flush();
                }
            }
            g.walkPoints = pts.ToArray(); g.walks = walks.ToArray();
        }

        static List<Vector2> Resample(List<Vector2> line, float step)
        {
            var o = new List<Vector2>();
            float len = RoadNetwork.Length(line);
            for (float s = 0f; s <= len; s += step) o.Add(PointAt(line, s));
            return o;
        }

        static Vector2 PointAt(List<Vector2> pts, float s)
        {
            float acc = 0f;
            for (int i = 1; i < pts.Count; i++)
            {
                float l = (pts[i] - pts[i - 1]).magnitude;
                if (acc + l >= s) return Vector2.Lerp(pts[i - 1], pts[i], l > 1e-4f ? (s - acc) / l : 0f);
                acc += l;
            }
            return pts[pts.Count - 1];
        }
    }
}
