using System.Collections.Generic;
using UnityEngine;

namespace Masar.EditorTools.City
{
    /// <summary>
    /// Vertical alignment of the road network from the OSM tags: flyovers (bridge / layer &gt; 0),
    /// tunnels and sunken expressways (tunnel / layer &lt; 0), and the ramps that join them to the ground.
    /// 1. Deck heights from what each structure crosses in plan: a road one layer above a ground road
    ///    is a flyover (Deck metres up); below one it is in a trench or tunnel (Deck metres down).
    ///    A bridge that only crosses a sunken road sits at ground level, as over Madinah Road.
    ///    Tagged structures that cross no road stay flat (canal bridges, parking decks).
    /// 2. The structures' points are fixed at their deck height. Ground roads within ramp reach
    ///    (height / MaxGrade, along the network) get free heights, ending at the first at-grade
    ///    junction with a cross street; everything else is fixed at 0.
    /// 3. Free heights solve a weighted Laplace equation (linear ramps along chains), then a light
    ///    smoothing rounds the crests and sags.
    /// OSM does not record real deck levels or ramp lengths: these are standard urban values, not
    /// survey data (KNOWN_ISSUES).
    /// </summary>
    public static class RoadGrades
    {
        /// <summary>Deck height above the road crossed: 5.5 m clearance plus the deck structure.</summary>
        public const float Deck = 7f;
        /// <summary>Steepest ramp: 5.5%, a usual urban flyover approach.</summary>
        public const float MaxGrade = 0.055f;
        /// <summary>Level deck kept each side of the outermost road a structure crosses.</summary>
        public const float DeckMargin = 25f;

        public static int Structures, Ramps;
        public static float MaxH, MinH, SteepestGrade;

