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
        /// <summary>One additive scene per 512 m block with that block's buildings, streamed by CityLodManager.</summary>
        const string SectionDir = "Assets/Masar/Scenes/Tahlia";
        const string GraphPath = "Assets/Masar/Data/Cities/TahliaGraph.asset";

        // Tahlia from Al Rawdah east to Madinah Road and King Fahd Road (tools/osm/fetch-osm.ps1 -Name tahlia-madinah):
        // the Prince Saud Al Faisal and King Fahd Road flyovers, the sunken Madinah Road, Serafi Megamall
        const string MapFile = "tahlia-madinah";
        const double South = 21.5463, West = 39.1483, North = 21.5700, East = 39.1900;

        /// <summary>Solves the road grades only and logs every structure (fast check, no scene).</summary>
        public static void ReportGrades()
        {
            string json = Path.GetFullPath(Path.Combine(Application.dataPath, $"../../data/osm/{MapFile}.json"));
            var net = RoadNetwork.Build(OsmMap.Load(json, South, West, North, East));
            RoadGrades.Report(net);
        }

        /// <summary>Re-bakes the reflection probes of the saved Tahlia scene (batchmode -executeMethod).</summary>
        public static void BakeTahliaProbes()
        {
            var scene = EditorSceneManager.OpenScene(TahliaScene, OpenSceneMode.Single);
            BakeProbes();
            EditorSceneManager.SaveScene(scene, TahliaScene);
        }

        /// <summary>
        /// Opens every streamed block scene of the open city scene additively, with its buildings shown
        /// (editor renders of the whole city: probe bakes, screenshots). Close them unsaved.
        /// </summary>
        public static List<UnityEngine.SceneManagement.Scene> OpenBlockScenes()
        {
            var sections = new List<UnityEngine.SceneManagement.Scene>();
            foreach (var bl in Object.FindObjectsByType<BlockLod>(FindObjectsSortMode.None))
            {
                if (string.IsNullOrEmpty(bl.section)) continue;
                var sec = EditorSceneManager.OpenScene($"{SectionDir}/{bl.section}.unity", OpenSceneMode.Additive);
                foreach (var r in sec.GetRootGameObjects()) r.SetActive(true);
                sections.Add(sec);
            }
            return sections;
        }

        /// <summary>
        /// Bakes the scene's reflection probes one at a time into EXR cubemaps and assigns them as custom
        /// probes. Lightmapping.Bake renders all probes in one job with no progress: on the 4.4 km map it
        /// ran for an hour without finishing (2026-11-23). One at a time, each probe's time is logged, and
        /// a far clip of 500 m keeps it from rendering the whole city.
        /// </summary>
        static void BakeProbes()
        {
            const string dir = "Assets/Masar/Generated/CityProbes";
            ResetFolder(dir);
            // The buildings are in the streamed block scenes: open them all so the reflections include them
            var sections = OpenBlockScenes();
            var probes = Object.FindObjectsByType<ReflectionProbe>(FindObjectsInactive.Exclude, FindObjectsSortMode.InstanceID);
            var t0 = System.DateTime.Now;
            int ok = 0;
            for (int i = 0; i < probes.Length; i++)
            {
                var p = probes[i];
                p.mode = UnityEngine.Rendering.ReflectionProbeMode.Custom;
                p.farClipPlane = 500f;
                string path = $"{dir}/{p.name}.exr";
                var t = System.DateTime.Now;
                if (!Lightmapping.BakeReflectionProbe(p, path)) { Debug.LogError($"[MASAR] Probe {p.name} failed"); continue; }
                AssetDatabase.ImportAsset(path);
                p.customBakedTexture = AssetDatabase.LoadAssetAtPath<Cubemap>(path);
                ok++;
                Debug.Log($"[MASAR] Probe {i + 1}/{probes.Length} {p.name} in {(System.DateTime.Now - t).TotalSeconds:F1}s");
            }
            Debug.Log($"[MASAR] Probes: {ok}/{probes.Length} baked in {(System.DateTime.Now - t0).TotalSeconds:F0}s ({sections.Count} block scenes open)");
            foreach (var sec in sections) EditorSceneManager.CloseScene(sec, true);   // unsaved: stay inactive on disk
        }

        [MenuItem("MASAR/Build/City (Tahlia)")]
        public static void BuildTahlia()
        {
            var t0 = System.DateTime.Now;
            string json = Path.GetFullPath(Path.Combine(Application.dataPath, $"../../data/osm/{MapFile}.json"));
            if (!File.Exists(json)) { Debug.LogError($"[MASAR] OSM data missing: {json}. Run tools/osm/fetch-osm.ps1."); return; }
            MaterialLibrary.EnsureFolders();
            ResetFolder(GenDir);
            ResetFolder(SectionDir);
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
            // Ground-level surfaces only (flyovers, ramps and trenches are built by RoadStructures);
            // nothing is built over an open trench except the roads that cross it
            surf.Region(k => Mathf.Max(surf.Rg[k], surf.VoidField(k)), 0f, 7f, p => Chunk(p)[m.Asphalt]);
            surf.Region(k => Mathf.Max(Mathf.Max(-surf.Rg[k], surf.Sg[k]), surf.VoidField(k)), RoadSurface.KerbHeight, 1.5f, p => Chunk(p)[m.Pavers]);
            // Plots: paved forecourts in the first 6 m behind the sidewalk of tertiary and bigger roads
            // (shop fronts and parking aprons, as on Tahlia), sand-coloured ground elsewhere
            const float Forecourt = 6f;
            bool Major(int k) => surf.Cls[k] >= (byte)RoadClass.Tertiary;
            surf.Region(k => Mathf.Max(Mathf.Max(Mathf.Max(-surf.Rg[k], -surf.Sg[k]), Mathf.Max(surf.Sg[k] - Forecourt, Major(k) ? -1f : 1f)), surf.VoidField(k)), RoadSurface.KerbHeight, 1.5f, p => Chunk(p)[m.Pavers]);
            surf.Region(k => Mathf.Max(Mathf.Max(Mathf.Max(-surf.Rg[k], -surf.Sg[k]), Major(k) ? Forecourt - surf.Sg[k] : -1f), surf.VoidField(k)), RoadSurface.KerbHeight, 6f, p => Chunk(p)[m.Ground]);
            int kerbSegs = BuildKerbs(surf, m, Chunk);
            int marks = BuildMarkings(net, surf, m, p => area.Contains(p) ? Chunk(p) : null);
            // Flyovers, ramps, trenches and tunnels (cast shadows, unlike the flat ground)
            var structChunks = new Dictionary<Vector2Int, MeshBatch>();
            var tunnelLight = MaterialLibrary.Emissive("City_TunnelLight", new Color(0.92f, 0.92f, 0.88f), new Color(3.2f, 3.0f, 2.6f), 0.5f);
            var structures = new RoadStructures(net, surf, m, tunnelLight, p =>
            {
                var k = RoadSurface.ChunkOf(p);
                if (!structChunks.TryGetValue(k, out var b)) { b = new MeshBatch(); structChunks[k] = b; }
                return b;
            });
            structures.Build();
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
            var structRoot = new GameObject("Structures").transform; structRoot.SetParent(world, false);
            foreach (var kv in structChunks)
                foreach (var g in kv.Value.Flush(structRoot, $"Struct_{kv.Key.x}_{kv.Key.y}", GenDir, collider: true))
                {
                    var r = g.GetComponent<Renderer>();
                    if (r.sharedMaterial == tunnelLight) { r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; Object.DestroyImmediate(g.GetComponent<MeshCollider>()); }
                    TestBlockBuilder.MarkStatic(g, occluder: false);
                }
            // Ground beyond the map
            var outer = new MeshBatch();
            outer[m.Ground].FloorRect(-3000f, -3000f, 3000f, 3000f, -0.04f, 6f);   // below the road (y = 0): the map covers it
            foreach (var g in outer.Flush(world, "OuterGround", GenDir, collider: true)) GameObjectUtility.SetStaticEditorFlags(g, StaticEditorFlags.ReflectionProbeStatic);
            Debug.Log($"[MASAR] Surfaces: {chunks.Count} chunks, {surfTris / 1000}k triangles, {kerbSegs} kerb segments, {marks} marking pieces");

            // ---------------- buildings and street furniture
            var dress = new CityDressing(map, net, surf, m, area);
            dress.Build();
            // Landscape ground (lawns, turf, parking asphalt, paths, gravel, pools) and painted lines
            var landChunks = new Dictionary<Vector2Int, MeshBatch>();
            dress.EmitGround(p =>
            {
                var k = RoadSurface.ChunkOf(p);
                if (!landChunks.TryGetValue(k, out var b)) { b = new MeshBatch(); landChunks[k] = b; }
                return b;
            });
            var landRoot = new GameObject("Landscape").transform; landRoot.SetParent(world, false);
            foreach (var kv in landChunks)
                foreach (var g in kv.Value.Flush(landRoot, $"Land_{kv.Key.x}_{kv.Key.y}", GenDir, collider: false))
                {
                    g.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    TestBlockBuilder.MarkStatic(g, occluder: false);
                }
            Debug.Log($"[MASAR] Landscape: {dress.Parks} parks, {dress.PocketParks} pocket gardens, {dress.Pitches} pitches, {dress.ParkingLots} car parks ({dress.ParkedCars.Count} parked cars), {dress.Sites} construction sites, {dress.Trees} trees");
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
            FindSpawn(net, area, out var spawnPos, out float spawnYaw);

            // ---------------- lane graph, signals
            var lg = new LaneGraphBuilder(net, surf, area);
            var graph = lg.Build(spawnPos, spawnYaw);
            if (AssetDatabase.LoadAssetAtPath<CityGraph>(GraphPath) != null) AssetDatabase.DeleteAsset(GraphPath);
            AssetDatabase.CreateAsset(graph, GraphPath);
            int heads = BuildSignalHeads(world, m, net, graph);
            Debug.Log($"[MASAR] Lane graph: {lg.RoadLanes} lanes, {lg.Connectors} connectors, {lg.Signals} signalized junctions ({heads} signal heads), {graph.walks.Length} walkways");

            // ---------------- traffic + pedestrians
            var trafficPrefabs = TrafficKit.BuildTrafficCars();
            // Parked cars: static copies of the traffic car bodies, hidden when small on screen
            var parkedRoot = new GameObject("ParkedCars").transform; parkedRoot.SetParent(world, false);
            var prnd = new System.Random(41);
            foreach (var (pos, yaw) in dress.ParkedCars)
            {
                var pf = trafficPrefabs[prnd.Next(trafficPrefabs.Length)];
                var parked = (GameObject)PrefabUtility.InstantiatePrefab(pf.gameObject);
                PrefabUtility.UnpackPrefabInstance(parked, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                parked.transform.SetParent(parkedRoot, false);
                parked.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
                MakeParked(parked);
                var lodg = parked.AddComponent<LODGroup>();
                lodg.fadeMode = LODFadeMode.None;
                lodg.SetLODs(new[] { new LOD(0.035f, parked.GetComponentsInChildren<Renderer>()) });
                lodg.RecalculateBounds();
                GameObjectUtility.SetStaticEditorFlags(parked, StaticEditorFlags.OccludeeStatic);
            }
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

            // ---------------- streamed blocks: each block's cells saved to a scene of its own. One scene held
            // 5.5 GB of meshes (one file over Unity's 4 GB limit, the player crashed on load, 2026-11-23);
            // streamed, only the blocks around the camera are in memory.
            var sectionPaths = new List<string>();
            EditorSceneManager.SaveScene(scene, TahliaScene);   // Unity opens no extra scene beside an untitled one
            foreach (var kv in blocks)
            {
                var cellsGo = kv.Value.cells.gameObject;
                if (cellsGo.transform.childCount == 0) continue;
                var bl = cellsGo.transform.parent.GetComponent<BlockLod>();
                string secName = $"Tahlia_Block_{kv.Key.x}_{kv.Key.y}";
                cellsGo.name = secName;
                cellsGo.transform.SetParent(null, true);
                var sec = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cellsGo, sec);
                cellsGo.SetActive(false);   // shown by BlockLod once loaded and close enough
                string secPath = $"{SectionDir}/{secName}.unity";
                EditorSceneManager.SaveScene(sec, secPath);
                EditorSceneManager.CloseScene(sec, true);
                bl.cells = null;
                bl.section = secName;
                sectionPaths.Add(secPath);
            }
            UnityEngine.SceneManagement.SceneManager.SetActiveScene(scene);
            Debug.Log($"[MASAR] Streamed blocks: {sectionPaths.Count} scenes in {SectionDir}");

            // ---------------- lighting, probes, occlusion
            var probes = new List<(Vector3, Vector3)>();
            // 400 m grid (about 70 probes on the 4.4 x 2.6 km map). A 200 m grid gave about 290 probes,
            // each rendering the whole city six times: the bake ran out of memory and hung (2026-11-23).
            for (float x = area.xMin + 200f; x < area.xMax; x += 400f)
                for (float z = area.yMin + 200f; z < area.yMax; z += 400f)
                    probes.Add((new Vector3(x, 6f, z), new Vector3(410f, 60f, 410f)));
            TestBlockBuilder.BuildLighting(probes, "Assets/Masar/Scenes/TahliaPost.asset");
            RenderSettings.fogDensity = 0.0011f;   // a bigger map: see further down the avenues
            var ls = new LightingSettings { bakedGI = false, realtimeGI = false, name = "TahliaLighting" };
            const string lsPath = "Assets/Masar/Scenes/TahliaLighting.lighting";
            if (AssetDatabase.LoadAssetAtPath<LightingSettings>(lsPath) != null) AssetDatabase.DeleteAsset(lsPath);
            AssetDatabase.CreateAsset(ls, lsPath);
            Lightmapping.lightingSettings = ls;
            // Probes as custom before the bake, so Lightmapping.Bake does only the sky (ambient) and
            // BakeProbes renders the probes one at a time (about 3 min for 77).
            foreach (var p in Object.FindObjectsByType<ReflectionProbe>(FindObjectsSortMode.None))
                p.mode = UnityEngine.Rendering.ReflectionProbeMode.Custom;
            EditorSceneManager.SaveScene(scene, TahliaScene);
            bool baked = Lightmapping.Bake();
            BakeProbes();
            // No Umbra occlusion bake for the city: with whole-cell merged meshes it culled visible buildings,
            // shopfronts and trees (found 2026-11-22 by comparing renders with and without it). The cell
            // near/mid/far switching does the distance work instead.
            var to = System.DateTime.Now;
            StaticOcclusionCulling.Clear();
            bool occ = true;
            EditorSceneManager.SaveScene(scene, TahliaScene);
            AddToBuildSettings(TahliaScene);
            SetSectionScenes(sectionPaths);
            AssetDatabase.SaveAssets();
            Debug.Log($"[MASAR] Tahlia scene built in {(System.DateTime.Now - t0).TotalSeconds:F0}s: probes {(baked ? "OK" : "FAILED")}, occlusion off (cleared: {occ})");
        }

        static void ResetFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) AssetDatabase.DeleteAsset(path);
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        /// <summary>Replaces the block scenes in the build list (after the main city scene).</summary>
        static void SetSectionScenes(List<string> paths)
        {
            var list = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            list.RemoveAll(s => s.path.StartsWith(SectionDir + "/"));
            foreach (var p in paths) list.Add(new EditorBuildSettingsScene(p, true));
            EditorBuildSettings.scenes = list.ToArray();
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
            foreach (var line in surf.Contours(surf.Rg, 0.06f))
            {
                float arc = 0f;
                for (int i = 1; i < line.Count; i++)
                {
                    Vector2 a = line[i - 1], b = line[i];
                    float len = (b - a).magnitude;
                    if (len < 0.01f) continue;
                    Vector2 dir = (b - a) / len, n = new Vector2(dir.y, -dir.x);
                    Vector2 mid = (a + b) * 0.5f;
                    // No kerb where a ramp or trench starts (the cut across the road) or over a trench
                    if (surf.Sample(surf.E, mid) < 0.6f || surf.VoidField(surf.Index(mid)) > 0f) { arc += len; continue; }
                    // Normal toward the road (where R decreases)
                    if (surf.Sample(surf.Rg, mid + n * 0.4f) > surf.Sample(surf.Rg, mid - n * 0.4f)) n = -n;
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
                pieces += Strip(chunk, m.PaintWhite, road.P, road.H, edge, 0.15f, y, 0f, 0f, Clear);
                pieces += Strip(chunk, road.Oneway ? m.PaintYellow : m.PaintWhite, road.P, road.H, -edge, 0.15f, y, 0f, 0f, Clear);
                for (int k = 1; k < n; k++)
                {
                    float u = -road.HalfWidth + sh + road.LaneWidth * k;
                    bool divider = !road.Oneway && k == road.LanesBack;
                    if (divider)
                    {
                        // Two-way center: double solid yellow on main roads, dashed white on smaller ones
                        if (road.Class >= RoadClass.Secondary)
                        {
                            pieces += Strip(chunk, m.PaintYellow, road.P, road.H, u - 0.12f, 0.12f, y, 0f, 0f, Clear);
                            pieces += Strip(chunk, m.PaintYellow, road.P, road.H, u + 0.12f, 0.12f, y, 0f, 0f, Clear);
                        }
                        else pieces += Strip(chunk, m.PaintWhite, road.P, road.H, u, 0.12f, y, 3f, 6f, Clear);
                    }
                    else pieces += Strip(chunk, m.PaintWhite, road.P, road.H, u, 0.12f, y, 3f, 6f, Clear);
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
                            pieces += Quad(chunk, m.PaintWhite, pz + nz * u, dz, 0.55f, 3.0f, y + Lifted(road.HeightAt(sZebra)));
                        // Stop line across the lanes that approach the junction on this arm
                        PointAt(road.P, sStop, out var ps, out var ds);
                        Vector2 ns = new Vector2(ds.y, -ds.x);
                        int n = road.LanesFwd + road.LanesBack;
                        float sh = road.HalfWidth - n * road.LaneWidth * 0.5f;
                        float divider = -road.HalfWidth + sh + road.LaneWidth * road.LanesBack;
                        // Approaching along +dir (dirSign -1 side) uses the forward lanes (right of center); the other side the backward lanes
                        float u0 = dirSign < 0 ? divider : -road.HalfWidth + sh, u1 = dirSign < 0 ? road.HalfWidth - sh : divider;
                        if (road.Oneway && dirSign > 0) continue;
                        if (u1 - u0 > 0.5f) pieces += Quad(chunk, m.PaintWhite, ps + ns * ((u0 + u1) * 0.5f), new Vector2(-ns.y, ns.x), u1 - u0, 0.45f, y + 0.001f + Lifted(road.HeightAt(sStop)));
                    }
                }
            }
            return pieces;
        }

        /// <summary>A painted line along a polyline offset u to the right; dash 0 = solid.</summary>
        static int Strip(System.Func<Vector2, MeshBatch> chunk, Material mat, List<Vector2> pts, List<float> hs, float u, float width, float y, float dash, float gap, System.Func<float, bool> clear)
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
                float ha = hs[i - 1], hb = hs[i];
                float HAt(float along) => Lifted(Mathf.Lerp(ha, hb, along / len));
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
                        count += Segment(chunk, mat, a + dir * (c0 - s), a + dir * (c1 - s), width, y + HAt(c0 - s), y + HAt(c1 - s));
                    }
                    else count += Segment(chunk, mat, a + dir * t, a + dir * t1, width, y + HAt(t), y + HAt(t1));
                }
                s += len;
            }
            return count;
        }

        static float Lifted(float h) => h > 0f ? h + RoadStructures.Lift : h;

        static int Segment(System.Func<Vector2, MeshBatch> chunk, Material mat, Vector2 a, Vector2 b, float width, float ya, float yb)
        {
            Vector2 d = (b - a).normalized, n = new Vector2(d.y, -d.x) * (width * 0.5f);
            var batch = chunk((a + b) * 0.5f);
            if (batch == null) return 0;   // outside the map
            var mb = batch[mat];
            int v = mb.Count;
            Vector2[] q = { a - n, b - n, b + n, a + n };
            for (int i = 0; i < 4; i++) mb.AddVertex(new Vector3(q[i].x, i == 1 || i == 2 ? yb : ya, q[i].y), Vector3.up, q[i] * 0.25f);
            mb.T.AddRange(new[] { v, v + 1, v + 2, v, v + 2, v + 3 });
            return 1;
        }

        static int Quad(System.Func<Vector2, MeshBatch> chunk, Material mat, Vector2 center, Vector2 along, float alongLen, float acrossLen, float y)
        {
            Vector2 d = along.normalized, n = new Vector2(d.y, -d.x);
            return Segment(chunk, mat, center - n * (acrossLen * 0.5f), center + n * (acrossLen * 0.5f), alongLen, y, y);
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
                    var j = graph.junctions[conn.junction];
                    basePos.y = RoadSurface.KerbHeight + j.position.y;
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

        /// <summary>
        /// Recomputes the spawn on the built city scene (car, camera, spawn point, lane graph) without
        /// rebuilding the city. -executeMethod Masar.EditorTools.City.OsmCityBuilder.ResetSpawn
        /// </summary>
        public static void ResetSpawn()
        {
            string json = Path.GetFullPath(Path.Combine(Application.dataPath, $"../../data/osm/{MapFile}.json"));
            var map = OsmMap.Load(json, South, West, North, East);
            var net = RoadNetwork.Build(map);
            FindSpawn(net, map.Bounds, out var pos, out float yaw);
            var scene = EditorSceneManager.OpenScene(TahliaScene, OpenSceneMode.Single);
            var rot = Quaternion.Euler(0f, yaw, 0f);
            var spawn = GameObject.Find("Spawn_Tahlia").transform;
            spawn.SetPositionAndRotation(pos + Vector3.up * 0.15f, rot);
            var session = Object.FindFirstObjectByType<DriveSession>();
            session.vehicle.transform.SetPositionAndRotation(spawn.position, spawn.rotation);
            session.cameraRig.transform.position = spawn.position - spawn.forward * 6f + Vector3.up * 2f;
            var graph = AssetDatabase.LoadAssetAtPath<CityGraph>(GraphPath);
            graph.spawnPosition = pos; graph.spawnYaw = yaw;
            EditorUtility.SetDirty(graph);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, TahliaScene);
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// A parked car is a static copy of a traffic car body: its scripts (TrafficCar, VehicleController)
        /// and rigidbodies go, its colliders stay (static). Removing only TrafficCar and the Rigidbody left
        /// VehicleController and the Rigidbody it requires: 320 parked cars simulating as vehicles cost
        /// 95 ms per frame (2026-11-23).
        /// </summary>
        static void MakeParked(GameObject car)
        {
            // A few passes: a component can't go while another requires it (VehicleVisuals -> VehicleController -> Rigidbody)
            for (int pass = 0; pass < 4; pass++)
            {
                foreach (var b in car.GetComponentsInChildren<MonoBehaviour>(true)) if (b != null) Object.DestroyImmediate(b);
                foreach (var rb in car.GetComponentsInChildren<Rigidbody>(true)) if (rb != null) Object.DestroyImmediate(rb);
            }
        }

        /// <summary>Applies <see cref="MakeParked"/> to the built city scene. -executeMethod ...OsmCityBuilder.FixParkedCars</summary>
        public static void FixParkedCars()
        {
            var scene = EditorSceneManager.OpenScene(TahliaScene, OpenSceneMode.Single);
            var root = GameObject.Find("World/ParkedCars").transform;
            foreach (Transform c in root) MakeParked(c.gameObject);
            int left = root.GetComponentsInChildren<Rigidbody>(true).Length + root.GetComponentsInChildren<MonoBehaviour>(true).Length;
            Debug.Log($"[MASAR] Parked cars: {root.childCount} made static ({left} scripts or rigidbodies left)");
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, TahliaScene);
        }

        static void FindSpawn(RoadNetwork net, Rect area, out Vector3 pos, out float yaw)
        {
            // Tahlia (Prince Mohammed bin Abdulaziz Street) if it is in the map, else any primary road: the
            // point deepest inside the map whose road 150 m ahead is also well inside, so the drive starts in
            // the city heading into it. OSM ways run past the map's edge: "a third of the way along" put the
            // car outside the map on bare ground, facing out (found 2026-11-23).
            float Inset(Vector2 q) => Mathf.Min(Mathf.Min(q.x - area.xMin, area.xMax - q.x), Mathf.Min(q.y - area.yMin, area.yMax - q.y));
            RoadNetwork.Road best = null; float bestScore = float.MinValue, bestS = 0f;
            foreach (var r in net.Roads)
            {
                if (r.Class < RoadClass.Primary || r.Graded) continue;
                bool named = r.Name.Contains("Tahlia") || r.Name.Contains("Mohammed") || r.Name.Contains("محمد");
                float len = RoadNetwork.Length(r.P);
                for (float s = 0f; s + 150f <= len; s += 10f)
                {
                    PointAt(r.P, s, out var a, out _);
                    PointAt(r.P, s + 150f, out var b, out _);
                    float score = Mathf.Min(Mathf.Min(Inset(a), Inset(b)), 800f) + (named ? 2000f : 0f);
                    if (score > bestScore) { bestScore = score; best = r; bestS = s; }
                }
            }
            if (best == null) best = net.Roads[0];
            PointAt(best.P, bestS, out var p, out var d);
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
