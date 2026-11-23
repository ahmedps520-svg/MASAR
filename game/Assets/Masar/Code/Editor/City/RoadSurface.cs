using System.Collections.Generic;
using Masar.World;
using UnityEngine;

namespace Masar.EditorTools.City
{
    /// <summary>
    /// Road, kerb, sidewalk and plot surfaces for an OSM road network, without polygon booleans:
    /// 1. Distance fields on a grid: R = distance to the nearest road edge (negative on the road),
    ///    S = distance to the nearest sidewalk outer edge.
    /// 2. Junction corners get real kerb radii from a morphological closing of the road region
    ///    (Euclidean distance transform), applied only where it adds area.
    /// 3. Marching squares turns the fields into non-overlapping meshes: road (y = 0), sidewalk and
    ///    plot (raised to the kerb height), split into chunks for culling. Kerbs are built along the
    ///    simplified R = 0 contour: a vertical face and a top stone strip, painted black/yellow on
    ///    main roads as in Jeddah.
    /// </summary>
    public sealed class RoadSurface
    {
        public const float KerbHeight = 0.15f;
        public const float Cell = 1.25f;
        public const float ChunkSize = 400f;
        const float CornerRadius = 5.5f;

        public readonly int Nx, Nz;
        public readonly Vector2 Origin;
        /// <summary>All roads, flyovers included: used to keep buildings, trees and parks off every road.</summary>
        public float[] R, S;
        public byte[] Cls;
        /// <summary>Ground-level roads only (no flyover, ramp or trench segments): the surfaces that are built at y = 0.</summary>
        public float[] Rg, Sg;
        /// <summary>Nearest ground road (index into the network) per grid point, -1 for none.</summary>
        public int[] Rid;
        /// <summary>Distance to the footprint of any road segment off the ground (negative inside).</summary>
        public float[] E;
        /// <summary>Distance to an open trench (negative inside): no ground surface there.</summary>
        public float[] V;

        readonly RoadNetwork _net;

        public RoadSurface(RoadNetwork net, Rect area)
        {
            _net = net;
            Origin = area.min;
            Nx = Mathf.CeilToInt(area.width / Cell) + 1;
            Nz = Mathf.CeilToInt(area.height / Cell) + 1;
            ComputeFields(null, out R, out S, out Cls, out _);
            ComputeFields((r, s) => !SegGraded(r, s), out Rg, out Sg, out _, out Rid);
            Footprints();
        }

        public static bool SegGraded(RoadNetwork.Road r, int s) => Mathf.Abs(r.H[s - 1]) > RoadNetwork.GroundTol || Mathf.Abs(r.H[s]) > RoadNetwork.GroundTol;
        static bool SegDepressed(RoadNetwork.Road r, int s) => r.H[s - 1] < -RoadNetwork.GroundTol || r.H[s] < -RoadNetwork.GroundTol;

        /// <summary>
        /// Ground surfaces above a trench are cut away, except a ground road that crosses it (with its
        /// sidewalks): it sits on a deck over the trench. Returns a field to combine with Max().
        /// </summary>
        public float VoidField(int k)
        {
            if (V[k] >= 0f) return -1000f;
            int id = Rid[k];
            if (id >= 0 && !_net.Roads[id].Graded && Sg[k] < 0f) return -1000f;
            return -V[k];
        }

        public bool Crossing(int k) { int id = Rid[k]; return id >= 0 && !_net.Roads[id].Graded && Sg[k] < 0f; }

        public int Index(Vector2 p)
        {
            int i = Mathf.Clamp(Mathf.RoundToInt((p.x - Origin.x) / Cell), 0, Nx - 1), j = Mathf.Clamp(Mathf.RoundToInt((p.y - Origin.y) / Cell), 0, Nz - 1);
            return j * Nx + i;
        }

