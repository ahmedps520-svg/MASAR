using System.Collections.Generic;
using System.IO;
using Masar.Cameras;
using Masar.Game;
using Masar.Vehicles;
using Masar.World;
using Masar.World.Traffic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Masar.EditorTools.City
{
    /// <summary>
    /// Builds a drivable city scene from an OpenStreetMap extract (data/osm/&lt;map&gt;.json):
    /// road surfaces, kerbs, sidewalks and markings from the real road network, surveyed + generated
    /// buildings, palms, street lights, signals, the AI traffic lane graph and pedestrian walkways.
    /// Menu: MASAR/Build/City (Tahlia). Batch: -executeMethod Masar.EditorTools.City.OsmCityBuilder.BuildTahlia
    /// </summary>
    public static class OsmCityBuilder
    {
        public const string TahliaScene = "Assets/Masar/Scenes/Tahlia.unity";
        const string GenDir = "Assets/Masar/Generated/City";
        const string GraphPath = "Assets/Masar/Data/Cities/TahliaGraph.asset";

        // Tahlia / Al Rawdah download box (tools/osm/fetch-osm.ps1 defaults)
        const double South = 21.5463, West = 39.1483, North = 21.5607, East = 39.1637;

        [MenuItem("MASAR/Build/City (Tahlia)")]
        public static void BuildTahlia()
        {
            var t0 = System.DateTime.Now;
            string json = Path.GetFullPath(Path.Combine(Application.dataPath, "../../data/osm/tahlia-rawdah.json"));
            if (!File.Exists(json)) { Debug.LogError($"[MASAR] OSM data missing: {json}. Run tools/osm/fetch-osm.ps1."); return; }
            MaterialLibrary.EnsureFolders();
            ResetFolder(GenDir);
            Directory.CreateDirectory(Path.GetDirectoryName(GraphPath));

            var map = OsmMap.Load(json, South, West, North, East);
            var area = map.Bounds;
            var net = RoadNetwork.Build(map);
            var surf = new RoadSurface(net, area);
            Debug.Log($"[MASAR] City: {map.Ways.Count} ways, {net.Roads.Count} roads, {net.Junctions.Count} junctions, grid {surf.Nx}x{surf.Nz} ({(System.DateTime.Now - t0).TotalSeconds:F0}s)");

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var m = CityMaterials.Create();
            var world = new GameObject("World").transform;

            // ---------------- ground, roads, kerbs, markings (per chunk)
            AssetDatabase.StartAssetEditing();
            var chunks = new Dictionary<Vector2Int, MeshBatch>();
            MeshBatch Chunk(Vector2 p)
            {
                var k = RoadSurface.ChunkOf(p);
                if (!chunks.TryGetValue(k, out var b)) { b = new MeshBatch(); chunks[k] = b; }
                return b;
            }
            surf.Region(k => surf.R[k], 0f, 7f, p => Chunk(p)[m.Asphalt]);
            surf.Region(k => Mathf.Max(-surf.R[k], surf.S[k]), RoadSurface.KerbHeight, 1.5f, p => Chunk(p)[m.Pavers]);
            surf.Region(k => Mathf.Max(-surf.R[k], -surf.S[k]), RoadSurface.KerbHeight, 6f, p => Chunk(p)[m.Ground]);
            int kerbSegs = BuildKerbs(surf, m, Chunk);
            int marks = BuildMarkings(net, surf, m, p => area.Contains(p) ? Chunk(p) : null);
            var groundRoot = new GameObject("Ground").transform; groundRoot.SetParent(world, false);
            int surfTris = 0;
            foreach (var kv in chunks)
            {
                foreach (var e in kv.Value.Entries) surfTris += e.Value.T.Count / 3;
                bool paint = false;
                foreach (var g in kv.Value.Flush(groundRoot, $"Chunk_{kv.Key.x}_{kv.Key.y}", GenDir, collider: true))
                {
                    var r = g.GetComponent<Renderer>();
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    paint = r.sharedMaterial == m.PaintWhite || r.sharedMaterial == m.PaintYellow;
                    if (paint) Object.DestroyImmediate(g.GetComponent<MeshCollider>());
                    TestBlockBuilder.MarkStatic(g, occluder: false);
                }
            }
            // Ground beyond the map
            var outer = new MeshBatch();
            outer[m.Ground].FloorRect(-3000f, -3000f, 3000f, 3000f, -0.04f, 6f);   // below the road (y = 0): the map covers it
            foreach (var g in outer.Flush(world, "OuterGround", GenDir, collider: true)) GameObjectUtility.SetStaticEditorFlags(g, StaticEditorFlags.ReflectionProbeStatic);
            Debug.Log($"[MASAR] Surfaces: {chunks.Count} chunks, {surfTris / 1000}k triangles, {kerbSegs} kerb segments, {marks} marking pieces");

            // ---------------- buildings and street furniture
            var dress = new CityDressing(map, net, surf, m, area);
            dress.Build();
            var cityRoot = new GameObject("Buildings").transform; cityRoot.SetParent(world, false);
            cityRoot.gameObject.AddComponent<CityLodManager>();
            int nearTris = 0, midTris = 0, farTris = 0;
            // Two levels: 128 m cells (near = full detail, mid = walls/glass/roofs) inside 512 m blocks
            // (far = one box per building, a few draw calls per block)
            const float BlockSize = 512f;
            Vector2Int BlockOf(Vector2 p) => new Vector2Int(Mathf.FloorToInt(p.x / BlockSize), Mathf.FloorToInt(p.y / BlockSize));
            var blocks = new Dictionary<Vector2Int, (Transform cells, MeshBatch far)>();
            (Transform cells, MeshBatch far) Block(Vector2Int k)
            {
                if (blocks.TryGetValue(k, out var b)) return b;
                var root = new GameObject($"Block_{k.x}_{k.y}");
                root.transform.SetParent(cityRoot, false);
                var cells = new GameObject("Cells").transform; cells.SetParent(root.transform, false);
                b = (cells, new MeshBatch());
                blocks[k] = b;
                return b;
            }
            foreach (var cell in dress.Cells.Values)
            {
                if (cell.Near.IsEmpty) continue;
                var cc = new Vector2((cell.Key.x + 0.5f) * CityDressing.CellSize, (cell.Key.y + 0.5f) * CityDressing.CellSize);
                var root = new GameObject($"Cell_{cell.Key.x}_{cell.Key.y}");
                root.transform.SetParent(Block(BlockOf(cc)).cells, false);
                var near = new GameObject("Near"); near.transform.SetParent(root.transform, false);
                var mid = new GameObject("Mid"); mid.transform.SetParent(root.transform, false);
                foreach (var e in cell.Near.Entries) nearTris += e.Value.T.Count / 3;
                foreach (var e in cell.Far.Entries) midTris += e.Value.T.Count / 3;
                foreach (var g in cell.Near.Flush(near.transform, $"Cell_{cell.Key.x}_{cell.Key.y}_N", GenDir, collider: false))
                {
                    bool overlay = g.GetComponent<Renderer>().sharedMaterial.name.StartsWith("City_FacadeGrime");
                    if (overlay) g.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    TestBlockBuilder.MarkStatic(g, occluder: !overlay);
                }
                foreach (var g in cell.Far.Flush(mid.transform, $"Cell_{cell.Key.x}_{cell.Key.y}_M", GenDir, collider: false))
                    TestBlockBuilder.MarkStatic(g, occluder: false);
                mid.SetActive(false);
                var lod = root.AddComponent<CellLod>();
                lod.near = near; lod.mid = mid;
                lod.center = new Vector3(cc.x, 0f, cc.y);
                lod.radius = CityDressing.CellSize * 0.71f;
            }
            // Far blocks: plaster boxes with a flat roof, from each building's volume
            foreach (var (c, size, yaw, boxMat) in dress.Boxes)
            {
                var b = Block(BlockOf(new Vector2(c.x, c.z)));
                var wall = b.far[boxMat != null ? boxMat : m.Plaster[0]];
                wall.Transform = Matrix4x4.TRS(c, Quaternion.Euler(0f, yaw, 0f), Vector3.one);
                wall.Box(-size * 0.5f, size * 0.5f, 3f, bottom: false, top: false);
                var roof = b.far[m.Concrete];
                roof.Transform = wall.Transform;
                roof.FloorRect(-size.x * 0.5f, -size.z * 0.5f, size.x * 0.5f, size.z * 0.5f, size.y * 0.5f, 4f);
                wall.Transform = roof.Transform = Matrix4x4.identity;
            }
            foreach (var kv in blocks)
            {
                var root = kv.Value.cells.parent;
                var far = new GameObject("Far"); far.transform.SetParent(root, false);
                foreach (var e in kv.Value.far.Entries) farTris += e.Value.T.Count / 3;
                foreach (var g in kv.Value.far.Flush(far.transform, $"Block_{kv.Key.x}_{kv.Key.y}_F", GenDir, collider: false))
                    TestBlockBuilder.MarkStatic(g, occluder: false);
                far.SetActive(false);
                var bl = root.gameObject.AddComponent<BlockLod>();
                bl.cells = kv.Value.cells.gameObject; bl.far = far;
                bl.center = new Vector3((kv.Key.x + 0.5f) * BlockSize, 0f, (kv.Key.y + 0.5f) * BlockSize);
                bl.radius = BlockSize * 0.71f;
            }
            var colRoot = new GameObject("BuildingColliders").transform; colRoot.SetParent(world, false);
            foreach (var (c, size, yaw) in dress.Colliders)
            {
                var go = new GameObject("Col");
                go.transform.SetParent(colRoot, false);
                go.transform.SetPositionAndRotation(c, Quaternion.Euler(0f, yaw, 0f));
                go.AddComponent<BoxCollider>().size = size;
                GameObjectUtility.SetStaticEditorFlags(go, (StaticEditorFlags)0);
            }
            AssetDatabase.StopAssetEditing();   // mesh assets written in one batch (much faster)
            var palmRoot = new GameObject("Palms").transform; palmRoot.SetParent(world, false);
            int seed = 1;
            foreach (var p in dress.Palms) PalmKit.Place(palmRoot, m, p, seed++);
            Debug.Log($"[MASAR] Buildings: {dress.Surveyed} surveyed (OSM; {dress.SurveyedKit} with the detailed kit), {dress.Generated} generated ({dress.GeneratedVillas} villas), {dress.Modern} modern; " +
                      $"{dress.Lights} street lights, {dress.Palms.Count} palms; cells {dress.Cells.Count}, blocks {blocks.Count}, near {nearTris / 1000}k tris, mid {midTris / 1000}k, far {farTris / 1000}k");

            // ---------------- spawn: right lane of Tahlia Street
            FindSpawn(net, out var spawnPos, out float spawnYaw);

            // ---------------- lane graph, signals
            var lg = new LaneGraphBuilder(net, surf, area);
            var graph = lg.Build(spawnPos, spawnYaw);
            if (AssetDatabase.LoadAssetAtPath<CityGraph>(GraphPath) != null) AssetDatabase.DeleteAsset(GraphPath);
            AssetDatabase.CreateAsset(graph, GraphPath);
            int heads = BuildSignalHeads(world, m, net, graph);
            Debug.Log($"[MASAR] Lane graph: {lg.RoadLanes} lanes, {lg.Connectors} connectors, {lg.Signals} signalized junctions ({heads} signal heads), {graph.walks.Length} walkways");

            // ---------------- traffic + pedestrians
            var trafficPrefabs = TrafficKit.BuildTrafficCars();
            var pedPrefabs = MixamoPedestrians.Build() ?? TrafficKit.BuildPedestrians();   // owner-provided characters first
            var tgo = new GameObject("Traffic");
            var ts = tgo.AddComponent<TrafficSystem>();
            ts.graph = graph; ts.carPrefabs = trafficPrefabs;
            var pgo = new GameObject("Pedestrians");
            var ps = pgo.AddComponent<PedestrianSystem>();
            ps.graph = graph; ps.prefabs = pedPrefabs;

            // ---------------- player, camera, session (same setup as the test block)
            var carPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(TestCarBuilder.PrefabPath) ?? TestCarBuilder.BuildPrefab();
            var spawn = new GameObject("Spawn_Tahlia").transform;
            spawn.SetPositionAndRotation(spawnPos + Vector3.up * 0.15f, Quaternion.Euler(0f, spawnYaw, 0f));
            var car = (GameObject)PrefabUtility.InstantiatePrefab(carPrefab);
            car.transform.SetPositionAndRotation(spawn.position, spawn.rotation);
            var camGo = new GameObject("Main Camera", typeof(Camera), typeof(CameraRig));
            camGo.tag = "MainCamera";
            var cam = camGo.GetComponent<Camera>();
            cam.farClipPlane = 1800f; cam.nearClipPlane = 0.25f; cam.fieldOfView = 60f;
            camGo.transform.position = spawn.position - spawn.forward * 6f + Vector3.up * 2f;
            var camData = camGo.AddComponent<UniversalAdditionalCameraData>();
            camData.renderPostProcessing = true;
            camData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            camData.antialiasingQuality = AntialiasingQuality.High;
            var rig = camGo.GetComponent<CameraRig>();
            rig.target = car.GetComponent<VehicleController>();
            var session = new GameObject("DriveSession").AddComponent<DriveSession>();
            session.vehicle = car.GetComponent<VehicleController>();
            session.cameraRig = rig;
            session.spawnPoint = spawn;
            session.mapAttribution = graph.attribution;

            // ---------------- lighting, probes, occlusion
            var probes = new List<(Vector3, Vector3)>();
            for (float x = area.xMin + 100f; x < area.xMax; x += 200f)
                for (float z = area.yMin + 100f; z < area.yMax; z += 200f)
                    probes.Add((new Vector3(x, 6f, z), new Vector3(210f, 40f, 210f)));
            TestBlockBuilder.BuildLighting(probes, "Assets/Masar/Scenes/TahliaPost.asset");
            RenderSettings.fogDensity = 0.0011f;   // a bigger map: see further down the avenues
            var ls = new LightingSettings { bakedGI = false, realtimeGI = false, name = "TahliaLighting" };
            const string lsPath = "Assets/Masar/Scenes/TahliaLighting.lighting";
            if (AssetDatabase.LoadAssetAtPath<LightingSettings>(lsPath) != null) AssetDatabase.DeleteAsset(lsPath);
            AssetDatabase.CreateAsset(ls, lsPath);
            Lightmapping.lightingSettings = ls;
            EditorSceneManager.SaveScene(scene, TahliaScene);
            bool baked = Lightmapping.Bake();
            StaticOcclusionCulling.smallestOccluder = 6f;
            StaticOcclusionCulling.smallestHole = 0.5f;
            StaticOcclusionCulling.backfaceThreshold = 100f;
            var to = System.DateTime.Now;
            bool occ = StaticOcclusionCulling.Compute();
            EditorSceneManager.SaveScene(scene, TahliaScene);
            AddToBuildSettings(TahliaScene);
            AssetDatabase.SaveAssets();
            Debug.Log($"[MASAR] Tahlia scene built in {(System.DateTime.Now - t0).TotalSeconds:F0}s: probes {(baked ? "OK" : "FAILED")}, occlusion {(occ ? "OK" : "FAILED")} ({(System.DateTime.Now - to).TotalSeconds:F0}s)");
        }

        static void ResetFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) AssetDatabase.DeleteAsset(path);
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        static void AddToBuildSettings(string path)
        {
            var list = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (!list.Exists(s => s.path == path)) list.Add(new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = list.ToArray();
        }

        // ------------------------------------------------------------------ kerbs

        static int BuildKerbs(RoadSurface surf, CityMaterials m, System.Func<Vector2, MeshBatch> chunk)
        {
            int count = 0;
            float h = RoadSurface.KerbHeight;
            foreach (var line in surf.Contours(surf.R, 0.06f))
            {
                float arc = 0f;
                for (int i = 1; i < line.Count; i++)
                {
                    Vector2 a = line[i - 1], b = line[i];
                    float len = (b - a).magnitude;
                    if (len < 0.01f) continue;
                    Vector2 dir = (b - a) / len, n = new Vector2(dir.y, -dir.x);
                    Vector2 mid = (a + b) * 0.5f;
                    // Normal toward the road (where R decreases)
                    if (surf.Sample(surf.R, mid + n * 0.4f) > surf.Sample(surf.R, mid - n * 0.4f)) n = -n;
                    bool painted = surf.NearestClass(mid) >= RoadClass.Secondary;
                    // Split into 1 m pieces so painted kerbs alternate black / yellow along the arc
                    int pieces = painted ? Mathf.Max(1, Mathf.CeilToInt(len)) : 1;
                    for (int k = 0; k < pieces; k++)
                    {
                        float t0 = (float)k / pieces, t1 = (float)(k + 1) / pieces;
                        Vector2 p0 = Vector2.Lerp(a, b, t0), p1 = Vector2.Lerp(a, b, t1);
                        Material mat = m.Concrete;
                        if (painted) mat = Mathf.FloorToInt(arc + (t0 + t1) * 0.5f * len) % 2 == 0 ? m.KerbBlack : m.KerbYellow;
                        var mb = chunk((p0 + p1) * 0.5f)[mat];
                        Vector3 n3 = new Vector3(n.x, 0f, n.y);
                        // Vertical face
                        Vector3 A = new Vector3(p0.x, 0f, p0.y), B = new Vector3(p1.x, 0f, p1.y);
                        int v = mb.Count;
                        mb.AddVertex(A, n3, new Vector2(0, 0)); mb.AddVertex(B, n3, new Vector2(1, 0));
                        mb.AddVertex(B + Vector3.up * h, n3, new Vector2(1, 0.15f)); mb.AddVertex(A + Vector3.up * h, n3, new Vector2(0, 0.15f));
                        mb.T.AddRange(new[] { v, v + 1, v + 2, v, v + 2, v + 3 });
                        // Top stone strip (0.25 m) on the raised side
                        Vector3 inward = -n3 * 0.25f, up = Vector3.up * (h + 0.003f);
                        int u = mb.Count;
                        mb.AddVertex(A + up, Vector3.up, new Vector2(0, 0)); mb.AddVertex(B + up, Vector3.up, new Vector2(1, 0));
                        mb.AddVertex(B + up + inward, Vector3.up, new Vector2(1, 0.25f)); mb.AddVertex(A + up + inward, Vector3.up, new Vector2(0, 0.25f));
                        mb.T.AddRange(new[] { u, u + 1, u + 2, u, u + 2, u + 3 });
                        count++;
                    }
                    arc += len;
                }
            }
            return count;
        }

        // ------------------------------------------------------------------ markings

        static int BuildMarkings(RoadNetwork net, RoadSurface surf, CityMaterials m, System.Func<Vector2, MeshBatch> chunk)
        {
            int pieces = 0;
            const float y = 0.006f;
            foreach (var road in net.Roads)
            {
                if (!road.Marked) continue;
                // Arc intervals to keep clear: junction boxes (+ room for the zebra and stop line at signals)
                var skip = new List<(float, float)>();
                for (int i = 0; i < road.NodeIds.Count; i++)
                    if (net.Junctions.TryGetValue(road.NodeIds[i], out var j))
                    {
                        float s = RoadNetwork.ArcAt(road.P, i), r = j.Radius + (j.Signal ? 5.5f : 1f);
                        skip.Add((s - r, s + r));
                    }
                bool Clear(float s) { foreach (var (a, b) in skip) if (s > a && s < b) return false; return true; }
                int n = road.LanesFwd + road.LanesBack;
                float sh = road.HalfWidth - n * road.LaneWidth * 0.5f;
                float edge = road.HalfWidth - Mathf.Max(0.3f, sh * 0.6f);
                // Edge lines: right edge white; on one-way carriageways the left (median) edge is yellow
                pieces += Strip(chunk, m.PaintWhite, road.P, edge, 0.15f, y, 0f, 0f, Clear);
                pieces += Strip(chunk, road.Oneway ? m.PaintYellow : m.PaintWhite, road.P, -edge, 0.15f, y, 0f, 0f, Clear);
                for (int k = 1; k < n; k++)
                {
                    float u = -road.HalfWidth + sh + road.LaneWidth * k;
                    bool divider = !road.Oneway && k == road.LanesBack;
                    if (divider)
                    {
                        // Two-way center: double solid yellow on main roads, dashed white on smaller ones
                        if (road.Class >= RoadClass.Secondary)
                        {
                            pieces += Strip(chunk, m.PaintYellow, road.P, u - 0.12f, 0.12f, y, 0f, 0f, Clear);
                            pieces += Strip(chunk, m.PaintYellow, road.P, u + 0.12f, 0.12f, y, 0f, 0f, Clear);
                        }
                        else pieces += Strip(chunk, m.PaintWhite, road.P, u, 0.12f, y, 3f, 6f, Clear);
                    }
                    else pieces += Strip(chunk, m.PaintWhite, road.P, u, 0.12f, y, 3f, 6f, Clear);
                }
            }
            // Stop lines and zebra crossings at signalized junctions
            foreach (var j in net.Junctions.Values)
            {
                if (!j.Signal) continue;
                foreach (var (road, idx) in j.Arms)
                {
                    float sNode = RoadNetwork.ArcAt(road.P, idx), len = RoadNetwork.Length(road.P);
                    foreach (int dirSign in new[] { -1, 1 })
                    {
                        // dirSign -1: the arm that leaves the node backwards along the road (points before idx)
                        float sZebra = sNode + dirSign * (j.Radius + 2.5f), sStop = sNode + dirSign * (j.Radius + 5.2f);
                        if (sZebra < 0f || sZebra > len || sStop < 0f || sStop > len) continue;
                        PointAt(road.P, sZebra, out var pz, out var dz);
                        Vector2 nz = new Vector2(dz.y, -dz.x);
                        for (float u = -road.HalfWidth + 0.5f; u < road.HalfWidth - 0.4f; u += 1.1f)
                            pieces += Quad(chunk, m.PaintWhite, pz + nz * u, dz, 0.55f, 3.0f, y);
                        // Stop line across the lanes that approach the junction on this arm
                        PointAt(road.P, sStop, out var ps, out var ds);
                        Vector2 ns = new Vector2(ds.y, -ds.x);
                        int n = road.LanesFwd + road.LanesBack;
                        float sh = road.HalfWidth - n * road.LaneWidth * 0.5f;
                        float divider = -road.HalfWidth + sh + road.LaneWidth * road.LanesBack;
                        // Approaching along +dir (dirSign -1 side) uses the forward lanes (right of center); the other side the backward lanes
                        float u0 = dirSign < 0 ? divider : -road.HalfWidth + sh, u1 = dirSign < 0 ? road.HalfWidth - sh : divider;
                        if (road.Oneway && dirSign > 0) continue;
                        if (u1 - u0 > 0.5f) pieces += Quad(chunk, m.PaintWhite, ps + ns * ((u0 + u1) * 0.5f), new Vector2(-ns.y, ns.x), u1 - u0, 0.45f, y + 0.001f);
                    }
                }
            }
            return pieces;
        }

        /// <summary>A painted line along a polyline offset u to the right; dash 0 = solid.</summary>
        static int Strip(System.Func<Vector2, MeshBatch> chunk, Material mat, List<Vector2> pts, float u, float width, float y, float dash, float gap, System.Func<float, bool> clear)
        {
            var line = RoadNetwork.Offset(pts, u);
            int count = 0;
            float s = 0f;
            for (int i = 1; i < line.Count; i++)
            {
                Vector2 a = line[i - 1], b = line[i];
                float len = (b - a).magnitude;
                if (len < 0.01f) { continue; }
                Vector2 dir = (b - a) / len;
                const float step = 2f;
                for (float t = 0f; t < len; t += step)
                {
                    float t1 = Mathf.Min(len, t + step);
                    float sMid = s + (t + t1) * 0.5f;
                    if (!clear(sMid)) continue;
                    if (dash > 0f)
                    {
                        // Clip the piece to the "on" part of the dash pattern
                        float period = dash + gap;
                        float on0 = Mathf.Floor((s + t) / period) * period, on1 = on0 + dash;
                        float c0 = Mathf.Max(s + t, on0), c1 = Mathf.Min(s + t1, on1);
                        if (c1 - c0 < 0.05f)
                        {
                            on0 += period; on1 += period;
                            c0 = Mathf.Max(s + t, on0); c1 = Mathf.Min(s + t1, on1);
                            if (c1 - c0 < 0.05f) continue;
                        }
                        count += Segment(chunk, mat, a + dir * (c0 - s), a + dir * (c1 - s), width, y);
                    }
                    else count += Segment(chunk, mat, a + dir * t, a + dir * t1, width, y);
                }
                s += len;
            }
            return count;
        }

        static int Segment(System.Func<Vector2, MeshBatch> chunk, Material mat, Vector2 a, Vector2 b, float width, float y)
        {
            Vector2 d = (b - a).normalized, n = new Vector2(d.y, -d.x) * (width * 0.5f);
            var batch = chunk((a + b) * 0.5f);
            if (batch == null) return 0;   // outside the map
            var mb = batch[mat];
            int v = mb.Count;
            Vector2[] q = { a - n, b - n, b + n, a + n };
            foreach (var p in q) mb.AddVertex(new Vector3(p.x, y, p.y), Vector3.up, p * 0.25f);
            mb.T.AddRange(new[] { v, v + 1, v + 2, v, v + 2, v + 3 });
            return 1;
        }

        static int Quad(System.Func<Vector2, MeshBatch> chunk, Material mat, Vector2 center, Vector2 along, float alongLen, float acrossLen, float y)
        {
            Vector2 d = along.normalized, n = new Vector2(d.y, -d.x);
            return Segment(chunk, mat, center - n * (acrossLen * 0.5f), center + n * (acrossLen * 0.5f), alongLen, y);
        }

        static void PointAt(List<Vector2> pts, float s, out Vector2 p, out Vector2 dir)
        {
            float acc = 0f;
            for (int i = 1; i < pts.Count; i++)
            {
                float l = (pts[i] - pts[i - 1]).magnitude;
                if (acc + l >= s || i == pts.Count - 1)
                {
                    float t = l > 1e-4f ? Mathf.Clamp01((s - acc) / l) : 0f;
                    p = Vector2.Lerp(pts[i - 1], pts[i], t); dir = l > 1e-4f ? (pts[i] - pts[i - 1]) / l : Vector2.right;
                    return;
                }
                acc += l;
            }
            p = pts[pts.Count - 1]; dir = Vector2.right;
        }

        // ------------------------------------------------------------------ signals

        static int BuildSignalHeads(Transform world, CityMaterials m, RoadNetwork net, CityGraph graph)
        {
            var parent = new GameObject("Signals").transform; parent.SetParent(world, false);
            int idx = 1000, count = 0;
            // One head per approach: at the right of the arriving road lanes' end, facing the traffic
            var done = new HashSet<string>();
            for (int li = 0; li < graph.lanes.Length; li++)
            {
                var conn = graph.lanes[li];
                if (conn.junction < 0 || !graph.junctions[conn.junction].signal) continue;
                // The road lane feeding this connector
                for (int fi = 0; fi < graph.lanes.Length; fi++)
                {
                    var feed = graph.lanes[fi];
                    if (feed.junction >= 0) continue;
                    bool feeds = false;
                    for (int k = 0; k < feed.nextCount; k++) if (graph.next[feed.nextStart + k] == li) { feeds = true; break; }
                    if (!feeds) continue;
                    Vector3 end = graph.points[feed.pointStart + feed.pointCount - 1];
                    Vector3 prev = graph.points[feed.pointStart + feed.pointCount - 2];
                    Vector3 dir = (end - prev).normalized;
                    string key = $"{conn.junction}_{Mathf.RoundToInt(Mathf.Atan2(dir.x, dir.z) * 4f)}";
                    if (!done.Add(key)) break;
                    // Find the rightmost lane end of this approach: step right until off the road
                    Vector3 right = Vector3.Cross(Vector3.up, dir).normalized;
                    Vector3 basePos = end;
                    for (int s = 0; s < 12; s++)
                    {
                        Vector3 cand = end + right * (1.8f + s * 1.0f);
                        basePos = cand;
                        if (!IsOnRoad(graph, cand)) break;
                    }
                    basePos += right * 0.8f;
                    basePos.y = RoadSurface.KerbHeight;
                    var j = graph.junctions[conn.junction];
                    var cyc = TestBlockBuilder.Signal(parent, m, basePos, Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg + 180f, j.cycleOffset, idx++);
                    cyc.useClock = true; cyc.group = conn.signalGroup; cyc.greenTime = j.greenTime;
                    count++;
                    break;
                }
            }
            return count;
        }

        static bool IsOnRoad(CityGraph g, Vector3 p)
        {
            // Close to any road lane center line of the graph (cheap test, editor only)
            for (int i = 0; i < g.lanes.Length; i++)
            {
                var l = g.lanes[i];
                if (l.junction >= 0) continue;
                for (int k = l.pointStart; k < l.pointStart + l.pointCount - 1; k++)
                {
                    Vector3 a = g.points[k], b = g.points[k + 1];
                    if ((p - a).sqrMagnitude > 900f && (p - b).sqrMagnitude > 900f) continue;
                    float d = RoadNetwork.SegDist(new Vector2(p.x, p.z), new Vector2(a.x, a.z), new Vector2(b.x, b.z), out _);
                    if (d < 1.9f) return true;
                }
            }
            return false;
        }

        // ------------------------------------------------------------------ spawn

        static void FindSpawn(RoadNetwork net, out Vector3 pos, out float yaw)
        {
            // Longest primary road named Tahlia (or the longest primary), right-most lane, near the middle of the map
            RoadNetwork.Road best = null; float bestScore = -1f;
            foreach (var r in net.Roads)
            {
                if (r.Class < RoadClass.Primary) continue;
                float len = RoadNetwork.Length(r.P);
                float score = len + (r.Name.Contains("Tahlia") || r.Name.Contains("Mohammed") || r.Name.Contains("محمد") ? 5000f : 0f);
                if (score > bestScore) { bestScore = score; best = r; }
            }
            if (best == null) best = net.Roads[0];
            float total = RoadNetwork.Length(best.P);
            // Start a third of the way along so the drive heads through the map
            PointAt(best.P, total * 0.33f, out var p, out var d);
            int n = best.LanesFwd + best.LanesBack;
            float sh = best.HalfWidth - n * best.LaneWidth * 0.5f;
            float u = best.HalfWidth - sh - best.LaneWidth * 0.5f;
            var q = p + new Vector2(d.y, -d.x) * u;
            pos = new Vector3(q.x, 0f, q.y);
            yaw = Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg;
            Debug.Log($"[MASAR] Spawn on '{best.Name}' ({best.Class}, {best.LanesFwd}+{best.LanesBack} lanes) at {pos}, heading {yaw:F0} deg");
        }
    }
}
