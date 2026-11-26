using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Masar.World.Traffic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Masar.EditorTools.City
{
    /// <summary>
    /// QA for the generated city (2026-11-26 owner report): asphalt that no traffic lane runs on (wide
    /// open spaces with no kerb), and pedestrian walkways that pass through buildings.
    /// Writes build/diag/city-map.png (1 px = 1.25 m, north up) and logs each open space.
    /// -executeMethod Masar.EditorTools.City.CityOpenSpaceCheck.Run
    /// </summary>
    public static class CityOpenSpaceCheck
    {
        const float LaneReach = 6f;   // asphalt further than this from every lane centre is "open"

        public static void Run()
        {
            string json = Path.GetFullPath(Path.Combine(Application.dataPath, $"../../data/osm/{OsmCityBuilder.MapFile}.json"));
            var map = OsmMap.Load(json, OsmCityBuilder.South, OsmCityBuilder.West, OsmCityBuilder.North, OsmCityBuilder.East);
            var net = RoadNetwork.Build(map);
            var graph = AssetDatabase.LoadAssetAtPath<CityGraph>(OsmCityBuilder.GraphPath);
            var surf = new RoadSurface(net, map.Bounds, LaneGraphBuilder.GroundConnectors(graph));
            int nx = surf.Nx, nz = surf.Nz, n = nx * nz;
            float cell = RoadSurface.Cell;

            // Distance from every grid point to the nearest lane centre (road lanes and junction connectors)
            var seed = new bool[n];
            for (int l = 0; l < graph.lanes.Length; l++)
                for (float s = 0f; s <= graph.lanes[l].length; s += 0.6f)
                {
                    graph.Sample(l, s, out var p, out _);
                    seed[surf.Index(new Vector2(p.x, p.z))] = true;
                }
            var laneDist = RoadSurface.DistanceTransform(seed, nx, nz);

            // Raw (before the corner closing) ground-road distance: negative on a modelled road width
            var raw = new float[n];
            for (int k = 0; k < n; k++) raw[k] = 1000f;
            foreach (var r in net.Roads)
                for (int s = 1; s < r.P.Count; s++)
                {
                    if (RoadSurface.SegGraded(r, s)) continue;
                    Vector2 a = r.P[s - 1], b = r.P[s];
                    float reach = r.HalfWidth + 2f;
                    int i0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.x, b.x) - reach - surf.Origin.x) / cell)), i1 = Mathf.Min(nx - 1, Mathf.CeilToInt((Mathf.Max(a.x, b.x) + reach - surf.Origin.x) / cell));
                    int j0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.y, b.y) - reach - surf.Origin.y) / cell)), j1 = Mathf.Min(nz - 1, Mathf.CeilToInt((Mathf.Max(a.y, b.y) + reach - surf.Origin.y) / cell));
                    for (int j = j0; j <= j1; j++)
                        for (int i = i0; i <= i1; i++)
                        {
                            float v = RoadNetwork.SegDist(surf.GridPos(i, j), a, b, out _) - r.HalfWidth;
                            int k = j * nx + i;
                            if (v < raw[k]) raw[k] = v;
                        }
                }

            // Open asphalt: built (ground road surface) but far from any lane
            var open = new bool[n];
            int asphalt = 0, openCells = 0, openFilled = 0;
            for (int k = 0; k < n; k++)
            {
                if (surf.Rg[k] >= 0f || surf.VoidField(k) >= 0f) continue;
                asphalt++;
                if (laneDist[k] * cell <= LaneReach) continue;
                open[k] = true; openCells++;
                if (raw[k] >= 0f) openFilled++;
            }
            float a2 = cell * cell;
            // Lanes and junction connectors at ground level that run off the asphalt (onto a kerb or an island)
            int roadPts = 0, roadOff = 0, connPts = 0, connOff = 0;
            var offList = new List<Vector2>();
            for (int l = 0; l < graph.lanes.Length; l++)
                for (float s = 0f; s <= graph.lanes[l].length; s += 1f)
                {
                    graph.Sample(l, s, out var p, out _);
                    if (Mathf.Abs(p.y) > 0.3f) continue;
                    var q2 = new Vector2(p.x, p.z);
                    bool off = surf.Sample(surf.Rg, q2) > 0f && surf.VoidField(surf.Index(q2)) < 0f;
                    if (graph.lanes[l].junction >= 0) { connPts++; if (off) { connOff++; offList.Add(q2); } }
                    else { roadPts++; if (off) { roadOff++; offList.Add(q2); } }
                }
            var offCells = new Dictionary<Vector2Int, int>();
            foreach (var p in offList) { var c = new Vector2Int(Mathf.RoundToInt(p.x / 50f) * 50, Mathf.RoundToInt(p.y / 50f) * 50); offCells[c] = offCells.TryGetValue(c, out var v) ? v + 1 : 1; }
            Debug.Log($"[MASAR open] off the asphalt: road lanes {roadOff} of {roadPts} m, junction connectors {connOff} of {connPts} m; worst spots: " +
                      string.Join(", ", offCells.OrderByDescending(kv => kv.Value).Take(8).Select(kv => $"({kv.Key.x}, {kv.Key.y}) {kv.Value} m")));
            Debug.Log($"[MASAR open] asphalt {asphalt * a2 / 1e4f:F1} ha; open (over {LaneReach} m from a lane) {openCells * a2 / 1e4f:F2} ha, of which {openFilled * a2 / 1e4f:F2} ha is not on any road's width (added by the corner closing)");

            // Connected open areas
            var comp = new int[n];
            var list = new StringBuilder();
            int found = 0;
            var q = new Queue<int>();
            var areas = new List<(int cells, int filled, Vector2 c, Vector2 min, Vector2 max, int road, float maxLane)>();
            for (int k0 = 0; k0 < n; k0++)
            {
                if (!open[k0] || comp[k0] != 0) continue;
                found++;
                int cnt = 0, filled = 0; Vector2 sum = Vector2.zero, mn = Vector2.one * 1e9f, mx = -Vector2.one * 1e9f; float maxLane = 0f;
                var roads = new Dictionary<int, int>();
                comp[k0] = found; q.Enqueue(k0);
                while (q.Count > 0)
                {
                    int k = q.Dequeue();
                    int i = k % nx, j = k / nx;
                    var p = surf.GridPos(i, j);
                    cnt++; sum += p; mn = Vector2.Min(mn, p); mx = Vector2.Max(mx, p);
                    if (raw[k] >= 0f) filled++;
                    maxLane = Mathf.Max(maxLane, laneDist[k] * cell);
                    int rid = surf.Rid[k];
                    if (rid >= 0) roads[rid] = roads.TryGetValue(rid, out var c) ? c + 1 : 1;
                    if (i > 0 && open[k - 1] && comp[k - 1] == 0) { comp[k - 1] = found; q.Enqueue(k - 1); }
                    if (i < nx - 1 && open[k + 1] && comp[k + 1] == 0) { comp[k + 1] = found; q.Enqueue(k + 1); }
                    if (j > 0 && open[k - nx] && comp[k - nx] == 0) { comp[k - nx] = found; q.Enqueue(k - nx); }
                    if (j < nz - 1 && open[k + nx] && comp[k + nx] == 0) { comp[k + nx] = found; q.Enqueue(k + nx); }
                }
                int top = roads.Count > 0 ? roads.OrderByDescending(kv => kv.Value).First().Key : -1;
                areas.Add((cnt, filled, sum / cnt, mn, mx, top, maxLane));
            }
            areas.Sort((x, y) => y.cells.CompareTo(x.cells));
            int shown = 0;
            foreach (var ar in areas)
            {
                if (ar.cells * a2 < 100f || shown++ >= 60) continue;
                string road = "";
                if (ar.road >= 0)
                {
                    var r = net.Roads[ar.road];
                    road = $"nearest road {ar.road} '{r.Name}' {r.Class} highway={r.Way.Tag("highway")} lanes {r.LanesFwd}+{r.LanesBack} width {r.HalfWidth * 2f:F1} m way {r.Way.Id}";
                }
                list.Append($"\n  {ar.cells * a2:F0} m2 at ({ar.c.x:F0}, {ar.c.y:F0}) box {ar.max.x - ar.min.x:F0} x {ar.max.y - ar.min.y:F0} m, {100f * ar.filled / ar.cells:F0}% off the road widths, up to {ar.maxLane:F0} m from a lane; {road}");
            }
            Debug.Log($"[MASAR open] {areas.Count} open areas, {areas.Count(x => x.cells * a2 >= 100f)} over 100 m2:{list}");

            // Pedestrians: walkway points (both walking sides) inside a building's collision box
            EditorSceneManager.OpenScene(OsmCityBuilder.TahliaScene, OpenSceneMode.Single);
            Physics.SyncTransforms();
            var colRoot = GameObject.Find("BuildingColliders");
            var buf = new Collider[8];
            int wpts = 0, wIn = 0, walksAny = 0, walksMost = 0;
            var blockedPts = new List<Vector2>();
            var okPts = new List<Vector2>();
            for (int w = 0; w < graph.walks.Length; w++)
            {
                int inside = 0, total = 0;
                for (float s = 0f; s <= graph.walks[w].length; s += 2f)
                {
                    graph.SampleWalk(w, s, out var p, out var t);
                    var right = Vector3.Cross(Vector3.up, t).normalized;
                    foreach (float side in new[] { 0.55f, -0.55f })
                    {
                        var at = p + right * side;
                        total++; wpts++;
                        int hits = Physics.OverlapSphereNonAlloc(at + Vector3.up * 1f, 0.3f, buf);
                        bool inBuilding = false;
                        for (int h = 0; h < hits; h++) if (buf[h].transform.IsChildOf(colRoot.transform)) inBuilding = true;
                        if (inBuilding) { inside++; wIn++; blockedPts.Add(new Vector2(at.x, at.z)); } else okPts.Add(new Vector2(at.x, at.z));
                    }
                }
                if (inside > 0) walksAny++;
                if (inside * 2 > total) walksMost++;
            }
            Debug.Log($"[MASAR open] walkways {graph.walks.Length}: {wIn} of {wpts} walking positions ({100f * wIn / Mathf.Max(1, wpts):F1}%) are inside a building; {walksAny} walkways pass through a building, {walksMost} are mostly inside one");

            // Picture: north up
            var px = new Color32[n];
            for (int k = 0; k < n; k++)
            {
                Color32 c = new Color32(214, 200, 170, 255);                                   // plot
                if (surf.Sg[k] < 0f) c = new Color32(185, 185, 180, 255);                    // sidewalk
                if (surf.Rg[k] < 0f && surf.VoidField(k) < 0f) c = new Color32(70, 70, 74, 255);   // asphalt
                if (open[k]) c = raw[k] >= 0f ? new Color32(230, 40, 40, 255) : new Color32(255, 150, 0, 255);
                px[k] = c;
            }
            foreach (var col in colRoot.GetComponentsInChildren<BoxCollider>())
            {
                var tr = col.transform;
                var b = col.size;
                for (float u = -b.x * 0.5f; u <= b.x * 0.5f; u += 0.6f)
                    for (float v = -b.z * 0.5f; v <= b.z * 0.5f; v += 0.6f)
                    {
                        var wp = tr.TransformPoint(col.center + new Vector3(u, 0f, v));
                        int k = surf.Index(new Vector2(wp.x, wp.z));
                        if (!open[k]) px[k] = new Color32(120, 80, 50, 255);
                    }
            }
            for (int l = 0; l < graph.lanes.Length; l++)
                for (float s = 0f; s <= graph.lanes[l].length; s += 1f)
                {
                    graph.Sample(l, s, out var p, out _);
                    px[surf.Index(new Vector2(p.x, p.z))] = graph.lanes[l].junction >= 0 ? new Color32(120, 170, 255, 255) : new Color32(40, 110, 255, 255);
                }
            foreach (var p in okPts) px[surf.Index(p)] = new Color32(30, 170, 60, 255);
            foreach (var p in blockedPts) px[surf.Index(p)] = new Color32(255, 0, 255, 255);
            var tex = new Texture2D(nx, nz, TextureFormat.RGBA32, false);
            tex.SetPixels32(px);
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../build/diag"));
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, "city-map.png"), tex.EncodeToPNG());
            // zoom=x,z,half: that square at 0.1 m per pixel, sampled as the kerb builder does (bilinear), to see kerb shapes
            foreach (var arg in System.Environment.GetCommandLineArgs())
            {
                if (!arg.StartsWith("zoom=")) continue;
                var v = arg.Substring(5).Split(',');
                float F(int i) => float.Parse(v[i], System.Globalization.CultureInfo.InvariantCulture);
                Vector2 c = new Vector2(F(0), F(1)); float half = F(2);
                int size = Mathf.RoundToInt(half * 2f / 0.1f);
                var zp = new Color32[size * size];
                for (int j = 0; j < size; j++)
                    for (int i = 0; i < size; i++)
                    {
                        var p = c + new Vector2(-half + i * 0.1f, -half + j * 0.1f);
                        bool road = surf.Sample(surf.Rg, p) < 0f, walk = surf.Sample(surf.Sg, p) < 0f;
                        zp[j * size + i] = road ? new Color32(70, 70, 74, 255) : walk ? new Color32(185, 185, 180, 255) : new Color32(214, 200, 170, 255);
                    }
                var zt = new Texture2D(size, size, TextureFormat.RGBA32, false);
                zt.SetPixels32(zp);
                File.WriteAllBytes(Path.Combine(dir, $"city-zoom_{v[0]}_{v[1]}.png"), zt.EncodeToPNG());
            }
            Debug.Log($"[MASAR open] map {nx} x {nz} px (1 px = {cell} m, origin {surf.Origin}) written to {dir}/city-map.png");
        }
    }
}