        public static void Solve(RoadNetwork net)
        {
            var roads = net.Roads;
            foreach (var r in roads) { r.H.Clear(); for (int i = 0; i < r.P.Count; i++) r.H.Add(0f); r.Graded = false; }

            // ---- 1. crossings in plan between roads of different layers
            var crossed = new HashSet<int>[roads.Count];
            for (int i = 0; i < roads.Count; i++) crossed[i] = new HashSet<int>();
            // Segment index of every plan crossing, per road and other road
            var crossAt = new List<(int other, int seg)>[roads.Count];
            for (int i = 0; i < roads.Count; i++) crossAt[i] = new List<(int, int)>();
            const float Cell = 40f;
            var hash = new Dictionary<Vector2Int, List<(int road, int seg)>>();
            for (int ri = 0; ri < roads.Count; ri++)
            {
                var r = roads[ri];
                for (int s = 1; s < r.P.Count; s++)
                {
                    Vector2 a = r.P[s - 1], b = r.P[s];
                    var c0 = new Vector2Int(Mathf.FloorToInt(Mathf.Min(a.x, b.x) / Cell), Mathf.FloorToInt(Mathf.Min(a.y, b.y) / Cell));
                    var c1 = new Vector2Int(Mathf.FloorToInt(Mathf.Max(a.x, b.x) / Cell), Mathf.FloorToInt(Mathf.Max(a.y, b.y) / Cell));
                    for (int x = c0.x; x <= c1.x; x++)
                        for (int y = c0.y; y <= c1.y; y++)
                        {
                            var k = new Vector2Int(x, y);
                            if (!hash.TryGetValue(k, out var l)) hash[k] = l = new List<(int, int)>();
                            l.Add((ri, s));
                        }
                }
            }
            foreach (var l in hash.Values)
                for (int a = 0; a < l.Count; a++)
                    for (int b = a + 1; b < l.Count; b++)
                    {
                        var (ra, sa) = l[a]; var (rb, sb) = l[b];
                        if (ra == rb || roads[ra].Layer == roads[rb].Layer) continue;
                        var A = roads[ra]; var B = roads[rb];
                        if (Intersect(A.P[sa - 1], A.P[sa], B.P[sb - 1], B.P[sb]))
                        {
                            crossed[ra].Add(rb); crossed[rb].Add(ra);
                            crossAt[ra].Add((rb, sa)); crossAt[rb].Add((ra, sb));
                        }
                    }

            // ---- 2. deck heights per structure
            var target = new float[roads.Count];
            for (int i = 0; i < roads.Count; i++)
            {
                var r = roads[i];
                if (r.Layer >= 0) continue;
                foreach (int o in crossed[i]) if (roads[o].Layer > r.Layer) { target[i] = Deck * r.Layer; break; }
            }
            var ups = new List<int>();
            for (int i = 0; i < roads.Count; i++) if (roads[i].Layer > 0) ups.Add(i);
            ups.Sort((x, y) => roads[x].Layer.CompareTo(roads[y].Layer));
            foreach (int i in ups)
            {
                float best = 0f; bool any = false;
                foreach (int o in crossed[i])
                    if (roads[o].Layer < roads[i].Layer) { best = any ? Mathf.Max(best, target[o] + Deck) : target[o] + Deck; any = true; }
                target[i] = any ? Mathf.Max(0f, best) : 0f;
            }

            // ---- deck span: the structure is held at its deck height from DeckMargin before its first
            //      crossing to DeckMargin after its last; beyond that its own approaches ramp like any road
            var deckFrom = new int[roads.Count]; var deckTo = new int[roads.Count];
            for (int i = 0; i < roads.Count; i++)
            {
                var r = roads[i];
                deckFrom[i] = 0; deckTo[i] = r.P.Count - 1;
                if (Mathf.Abs(target[i]) < 0.01f) continue;
                int lo = int.MaxValue, hi = -1;
                foreach (var (o, seg) in crossAt[i])
                {
                    bool counts = target[i] > 0f ? roads[o].Layer < r.Layer : roads[o].Layer > r.Layer;
                    if (!counts) continue;
                    lo = Mathf.Min(lo, seg - 1); hi = Mathf.Max(hi, seg);
                }
                if (hi < 0) continue;
                float back = 0f;
                while (lo > 0 && back < DeckMargin) { back += (r.P[lo] - r.P[lo - 1]).magnitude; lo--; }
                float fwd = 0f;
                while (hi < r.P.Count - 1 && fwd < DeckMargin) { fwd += (r.P[hi + 1] - r.P[hi]).magnitude; hi++; }
                deckFrom[i] = lo; deckTo[i] = hi;
            }

            // ---- vertex graph (shared OSM nodes are shared vertices)
            var vid = new Dictionary<long, int>();
            var adj = new List<List<(int v, float w)>>();
            var fixedVal = new List<float>(); var isFixed = new List<bool>(); var fixedN = new List<int>();
            var roadVerts = new int[roads.Count][];
            for (int ri = 0; ri < roads.Count; ri++)
            {
                var r = roads[ri];
                roadVerts[ri] = new int[r.P.Count];
                for (int i = 0; i < r.P.Count; i++)
                {
                    if (!vid.TryGetValue(r.NodeIds[i], out int v))
                    {
                        v = adj.Count; vid[r.NodeIds[i]] = v;
                        adj.Add(new List<(int, float)>()); fixedVal.Add(0f); isFixed.Add(false); fixedN.Add(0);
                    }
                    roadVerts[ri][i] = v;
                    if (i > 0)
                    {
                        int u = roadVerts[ri][i - 1];
                        float len = Mathf.Max(0.05f, (r.P[i] - r.P[i - 1]).magnitude);
                        adj[u].Add((v, len)); adj[v].Add((u, len));
                    }
                    if (Mathf.Abs(target[ri]) > 0.01f && i >= deckFrom[ri] && i <= deckTo[ri])
                    {
                        // Shared by two structures at different decks: the mean
                        fixedVal[v] = (fixedVal[v] * fixedN[v] + target[ri]) / (fixedN[v] + 1); fixedN[v]++;
                        isFixed[v] = true;
                    }
                }
            }
            int nv = adj.Count;
            var h = new float[nv];
            for (int v = 0; v < nv; v++) h[v] = isFixed[v] ? fixedVal[v] : 0f;

            // ---- pins: at-grade junctions with a cross street end a ramp
            var pinned = new bool[nv];
            foreach (var j in net.Junctions.Values)
            {
                if (!vid.TryGetValue(j.NodeId, out int v) || isFixed[v]) continue;
                var names = new HashSet<string>();
                foreach (var (road, _) in j.Arms)
                    if (Mathf.Abs(target[road.Index]) < 0.01f && road.Class != RoadClass.Link) names.Add(road.Name);
                if (names.Count >= 2) pinned[v] = true;
            }

            // ---- reach: Dijkstra from the structures through ground roads
            var dist = new float[nv]; var reach = new float[nv];
            for (int v = 0; v < nv; v++) { dist[v] = float.MaxValue; reach[v] = 0f; }
            var open = new SortedSet<(float d, int v)>();
            for (int v = 0; v < nv; v++)
                if (isFixed[v]) { dist[v] = 0f; reach[v] = Mathf.Abs(fixedVal[v]) / MaxGrade; open.Add((0f, v)); }
            while (open.Count > 0)
            {
                var (d, u) = open.Min; open.Remove(open.Min);
                if (d > dist[u]) continue;
                if (pinned[u] && !isFixed[u]) continue;   // ramps stop at the cross street
                foreach (var (w, len) in adj[u])
                {
                    if (isFixed[w]) continue;
                    float nd = d + len;
                    if (nd < dist[w] && nd < reach[u] + 15f)
                    {
                        if (dist[w] != float.MaxValue) open.Remove((dist[w], w));
                        dist[w] = nd; reach[w] = reach[u];
                        open.Add((nd, w));
                    }
                }
            }
            var free = new List<int>();
            for (int v = 0; v < nv; v++)
                if (!isFixed[v] && !pinned[v] && dist[v] < reach[v]) free.Add(v);
            // Initial guess: straight ramps
            foreach (int v in free) h[v] = 0f;

            // ---- 3. Laplace (Gauss-Seidel, inverse-length weights), then smoothing on chains
            for (int it = 0; it < 600; it++)
                foreach (int v in free)
                {
                    float sw = 0f, sh = 0f;
                    foreach (var (w, len) in adj[v]) { float wt = 1f / len; sw += wt; sh += wt * h[w]; }
                    if (sw > 0f) h[v] = sh / sw;
                }
            var inRamp = new bool[nv];
            foreach (int v in free) inRamp[v] = true;
            for (int v = 0; v < nv; v++) if (isFixed[v]) foreach (var (w, _) in adj[v]) if (inRamp[w]) inRamp[v] = true;
            var tmp = new float[nv];
            for (int pass = 0; pass < 6; pass++)
            {
                System.Array.Copy(h, tmp, nv);
                for (int v = 0; v < nv; v++)
                {
                    if (adj[v].Count != 2 || pinned[v]) continue;
                    if (!inRamp[v] && !inRamp[adj[v][0].v] && !inRamp[adj[v][1].v]) continue;
                    tmp[v] = 0.5f * h[v] + 0.25f * (h[adj[v][0].v] + h[adj[v][1].v]);
                }
                System.Array.Copy(tmp, h, nv);
            }

            // ---- write back + stats
            Structures = 0; Ramps = free.Count; MaxH = 0f; MinH = 0f; SteepestGrade = 0f;
            for (int ri = 0; ri < roads.Count; ri++)
            {
                var r = roads[ri];
                for (int i = 0; i < r.P.Count; i++)
                {
                    r.H[i] = Mathf.Abs(h[roadVerts[ri][i]]) < RoadNetwork.GroundTol ? 0f : h[roadVerts[ri][i]];
                    if (Mathf.Abs(r.H[i]) > RoadNetwork.GroundTol) r.Graded = true;
                    MaxH = Mathf.Max(MaxH, r.H[i]); MinH = Mathf.Min(MinH, r.H[i]);
                    if (i > 0)
                    {
                        float l = (r.P[i] - r.P[i - 1]).magnitude;
                        if (l > 1f) SteepestGrade = Mathf.Max(SteepestGrade, Mathf.Abs(r.H[i] - r.H[i - 1]) / l);
                    }
                }
                if (Mathf.Abs(target[ri]) > 0.01f) Structures++;
            }
            Debug.Log($"[MASAR] Grades: {Structures} structures (deck {MinH:F1} .. {MaxH:F1} m), {Ramps} ramp points, steepest {SteepestGrade * 100f:F1}%");
        }

