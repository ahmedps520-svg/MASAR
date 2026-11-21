using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Masar.EditorTools.City
{
    public enum RoadClass { Service = 0, Residential = 1, Link = 2, Tertiary = 3, Secondary = 4, Primary = 5, Trunk = 6 }

    /// <summary>
    /// Drivable roads from OSM: one <see cref="Road"/> per OSM way, with width, lanes and sidewalks from
    /// its tags (or class defaults when untagged), plus the junctions where roads meet.
    /// KSA drives on the right. Defaults are typical Jeddah urban cross-sections, not survey data.
    /// </summary>
    public sealed class RoadNetwork
    {
        public sealed class Road
        {
            public OsmMap.Way Way;
            public RoadClass Class;
            public List<Vector2> P = new List<Vector2>();
            public List<long> NodeIds = new List<long>();
            public float HalfWidth, Sidewalk, LaneWidth;
            public int LanesFwd, LanesBack;     // forward = along the point order
            public bool Oneway => LanesBack == 0;
            public float SpeedLimit;            // m/s
            public string Name;
            public bool Marked => Class >= RoadClass.Link;
        }

        public sealed class Junction
        {
            public long NodeId;
            public Vector2 P;
            public List<(Road road, int index)> Arms = new List<(Road, int)>();   // road + point index of this node in it
            public float Radius;
            public bool Signal;
            public RoadClass MaxClass;
            public int Cluster;
            public HashSet<string> Names = new HashSet<string>();
        }

        public readonly List<Road> Roads = new List<Road>();
        public readonly Dictionary<long, Junction> Junctions = new Dictionary<long, Junction>();
        /// <summary>Road incidences per node (way endpoint = 1, interior node = 2).</summary>
        public readonly Dictionary<long, int> Degree = new Dictionary<long, int>();

        public static RoadNetwork Build(OsmMap map)
        {
            var net = new RoadNetwork();
            foreach (var w in map.Ways)
            {
                var hw = w.Tag("highway");
                if (hw == null || w.Tag("area") == "yes") continue;
                if (!TryClass(hw, w.Tag("service"), out var cls)) continue;
                var road = new Road { Way = w, Class = cls, Name = w.Tag("name:en") ?? w.Tag("name") ?? "" };
                foreach (var id in w.Nodes)
                    if (map.Nodes.TryGetValue(id, out var n)) { road.P.Add(n.P); road.NodeIds.Add(id); }
                if (road.P.Count < 2) continue;

                string ow = w.Tag("oneway");
                bool oneway = ow == "yes" || ow == "1" || ow == "true" || w.Tag("junction") == "roundabout" || cls == RoadClass.Trunk && ow != "no";
                if (ow == "-1") { road.P.Reverse(); road.NodeIds.Reverse(); oneway = true; }
                int lanes = ParseInt(w.Tag("lanes"));
                Defaults(cls, oneway, out int defFwd, out int defBack, out road.LaneWidth, out float shoulder, out road.Sidewalk, out float speedKmh);
                if (lanes > 0)
                {
                    if (oneway) { defFwd = lanes; defBack = 0; }
                    else
                    {
                        int f = ParseInt(w.Tag("lanes:forward")), b = ParseInt(w.Tag("lanes:backward"));
                        if (f > 0 && b > 0) { defFwd = f; defBack = b; }
                        else { defFwd = Mathf.Max(1, lanes / 2); defBack = Mathf.Max(1, lanes - defFwd); }
                    }
                }
                road.LanesFwd = defFwd; road.LanesBack = defBack;
                float width = ParseFloat(w.Tag("width"));
                float total = (defFwd + defBack) * road.LaneWidth + 2f * shoulder;
                if (width > 3f && width < 60f) { road.LaneWidth = Mathf.Clamp((width - 2f * shoulder) / (defFwd + defBack), 2.6f, 4.2f); total = width; }
                road.HalfWidth = total * 0.5f;
                float ms = ParseFloat(w.Tag("maxspeed"));
                road.SpeedLimit = (ms > 5f ? ms : speedKmh) / 3.6f;
                net.Roads.Add(road);
            }

            // Junctions: nodes where at least three road ends/sides meet
            foreach (var r in net.Roads)
                for (int i = 0; i < r.NodeIds.Count; i++)
                {
                    long id = r.NodeIds[i];
                    int add = i == 0 || i == r.NodeIds.Count - 1 ? 1 : 2;
                    net.Degree[id] = net.Degree.TryGetValue(id, out var d) ? d + add : add;
                }
            var signalNodes = new List<Vector2>();
            foreach (var n in map.Nodes.Values) if (n.Tag("highway") == "traffic_signals") signalNodes.Add(n.P);
            foreach (var r in net.Roads)
                for (int i = 0; i < r.NodeIds.Count; i++)
                {
                    long id = r.NodeIds[i];
                    if (net.Degree[id] < 3) continue;
                    if (!net.Junctions.TryGetValue(id, out var j)) { j = new Junction { NodeId = id, P = r.P[i] }; net.Junctions[id] = j; }
                    j.Arms.Add((r, i));
                }
            foreach (var j in net.Junctions.Values)
            {
                float maxHw = 0f; int majors = 0; var names = new HashSet<string>();
                foreach (var (road, _) in j.Arms)
                {
                    maxHw = Mathf.Max(maxHw, road.HalfWidth);
                    if (road.Class > j.MaxClass) j.MaxClass = road.Class;
                    if (road.Class >= RoadClass.Tertiary) { majors++; names.Add(road.Name + road.Class); }
                }
                j.Radius = maxHw + 2.5f;
                bool tagged = false;
                foreach (var s in signalNodes) if ((s - j.P).sqrMagnitude < 30f * 30f) { tagged = true; break; }
                // OSM tags few of Jeddah's signals: also signalize crossings of two different major roads
                // (inferred, documented in KNOWN_ISSUES)
                j.Signal = tagged || (j.MaxClass >= RoadClass.Secondary && majors >= 3 && names.Count >= 2);
                j.Names = names;
            }

            // Divided roads split one real intersection into several OSM nodes (each carriageway crosses
            // the other separately). Major-road junctions within 45 m form one intersection: it is
            // signalized as a whole, and all its nodes share one signal clock and phase axis.
            var list = new List<Junction>(net.Junctions.Values);
            var parent = new Dictionary<Junction, Junction>();
            foreach (var j in list) parent[j] = j;
            Junction Find(Junction x) { while (parent[x] != x) x = parent[x] = parent[parent[x]]; return x; }
            for (int a = 0; a < list.Count; a++)
                for (int b = a + 1; b < list.Count; b++)
                {
                    var ja = list[a]; var jb = list[b];
                    if (ja.MaxClass < RoadClass.Secondary || jb.MaxClass < RoadClass.Secondary) continue;
                    if ((ja.P - jb.P).sqrMagnitude < 45f * 45f) parent[Find(ja)] = Find(jb);
                }
            var clusters = new Dictionary<Junction, List<Junction>>();
            foreach (var j in list)
            {
                var root = Find(j);
                if (!clusters.TryGetValue(root, out var l)) clusters[root] = l = new List<Junction>();
                l.Add(j);
            }
            int cid = 0;
            foreach (var l in clusters.Values)
            {
                bool any = false; var names = new HashSet<string>(); var max = RoadClass.Service;
                foreach (var j in l) { any |= j.Signal; names.UnionWith(j.Names); if (j.MaxClass > max) max = j.MaxClass; }
                bool signal = any || (l.Count > 1 && max >= RoadClass.Secondary && names.Count >= 2);
                foreach (var j in l) { j.Signal = signal; j.Cluster = cid; }
                cid++;
            }
            int sig = 0, biggest = 0; foreach (var l in clusters.Values) { if (l[0].Signal) sig++; biggest = Mathf.Max(biggest, l.Count); }
            Debug.Log($"[MASAR] Intersections: {clusters.Count} clusters, {sig} signalized, largest {biggest} nodes");
            return net;
        }

        static bool TryClass(string hw, string service, out RoadClass c)
        {
            c = RoadClass.Residential;
            switch (hw)
            {
                case "motorway": case "trunk": c = RoadClass.Trunk; return true;
                case "primary": c = RoadClass.Primary; return true;
                case "secondary": c = RoadClass.Secondary; return true;
                case "tertiary": c = RoadClass.Tertiary; return true;
                case "motorway_link": case "trunk_link": case "primary_link": case "secondary_link": case "tertiary_link": c = RoadClass.Link; return true;
                case "unclassified": case "residential": case "living_street": c = RoadClass.Residential; return true;
                case "service": c = RoadClass.Service; return service != "drive-through" && service != "emergency_access";
                default: return false;
            }
        }

        static void Defaults(RoadClass c, bool oneway, out int fwd, out int back, out float laneW, out float shoulder, out float sidewalk, out float speedKmh)
        {
            switch (c)
            {
                case RoadClass.Trunk: fwd = oneway ? 3 : 3; back = oneway ? 0 : 3; laneW = 3.65f; shoulder = 0.8f; sidewalk = 4.5f; speedKmh = 90; break;
                case RoadClass.Primary: fwd = oneway ? 3 : 2; back = oneway ? 0 : 2; laneW = 3.6f; shoulder = 0.6f; sidewalk = 4.5f; speedKmh = 70; break;
                case RoadClass.Secondary: fwd = oneway ? 2 : 2; back = oneway ? 0 : 2; laneW = 3.5f; shoulder = 0.5f; sidewalk = 3.5f; speedKmh = 60; break;
                case RoadClass.Tertiary: fwd = oneway ? 2 : 1; back = oneway ? 0 : 1; laneW = 3.5f; shoulder = 0.6f; sidewalk = 3f; speedKmh = 50; break;
                case RoadClass.Link: fwd = 1; back = oneway ? 0 : 1; laneW = 4f; shoulder = 0.5f; sidewalk = 1.5f; speedKmh = 40; break;
                case RoadClass.Service: fwd = 1; back = oneway ? 0 : 1; laneW = 2.8f; shoulder = 0.2f; sidewalk = 1f; speedKmh = 20; break;
                default: fwd = 1; back = oneway ? 0 : 1; laneW = 3.4f; shoulder = 1.2f; sidewalk = 2.5f; speedKmh = 40; break;
            }
        }

        static int ParseInt(string s) => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;
        static float ParseFloat(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0f;
            int sp = s.IndexOf(' ');
            if (sp > 0) s = s.Substring(0, sp);
            return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0f;
        }

        /// <summary>Distance from p to the segment ab, and the segment parameter of the closest point.</summary>
        public static float SegDist(Vector2 p, Vector2 a, Vector2 b, out float t)
        {
            Vector2 ab = b - a;
            float l2 = ab.sqrMagnitude;
            t = l2 > 1e-6f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / l2) : 0f;
            return (p - (a + ab * t)).magnitude;
        }

        /// <summary>Polyline offset to the right by d (negative = left), with mitred joins (limited).</summary>
        public static List<Vector2> Offset(IList<Vector2> pts, float d)
        {
            var o = new List<Vector2>(pts.Count);
            for (int i = 0; i < pts.Count; i++)
            {
                Vector2 dirIn = i > 0 ? (pts[i] - pts[i - 1]).normalized : (pts[1] - pts[0]).normalized;
                Vector2 dirOut = i < pts.Count - 1 ? (pts[i + 1] - pts[i]).normalized : dirIn;
                Vector2 nIn = new Vector2(dirIn.y, -dirIn.x), nOut = new Vector2(dirOut.y, -dirOut.x);
                Vector2 n = (nIn + nOut);
                if (n.sqrMagnitude < 1e-6f) n = nIn; else n.Normalize();
                float cos = Mathf.Max(0.35f, Vector2.Dot(n, nIn));
                o.Add(pts[i] + n * (d / cos));
            }
            return o;
        }

        public static float Length(IList<Vector2> pts)
        {
            float l = 0f;
            for (int i = 1; i < pts.Count; i++) l += (pts[i] - pts[i - 1]).magnitude;
            return l;
        }

        /// <summary>Cuts a polyline to the arc-length range [s0, s1].</summary>
        public static List<Vector2> Trim(IList<Vector2> pts, float s0, float s1)
        {
            var o = new List<Vector2>();
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
                    if (o.Count == 0 || (o[o.Count - 1] - pa).sqrMagnitude > 1e-6f) o.Add(pa);
                    o.Add(Vector2.Lerp(a, b, tb));
                }
                s = e;
            }
            return o;
        }

        /// <summary>Arc length at point index i.</summary>
        public static float ArcAt(IList<Vector2> pts, int index)
        {
            float l = 0f;
            for (int i = 1; i <= index && i < pts.Count; i++) l += (pts[i] - pts[i - 1]).magnitude;
            return l;
        }
    }
}