        void Footprints()
        {
            int n = Nx * Nz;
            E = new float[n]; V = new float[n];
            for (int k = 0; k < n; k++) { E[k] = 1000f; V[k] = 1000f; }
            foreach (var r in _net.Roads)
            {
                if (!r.Graded) continue;
                float reach = r.HalfWidth + 6f;
                for (int s = 1; s < r.P.Count; s++)
                {
                    if (!SegGraded(r, s)) continue;
                    bool trench = SegDepressed(r, s) && !r.Tunnel;
                    Vector2 a = r.P[s - 1], b = r.P[s];
                    Box(a, b, reach, out int i0, out int i1, out int j0, out int j1);
                    for (int j = j0; j <= j1; j++)
                        for (int i = i0; i <= i1; i++)
                        {
                            int k = j * Nx + i;
                            var p = GridPos(i, j);
                            float dist = RoadNetwork.SegDist(p, a, b, out float t);
                            float e = dist - r.HalfWidth;
                            if (e < E[k]) E[k] = e;
                            if (!trench) continue;
                            // Flat-ended: the trench starts and stops at its points, never with a round cap
                            Vector2 ab = b - a; float l2 = ab.sqrMagnitude;
                            float tt = l2 > 1e-6f ? Vector2.Dot(p - a, ab) / l2 : 0f;
                            if (tt < -0.02f || tt > 1.02f) continue;
                            float v = dist - (r.HalfWidth + 0.6f);
                            if (v < V[k]) V[k] = v;
                        }
                }
            }
        }