        /// <summary>Logs each graded road (name, tags, height range, centre) and the steepest segments.</summary>
        public static void Report(RoadNetwork net)
        {
            var steep = new List<(float g, string name, Vector2 at, float h0, float h1)>();
            foreach (var r in net.Roads)
            {
                if (!r.Graded && r.Layer == 0 && !r.Bridge && !r.Tunnel) continue;
                float lo = float.MaxValue, hi = float.MinValue, len = 0f;
                for (int i = 0; i < r.P.Count; i++)
                {
                    lo = Mathf.Min(lo, r.H[i]); hi = Mathf.Max(hi, r.H[i]);
                    if (i == 0) continue;
                    float l = (r.P[i] - r.P[i - 1]).magnitude; len += l;
                    if (l > 1f) steep.Add((Mathf.Abs(r.H[i] - r.H[i - 1]) / l, r.Name, r.P[i], r.H[i - 1], r.H[i]));
                }
                Vector2 c = r.P[r.P.Count / 2];
                Debug.Log($"[MASAR] Grade road '{r.Name}' {r.Class} layer {r.Layer}{(r.Bridge ? " bridge" : "")}{(r.Tunnel ? " tunnel" : "")} " +
                          $"h {lo:F1}..{hi:F1} len {len:F0} m, ends ({r.P[0].x:F0},{r.P[0].y:F0}) ({r.P[r.P.Count - 1].x:F0},{r.P[r.P.Count - 1].y:F0}) mid ({c.x:F0},{c.y:F0})");
            }
            steep.Sort((a, b) => b.g.CompareTo(a.g));
            for (int i = 0; i < Mathf.Min(15, steep.Count); i++)
                Debug.Log($"[MASAR] Steep {steep[i].g * 100f:F1}% '{steep[i].name}' at ({steep[i].at.x:F0},{steep[i].at.y:F0}) {steep[i].h0:F1} -> {steep[i].h1:F1}");
        }

        static bool Intersect(Vector2 p1, Vector2 p2, Vector2 p3, Vector2 p4)
        {
            float d1 = Cross(p4 - p3, p1 - p3), d2 = Cross(p4 - p3, p2 - p3), d3 = Cross(p2 - p1, p3 - p1), d4 = Cross(p2 - p1, p4 - p1);
            return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
        }
        static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
    }
}