        void Box(Vector2 a, Vector2 b, float reach, out int i0, out int i1, out int j0, out int j1)
        {
            i0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.x, b.x) - reach - Origin.x) / Cell));
            i1 = Mathf.Min(Nx - 1, Mathf.CeilToInt((Mathf.Max(a.x, b.x) + reach - Origin.x) / Cell));
            j0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.y, b.y) - reach - Origin.y) / Cell));
            j1 = Mathf.Min(Nz - 1, Mathf.CeilToInt((Mathf.Max(a.y, b.y) + reach - Origin.y) / Cell));
        }

        public Vector2 GridPos(int i, int j) => Origin + new Vector2(i * Cell, j * Cell);

        /// <summary>Bilinear sample of a field (large positive outside the grid).</summary>
        public float Sample(float[] f, Vector2 p)
        {
            float gx = (p.x - Origin.x) / Cell, gz = (p.y - Origin.y) / Cell;
            int i = Mathf.FloorToInt(gx), j = Mathf.FloorToInt(gz);
            if (i < 0 || j < 0 || i >= Nx - 1 || j >= Nz - 1) return 1000f;
            float tx = gx - i, tz = gz - j;
            float a = f[j * Nx + i], b = f[j * Nx + i + 1], c = f[(j + 1) * Nx + i], d = f[(j + 1) * Nx + i + 1];
            return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), tz);
        }

        public RoadClass NearestClass(Vector2 p)
        {
            int i = Mathf.Clamp(Mathf.RoundToInt((p.x - Origin.x) / Cell), 0, Nx - 1), j = Mathf.Clamp(Mathf.RoundToInt((p.y - Origin.y) / Cell), 0, Nz - 1);
            return (RoadClass)Cls[j * Nx + i];
        }

        // ------------------------------------------------------------------ fields

        /// <summary>Road (R) and sidewalk-edge (S) distance fields over the segments that pass the filter (all when null).</summary>
        void ComputeFields(System.Func<RoadNetwork.Road, int, bool> include, out float[] R, out float[] S, out byte[] cls, out int[] rid)
        {
            int n = Nx * Nz;
            var d = new float[n];
            var dsw = new float[n];
            var sw = new float[n];
            cls = new byte[n];
            rid = new int[n];
            for (int k = 0; k < n; k++) { d[k] = 1000f; dsw[k] = 1000f; rid[k] = -1; }
            foreach (var r in _net.Roads)
            {
                float reach = r.HalfWidth + r.Sidewalk + CornerRadius + 3f;
                for (int s = 1; s < r.P.Count; s++)
                {
                    if (include != null && !include(r, s)) continue;
                    Vector2 a = r.P[s - 1], b = r.P[s];
                    Box(a, b, reach, out int i0, out int i1, out int j0, out int j1);
                    for (int j = j0; j <= j1; j++)
                        for (int i = i0; i <= i1; i++)
                        {
                            float dist = RoadNetwork.SegDist(GridPos(i, j), a, b, out _);
                            int k = j * Nx + i;
                            float v = dist - r.HalfWidth;
                            if (v < d[k]) { d[k] = v; cls[k] = (byte)r.Class; sw[k] = r.Sidewalk; rid[k] = r.Index; }
                            float v2 = v - r.Sidewalk;
                            if (v2 < dsw[k]) dsw[k] = v2;
                        }
                }
            }

            // Closing of the road region by CornerRadius: dilate (d < r), then erode by the distance to the outside
            var outside = new bool[n];
            for (int k = 0; k < n; k++) outside[k] = d[k] >= CornerRadius;
            var edt = DistanceTransform(outside, Nx, Nz);
            R = new float[n];
            S = new float[n];
            for (int k = 0; k < n; k++)
            {
                float closed = CornerRadius - edt[k] * Cell;
                // The closing only adds area in concave corners; elsewhere keep the exact distance (bias hides grid error)
                float rr = Mathf.Min(d[k], closed + 0.45f);
                R[k] = rr;
                // Sidewalk outer edge: exact along roads; follows the rounded kerb only where the closing added road
                S[k] = rr < d[k] - 0.05f ? Mathf.Min(dsw[k], rr - sw[k]) : dsw[k];
            }
        }

        /// <summary>Exact Euclidean distance transform (Felzenszwalb-Huttenlocher), in cells, to the nearest seed.</summary>
        static float[] DistanceTransform(bool[] seed, int nx, int nz)
        {
            const float Inf = 1e12f;
            var g = new float[nx * nz];
            for (int k = 0; k < g.Length; k++) g[k] = seed[k] ? 0f : Inf;
            int m = Mathf.Max(nx, nz);
            var f = new float[m]; var dd = new float[m]; var v = new int[m]; var z = new float[m + 1];
            for (int i = 0; i < nx; i++)
            {
                for (int j = 0; j < nz; j++) f[j] = g[j * nx + i];
                Edt1D(f, nz, dd, v, z);
                for (int j = 0; j < nz; j++) g[j * nx + i] = dd[j];
            }
            for (int j = 0; j < nz; j++)
            {
                for (int i = 0; i < nx; i++) f[i] = g[j * nx + i];
                Edt1D(f, nx, dd, v, z);
                for (int i = 0; i < nx; i++) g[j * nx + i] = Mathf.Sqrt(dd[i]);
            }
            return g;
        }

        static void Edt1D(float[] f, int n, float[] d, int[] v, float[] z)
        {
            int k = 0; v[0] = 0; z[0] = float.NegativeInfinity; z[1] = float.PositiveInfinity;
            for (int q = 1; q < n; q++)
            {
                float s;
                while (true)
                {
                    s = ((f[q] + q * q) - (f[v[k]] + v[k] * v[k])) / (2f * q - 2f * v[k]);
                    if (s <= z[k] && k > 0) k--; else break;
                }
                if (s <= z[k]) { v[0] = q; z[0] = float.NegativeInfinity; z[1] = float.PositiveInfinity; k = 0; continue; }
                k++; v[k] = q; z[k] = s; z[k + 1] = float.PositiveInfinity;
            }
            k = 0;
            for (int q = 0; q < n; q++)
            {
                while (z[k + 1] < q) k++;
                d[q] = (q - v[k]) * (q - v[k]) + f[v[k]];
            }
        }

        // ------------------------------------------------------------------ surfaces

        public delegate float FieldAt(int k);

        /// <summary>Region {F &lt; 0} as an up-facing mesh at height y, world-space UVs, into per-chunk builders.</summary>
        public void Region(FieldAt field, float y, float uvScale, System.Func<Vector2, MeshBuilder> builderFor)
        {
            var vals = new float[4];
            var pts = new List<Vector2>(8);
            for (int j = 0; j < Nz - 1; j++)
            {
                int runStart = -1;
                for (int i = 0; i <= Nx - 1; i++)
                {
                    bool full = false;
                    if (i < Nx - 1)
                    {
                        vals[0] = field(j * Nx + i); vals[1] = field(j * Nx + i + 1);
                        vals[2] = field((j + 1) * Nx + i + 1); vals[3] = field((j + 1) * Nx + i);
                        full = vals[0] < 0 && vals[1] < 0 && vals[2] < 0 && vals[3] < 0;
                        // Runs never cross a chunk boundary, so each run lands in one chunk
                        if (full && runStart >= 0 && ChunkOf(GridPos(i, j)) != ChunkOf(GridPos(runStart, j))) { EmitRun(runStart, i, j, y, uvScale, builderFor); runStart = i; continue; }
                    }
                    if (full) { if (runStart < 0) runStart = i; continue; }
                    if (runStart >= 0) { EmitRun(runStart, i, j, y, uvScale, builderFor); runStart = -1; }
                    if (i >= Nx - 1) break;
                    if (vals[0] >= 0 && vals[1] >= 0 && vals[2] >= 0 && vals[3] >= 0) continue;
                    // Partial cell: walk corners and edges counter-clockwise, keep inside corners + crossings
                    pts.Clear();
                    Vector2 p0 = GridPos(i, j);
                    Vector2[] c = { p0, p0 + new Vector2(Cell, 0), p0 + new Vector2(Cell, Cell), p0 + new Vector2(0, Cell) };
                    for (int e = 0; e < 4; e++)
                    {
                        int e2 = (e + 1) & 3;
                        if (vals[e] < 0) pts.Add(c[e]);
                        if ((vals[e] < 0) != (vals[e2] < 0))
                        {
                            float t = vals[e] / (vals[e] - vals[e2]);
                            pts.Add(Vector2.Lerp(c[e], c[e2], Mathf.Clamp(t, 0.02f, 0.98f)));
                        }
                    }
                    if (pts.Count < 3) continue;
                    var b = builderFor(p0 + new Vector2(Cell * 0.5f, Cell * 0.5f));
                    int baseIdx = b.Count;
                    foreach (var p in pts) b.AddVertex(new Vector3(p.x, y, p.y), Vector3.up, p / uvScale);
                    for (int t = 1; t + 1 < pts.Count; t++) { b.T.Add(baseIdx); b.T.Add(baseIdx + t); b.T.Add(baseIdx + t + 1); }
                }
            }
        }

        void EmitRun(int i0, int i1, int j, float y, float uvScale, System.Func<Vector2, MeshBuilder> builderFor)
        {
            Vector2 a = GridPos(i0, j), b = GridPos(i1, j + 1);
            builderFor((a + b) * 0.5f).FloorRect(a.x, a.y, b.x, b.y, y, uvScale);
        }

        public static Vector2Int ChunkOf(Vector2 p) => new Vector2Int(Mathf.FloorToInt(p.x / ChunkSize), Mathf.FloorToInt(p.y / ChunkSize));

        // ------------------------------------------------------------------ contours (kerbs)

        /// <summary>Iso-contour F = 0 as polylines (marching squares, chained), simplified to a tolerance.</summary>
        public List<List<Vector2>> Contours(float[] f, float tolerance)
        {
            // Crossing point id = grid edge id: horizontal edge (i,j)->(i+1,j) = 2k, vertical (i,j)->(i,j+1) = 2k+1
            var pos = new Dictionary<int, Vector2>();
            var adj = new Dictionary<int, List<int>>();
            void Link(int a, int b)
            {
                if (!adj.TryGetValue(a, out var la)) adj[a] = la = new List<int>(2);
                if (!adj.TryGetValue(b, out var lb)) adj[b] = lb = new List<int>(2);
                la.Add(b); lb.Add(a);
            }
            int Edge(int i, int j, bool vertical)
            {
                int id = (j * Nx + i) * 2 + (vertical ? 1 : 0);
                if (!pos.ContainsKey(id))
                {
                    int k0 = j * Nx + i, k1 = vertical ? (j + 1) * Nx + i : j * Nx + i + 1;
                    float t = Mathf.Clamp(f[k0] / (f[k0] - f[k1]), 0.02f, 0.98f);
                    pos[id] = Vector2.Lerp(GridPos(i, j), vertical ? GridPos(i, j + 1) : GridPos(i + 1, j), t);
                }
                return id;
            }
            for (int j = 0; j < Nz - 1; j++)
                for (int i = 0; i < Nx - 1; i++)
                {
                    float v0 = f[j * Nx + i], v1 = f[j * Nx + i + 1], v2 = f[(j + 1) * Nx + i + 1], v3 = f[(j + 1) * Nx + i];
                    int code = (v0 < 0 ? 1 : 0) | (v1 < 0 ? 2 : 0) | (v2 < 0 ? 4 : 0) | (v3 < 0 ? 8 : 0);
                    if (code == 0 || code == 15) continue;
                    // Edges: bottom (i,j)h, right (i+1,j)v, top (i,j+1)h, left (i,j)v
                    int B() => Edge(i, j, false); int Rt() => Edge(i + 1, j, true); int T() => Edge(i, j + 1, false); int L() => Edge(i, j, true);
                    switch (code)
                    {
                        case 1: case 14: Link(L(), B()); break;
                        case 2: case 13: Link(B(), Rt()); break;
                        case 3: case 12: Link(L(), Rt()); break;
                        case 4: case 11: Link(Rt(), T()); break;
                        case 6: case 9: Link(B(), T()); break;
                        case 7: case 8: Link(L(), T()); break;
                        case 5: case 10:
                        {
                            float center = (v0 + v1 + v2 + v3) * 0.25f;
                            bool joined = (center < 0) == (code == 5);
                            if (joined) { Link(L(), T()); Link(B(), Rt()); }
                            else { Link(L(), B()); Link(Rt(), T()); }
                            break;
                        }
                    }
                }
            var result = new List<List<Vector2>>();
            var used = new HashSet<int>();
            foreach (var start in adj.Keys)
            {
                if (used.Contains(start)) continue;
                // Walk to one end first (open contours end at the grid border), then collect
                int cur = start, prev = -1;
                for (int guard = 0; guard < 1_000_000; guard++)
                {
                    var l = adj[cur];
                    int next = l[0] != prev ? l[0] : (l.Count > 1 ? l[1] : -1);
                    if (l.Count < 2 || next == start) break;
                    prev = cur; cur = next;
                }
                var line = new List<Vector2>();
                prev = -1;
                int first = cur;
                while (cur >= 0 && !used.Contains(cur))
                {
                    used.Add(cur);
                    line.Add(pos[cur]);
                    var l = adj[cur];
                    int next = -1;
                    foreach (var c in l) if (c != prev && !used.Contains(c)) { next = c; break; }
                    if (next < 0 && l.Contains(first) && line.Count > 2 && prev != first) line.Add(pos[first]);   // closed loop
                    prev = cur; cur = next;
                }
                if (line.Count >= 2) result.Add(Simplify(line, tolerance));
            }
            return result;
        }

        static List<Vector2> Simplify(List<Vector2> pts, float tol)
        {
            if (pts.Count < 3) return pts;
            var keep = new bool[pts.Count];
            keep[0] = keep[pts.Count - 1] = true;
            var stack = new Stack<(int, int)>();
            stack.Push((0, pts.Count - 1));
            while (stack.Count > 0)
            {
                var (a, b) = stack.Pop();
                float best = 0f; int idx = -1;
                for (int i = a + 1; i < b; i++)
                {
                    float dd = RoadNetwork.SegDist(pts[i], pts[a], pts[b], out _);
                    if (dd > best) { best = dd; idx = i; }
                }
                if (idx >= 0 && best > tol) { keep[idx] = true; stack.Push((a, idx)); stack.Push((idx, b)); }
            }
            var o = new List<Vector2>();
            for (int i = 0; i < pts.Count; i++) if (keep[i]) o.Add(pts[i]);
            return o;
        }
    }
}
