using System.Collections.Generic;
using Masar.Cameras;
using Masar.Game;
using Masar.Game.UI;
using Masar.Vehicles;
using Masar.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Masar.EditorTools
{
    /// <summary>
    /// Builds the Phase 0 visual benchmark: one Jeddah-style urban block, fully generated from code.
    ///
    /// Layout (meters, +X east, +Z north):
    ///  - Divided avenue along X (z = 0): 3 lanes each way, a 5 m planted median with date palms
    ///    and double-arm street lights, a median U-turn opening, and signalized cross streets at x = ±110.
    ///  - A block south of the avenue: apartment buildings with ground-floor shops along the
    ///    avenue, a mosque, a parking lot and walled villas along a local street at z = -124.5.
    ///  - Buildings on the far sides of every street, so it reads as a city, not an island.
    /// This is a hand-tuned test layout, not a real Jeddah location. Real road geometry from
    /// OpenStreetMap arrives with the road-network pipeline (Month 3).
    /// </summary>
    public static class TestBlockBuilder
    {
        public const string ScenePath = "Assets/Masar/Scenes/TestBlock.unity";
        public const string MenuScenePath = "Assets/Masar/Scenes/MainMenu.unity";
        const string MeshDir = "Assets/Masar/Generated/Meshes";

        // Avenue cross-section
        const float MedianHalf = 2.5f, CarriageOuter = 13f, SidewalkOuter = 18f;
        const float CrossX = 110f, CrossHalf = 4.5f, CrossWalk = 3f;
        const float SouthZ = -124.5f;
        const float Extent = 260f;
        const float Kerb = 0.15f, MedianKerb = 0.2f;
        const float UTurnX0 = 22f, UTurnX1 = 34f;

        [MenuItem("MASAR/Build/Test Block Scene")]
        public static void BuildAll()
        {
            MaterialLibrary.EnsureFolders();
            var carPrefab = TestCarBuilder.BuildPrefab();
            BuildMenuScene();
            BuildDriveScene(carPrefab);
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(MenuScenePath, true),
                new EditorBuildSettingsScene(ScenePath, true),
            };
            AssetDatabase.SaveAssets();
            Debug.Log("[MASAR] Scenes built: MainMenu, TestBlock");
        }

        // ------------------------------------------------------------------ menu scene

        private static void BuildMenuScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cam = new GameObject("Main Camera", typeof(Camera)).GetComponent<Camera>();
            cam.tag = "MainCamera";
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.04f, 0.05f, 0.07f);
            var data = cam.gameObject.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = false;
            new GameObject("MainMenu").AddComponent<MainMenu>();
            EditorSceneManager.SaveScene(scene, MenuScenePath);
        }

        // ------------------------------------------------------------------ drive scene

        private static void BuildDriveScene(GameObject carPrefab)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var m = CityMaterials.Create();
            var world = new GameObject("World").transform;

            var ground = new MeshBatch();
            var roads = new MeshBatch();
            var raised = new MeshBatch();
            BuildGround(ground, m);
            BuildRoads(roads, m);
            BuildMarkings(roads, m);
            BuildRoadDetails(roads, m);
            BuildRaised(raised, m);
            // The 5 km ground plane is collidable but excluded from occlusion (it would inflate the bake volume)
            foreach (var g in ground.Flush(world, "Ground", MeshDir, collider: true))
                GameObjectUtility.SetStaticEditorFlags(g, StaticEditorFlags.ReflectionProbeStatic);
            foreach (var g in roads.Flush(world, "Roads", MeshDir, collider: true)) MarkStatic(g, occluder: false);
            foreach (var g in raised.Flush(world, "Sidewalks", MeshDir, collider: true)) MarkStatic(g, occluder: false);

            BuildStreetFurniture(world, m);
            BuildBuildings(world, m);
            BuildSignals(world, m);
            BuildLighting();

            // Player vehicle + spawn
            var spawn = new GameObject("Spawn_Avenue_Eastbound").transform;
            spawn.position = new Vector3(-180f, 0.15f, -7.75f);
            spawn.rotation = Quaternion.Euler(0f, 90f, 0f);
            var car = (GameObject)PrefabUtility.InstantiatePrefab(carPrefab);
            car.transform.SetPositionAndRotation(spawn.position, spawn.rotation);

            // Camera
            var camGo = new GameObject("Main Camera", typeof(Camera), typeof(CameraRig));
            camGo.tag = "MainCamera";
            var cam = camGo.GetComponent<Camera>();
            cam.farClipPlane = 1500f;
            cam.nearClipPlane = 0.25f;
            cam.fieldOfView = 60f;
            camGo.transform.position = spawn.position + new Vector3(-6f, 2f, 0f);
            var camData = camGo.AddComponent<UniversalAdditionalCameraData>();
            camData.renderPostProcessing = true;
            camData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            camData.antialiasingQuality = AntialiasingQuality.High;
            var rig = camGo.GetComponent<CameraRig>();
            rig.target = car.GetComponent<VehicleController>();

            // Session
            var session = new GameObject("DriveSession").AddComponent<DriveSession>();
            session.vehicle = car.GetComponent<VehicleController>();
            session.cameraRig = rig;
            session.spawnPoint = spawn;

            // Bake environment lighting + reflection probes (no lightmaps in Phase 0)
            var ls = new LightingSettings { bakedGI = false, realtimeGI = false, name = "TestBlockLighting" };
            AssetDatabase.CreateAsset(ls, "Assets/Masar/Scenes/TestBlockLighting.lighting");
            Lightmapping.lightingSettings = ls;
            EditorSceneManager.SaveScene(scene, ScenePath);
            bool baked = Lightmapping.Bake();
            Debug.Log($"[MASAR] Environment/reflection bake: {(baked ? "OK" : "FAILED")}");

            // Umbra occlusion culling: buildings occlude, everything static is an occludee
            StaticOcclusionCulling.smallestOccluder = 4f;
            StaticOcclusionCulling.smallestHole = 0.25f;
            StaticOcclusionCulling.backfaceThreshold = 100f;
            var t0 = System.DateTime.Now;
            bool occ = StaticOcclusionCulling.Compute();
            EditorSceneManager.SaveScene(scene, ScenePath);
            var occFile = new System.IO.FileInfo("Assets/Masar/Scenes/TestBlock/OcclusionCullingData.asset");
            Debug.Log($"[MASAR] Occlusion bake: {(occ ? "OK" : "FAILED")} in {(System.DateTime.Now - t0).TotalSeconds:F0}s, " +
                      $"data file {(occFile.Exists ? occFile.Length / 1024 + " KB" : "missing")}");
        }

        // ------------------------------------------------------------------ ground & roads

        private static void BuildGround(MeshBatch b, CityMaterials m)
        {
            // Large ground plane so the horizon is ground + haze, never the sky's lower hemisphere
            b[m.Ground].FloorRect(-2500f, -2500f, 2500f, 2500f, -0.02f, 6f);
        }

        private static void BuildRoads(MeshBatch b, CityMaterials m)
        {
            var a = b[m.Asphalt];
            const float uv = 7f; // meters per asphalt texture repeat
            // Avenue carriageways
            a.FloorRect(-Extent, MedianHalf, Extent, CarriageOuter, 0f, uv);
            a.FloorRect(-Extent, -CarriageOuter, Extent, -MedianHalf, 0f, uv);
            // Median openings: intersections and U-turn
            foreach (float cx in new[] { -CrossX, CrossX })
                a.FloorRect(cx - CrossHalf - CrossWalk, -MedianHalf, cx + CrossHalf + CrossWalk, MedianHalf, 0f, uv);
            a.FloorRect(UTurnX0, -MedianHalf, UTurnX1, MedianHalf, 0f, uv);
            // Cross streets (north stubs + block sides down to the south street)
            foreach (float cx in new[] { -CrossX, CrossX })
            {
                a.FloorRect(cx - CrossHalf, CarriageOuter, cx + CrossHalf, 80f, 0f, uv);
                a.FloorRect(cx - CrossHalf, SouthZ - CrossHalf, cx + CrossHalf, -CarriageOuter, 0f, uv);
            }
            // South local street
            a.FloorRect(-CrossX - CrossHalf, SouthZ - CrossHalf, CrossX + CrossHalf, SouthZ + CrossHalf, 0f, uv);
            // Parking lot inside the block
            a.FloorRect(-96f, -78f, -30f, -48f, 0.152f, uv);
        }

        private static void BuildMarkings(MeshBatch b, CityMaterials m)
        {
            var w = b[m.PaintWhite];
            var y = b[m.PaintYellow];
            const float h = 0.006f;
            bool InIntersection(float x) => Mathf.Abs(Mathf.Abs(x) - CrossX) < CrossHalf + CrossWalk + 3f;

            foreach (int side in new[] { -1, 1 })
            {
                // Solid yellow along the median, solid white along the outer edge
                Line(y, -Extent, Extent, side * (MedianHalf + 0.35f), 0.15f, h, InIntersection, x => x > UTurnX0 - 1 && x < UTurnX1 + 1);
                Line(w, -Extent, Extent, side * (CarriageOuter - 0.35f), 0.15f, h, InIntersection, null);
                // Dashed lane lines (3 m dash, 6 m gap)
                foreach (float lane in new[] { MedianHalf + 3.5f, MedianHalf + 7.0f })
                    Dashed(w, -Extent, Extent, side * lane, 0.12f, 3f, 6f, h, InIntersection);
            }

            // Stop lines and zebra crossings at both signalized intersections
            foreach (float cx in new[] { -CrossX, CrossX })
            {
                foreach (int dir in new[] { -1, 1 })
                {
                    // Crossing over the avenue on each side of the cross street
                    float crossCenter = cx + dir * (CrossHalf + CrossWalk * 0.5f + 0.5f);
                    Zebra(w, crossCenter, -CarriageOuter + 0.4f, -MedianHalf - 0.2f, true, h);
                    Zebra(w, crossCenter, MedianHalf + 0.2f, CarriageOuter - 0.4f, true, h);
                    // Stop lines before the crossings (eastbound south side approaches from -X, westbound from +X)
                    float stopX = cx + dir * (CrossHalf + CrossWalk + 1.6f);
                    if (dir < 0) w.FloorRect(stopX - 0.2f, -CarriageOuter + 0.3f, stopX + 0.2f, -MedianHalf - 0.3f, h + 0.001f);
                    else w.FloorRect(stopX - 0.2f, MedianHalf + 0.3f, stopX + 0.2f, CarriageOuter - 0.3f, h + 0.001f);
                }
                // Crossings over the cross street mouths
                ZebraZ(w, cx, CarriageOuter + 2.5f, h);
                ZebraZ(w, cx, -CarriageOuter - 2.5f, h);
                // Cross street centre lines
                DashedZ(w, cx, CarriageOuter + 6f, 80f, 0.12f, 3f, 5f, h);
                DashedZ(w, cx, SouthZ + CrossHalf + 4f, -CarriageOuter - 6f, 0.12f, 3f, 5f, h);
            }
            // South street centre line
            Dashed(w, -CrossX + CrossHalf + 3f, CrossX - CrossHalf - 3f, SouthZ, 0.12f, 3f, 5f, h, null);

            // Parking bays (angled lines on the lot)
            for (float x = -94f; x <= -32f; x += 2.7f)
            {
                w.FloorRect(x, -62.5f, x + 0.1f, -57.5f, 0.152f + h);
                w.FloorRect(x, -68.5f, x + 0.1f, -63.5f, 0.152f + h);
            }
        }

        private static void Line(MeshBuilder b, float x0, float x1, float z, float width, float y,
                                 System.Func<float, bool> skip, System.Func<float, bool> skip2)
        {
            float step = 2f;
            float start = x0;
            bool on = false;
            for (float x = x0; x <= x1; x += step)
            {
                bool s = (skip != null && skip(x)) || (skip2 != null && skip2(x));
                if (!s && !on) { start = x; on = true; }
                if ((s || x + step > x1) && on)
                {
                    b.FloorRect(start, z - width * 0.5f, x, z + width * 0.5f, y);
                    on = false;
                }
            }
        }

        private static void Dashed(MeshBuilder b, float x0, float x1, float z, float width, float dash, float gap, float y, System.Func<float, bool> skip)
        {
            for (float x = x0; x < x1; x += dash + gap)
            {
                if (skip != null && (skip(x) || skip(x + dash))) continue;
                b.FloorRect(x, z - width * 0.5f, Mathf.Min(x1, x + dash), z + width * 0.5f, y);
            }
        }

        private static void DashedZ(MeshBuilder b, float x, float z0, float z1, float width, float dash, float gap, float y)
        {
            float lo = Mathf.Min(z0, z1), hi = Mathf.Max(z0, z1);
            for (float z = lo; z < hi; z += dash + gap)
                b.FloorRect(x - width * 0.5f, z, x + width * 0.5f, Mathf.Min(hi, z + dash), y);
        }

        private static void Zebra(MeshBuilder b, float xCenter, float z0, float z1, bool alongX, float y)
        {
            // Stripes run along the direction of traffic (X), spaced across the road (Z)
            for (float z = z0; z + 0.5f <= z1; z += 1.0f)
                b.FloorRect(xCenter - CrossWalk * 0.5f, z, xCenter + CrossWalk * 0.5f, z + 0.5f, y);
        }

        private static void ZebraZ(MeshBuilder b, float xCenter, float zCenter, float y)
        {
            for (float x = xCenter - CrossHalf + 0.3f; x + 0.5f <= xCenter + CrossHalf - 0.3f; x += 1.0f)
                b.FloorRect(x, zCenter - CrossWalk * 0.5f, x + 0.5f, zCenter + CrossWalk * 0.5f, y);
        }

        // ------------------------------------------------------------------ sidewalks, median, plots

        private static void BuildRaised(MeshBatch b, CityMaterials m)
        {
            // Avenue sidewalks (gaps at the cross streets)
            foreach (int side in new[] { -1, 1 })
            {
                float z0 = side > 0 ? CarriageOuter : -SidewalkOuter, z1 = side > 0 ? SidewalkOuter : -CarriageOuter;
                foreach (var (x0, x1) in Spans(-Extent, Extent, new[] { (-CrossX - CrossHalf, -CrossX + CrossHalf), (CrossX - CrossHalf, CrossX + CrossHalf) }))
                    Slab(b, m.Pavers, m.Concrete, x0, z0, x1, z1, Kerb, 1.5f, side > 0 ? 1 : 3);
            }
            // Median (gaps at intersections and the U-turn) with alternating black/yellow kerbs
            foreach (var (x0, x1) in Spans(-Extent, Extent, new[]
                     {
                         (-CrossX - CrossHalf - CrossWalk, -CrossX + CrossHalf + CrossWalk),
                         (CrossX - CrossHalf - CrossWalk, CrossX + CrossHalf + CrossWalk),
                         (UTurnX0, UTurnX1)
                     }))
            {
                Slab(b, m.Ground, m.Concrete, x0, -MedianHalf, x1, MedianHalf, MedianKerb, 4f, -1);
                PaintedKerb(b, m, x0, x1, MedianHalf, MedianKerb);
                PaintedKerb(b, m, x0, x1, -MedianHalf, MedianKerb);
            }
            // Cross-street sidewalks
            foreach (float cx in new[] { -CrossX, CrossX })
            {
                foreach (int side in new[] { -1, 1 })
                {
                    float x0 = side < 0 ? cx - CrossHalf - CrossWalk : cx + CrossHalf;
                    float x1 = side < 0 ? cx - CrossHalf : cx + CrossHalf + CrossWalk;
                    Slab(b, m.Pavers, m.Concrete, x0, SidewalkOuter, x1, 80f, Kerb, 1.5f, -1);
                    bool blockSide = (cx > 0 && side < 0) || (cx < 0 && side > 0);
                    float zEnd = blockSide ? SouthZ + CrossHalf : SouthZ - CrossHalf - CrossWalk;
                    Slab(b, m.Pavers, m.Concrete, x0, zEnd, x1, -SidewalkOuter, Kerb, 1.5f, -1);
                }
            }
            // South street sidewalks
            Slab(b, m.Pavers, m.Concrete, -CrossX + CrossHalf, SouthZ + CrossHalf, CrossX - CrossHalf, SouthZ + CrossHalf + CrossWalk, Kerb, 1.5f, -1);
            Slab(b, m.Pavers, m.Concrete, -CrossX - CrossHalf - CrossWalk, SouthZ - CrossHalf - CrossWalk, CrossX + CrossHalf + CrossWalk, SouthZ - CrossHalf, Kerb, 1.5f, -1);

            // Plots (raised to sidewalk level, ground texture): the block interior and the outer areas
            Slab(b, m.Ground, m.Concrete, -CrossX + CrossHalf + CrossWalk, SouthZ + CrossHalf + CrossWalk, CrossX - CrossHalf - CrossWalk, -SidewalkOuter, Kerb, 6f, -1);
            Slab(b, m.Ground, m.Concrete, -Extent, SidewalkOuter, -CrossX - CrossHalf - CrossWalk, 80f, Kerb, 6f, -1);
            Slab(b, m.Ground, m.Concrete, -CrossX + CrossHalf + CrossWalk, SidewalkOuter, CrossX - CrossHalf - CrossWalk, 80f, Kerb, 6f, -1);
            Slab(b, m.Ground, m.Concrete, CrossX + CrossHalf + CrossWalk, SidewalkOuter, Extent, 80f, Kerb, 6f, -1);
            Slab(b, m.Ground, m.Concrete, -Extent, -200f, -CrossX - CrossHalf - CrossWalk, -SidewalkOuter, Kerb, 6f, -1);
            Slab(b, m.Ground, m.Concrete, CrossX + CrossHalf + CrossWalk, -200f, Extent, -SidewalkOuter, Kerb, 6f, -1);
            Slab(b, m.Ground, m.Concrete, -CrossX + CrossHalf - 7f, -200f, CrossX - CrossHalf + 7f, SouthZ - CrossHalf - CrossWalk, Kerb, 6f, -1);
            // Small plaza with pavers in front of the mosque
            b[m.Pavers].FloorRect(40f, -70f, 98f, -44f, Kerb + 0.003f, 1.5f);
        }

        /// <summary>Raised slab: top surface, vertical kerb faces on all sides, and a concrete kerb strip on the edges.</summary>
        private static void Slab(MeshBatch b, Material top, Material kerb, float x0, float z0, float x1, float z1, float h, float uv, int stripSide)
        {
            if (x1 - x0 < 0.05f || z1 - z0 < 0.05f) return;
            b[top].FloorRect(x0, z0, x1, z1, h, uv);
            var k = b[kerb];
            k.Box(new Vector3(x0, 0f, z0), new Vector3(x1, h - 0.001f, z1), 1f, false, false);
            // 0.3 m concrete kerb strip along the edges facing traffic
            if (stripSide == 1) k.FloorRect(x0, z0, x1, z0 + 0.3f, h + 0.002f, 1f);
            if (stripSide == 3) k.FloorRect(x0, z1 - 0.3f, x1, z1, h + 0.002f, 1f);
        }

        private static void PaintedKerb(MeshBatch b, CityMaterials m, float x0, float x1, float zEdge, float h)
        {
            int i = 0;
            for (float x = x0; x < x1; x += 1f, i++)
            {
                var mat = i % 2 == 0 ? m.KerbBlack : m.KerbYellow;
                float xe = Mathf.Min(x1, x + 1f);
                float zIn = zEdge > 0 ? zEdge - 0.25f : zEdge;
                float zOut = zEdge > 0 ? zEdge : zEdge + 0.25f;
                b[mat].Box(new Vector3(x, 0f, zIn), new Vector3(xe, h + 0.01f, zOut), 1f, false, true);
            }
        }

        private static IEnumerable<(float, float)> Spans(float x0, float x1, (float, float)[] gaps)
        {
            var sorted = new List<(float, float)>(gaps);
            sorted.Sort((a, c) => a.Item1.CompareTo(c.Item1));
            float cur = x0;
            foreach (var g in sorted)
            {
                if (g.Item1 > cur) yield return (cur, Mathf.Min(g.Item1, x1));
                cur = Mathf.Max(cur, g.Item2);
            }
            if (cur < x1) yield return (cur, x1);
        }

        // ------------------------------------------------------------------ furniture

        private static void BuildStreetFurniture(Transform world, CityMaterials m)
        {
            var parent = new GameObject("StreetFurniture").transform;
            parent.SetParent(world, false);
            var batch = new MeshBatch();
            var colliders = new List<(Vector3, float, float)>();
            int seed = 1;
            // Median palms every 12 m and double-arm lights every 36 m
            for (float x = -Extent + 6f; x < Extent - 6f; x += 12f)
            {
                if (Mathf.Abs(Mathf.Abs(x) - CrossX) < CrossHalf + CrossWalk + 4f) continue;
                if (x > UTurnX0 - 3f && x < UTurnX1 + 3f) continue;
                bool light = Mathf.RoundToInt((x + Extent) / 12f) % 3 == 0;
                if (light)
                {
                    BuildingKit.StreetLight(batch, m, new Vector3(x, MedianKerb, 0f), true, 0f);
                    colliders.Add((new Vector3(x, MedianKerb, 0f), 0.2f, 10f));
                }
                else
                {
                    BuildingKit.DatePalm(batch, m, new Vector3(x, MedianKerb, 0f), seed++);
                    colliders.Add((new Vector3(x, MedianKerb, 0f), 0.3f, 6f));
                }
            }
            // Sidewalk lights along the block streets (single arm, facing the road)
            for (float z = -SidewalkOuter - 10f; z > SouthZ + 8f; z -= 30f)
            {
                BuildingKit.StreetLight(batch, m, new Vector3(CrossX - CrossHalf - 1.5f, Kerb, z), false, -90f);
                BuildingKit.StreetLight(batch, m, new Vector3(-CrossX + CrossHalf + 1.5f, Kerb, z), false, 90f);
            }
            // Palms in front of the villas and in the small park
            for (float x = -90f; x <= 90f; x += 18f) BuildingKit.DatePalm(batch, m, new Vector3(x, Kerb, SouthZ + CrossHalf + 1.5f), seed++);
            for (float x = -20f; x <= 30f; x += 9f) for (float z = -78f; z <= -50f; z += 9f) BuildingKit.DatePalm(batch, m, new Vector3(x + (seed % 3), Kerb, z), seed++);

            foreach (var g in batch.Flush(parent, "Furniture", MeshDir, collider: false)) MarkStatic(g, occluder: false);
            foreach (var (p, r, h) in colliders)
            {
                var c = new GameObject("PoleCollider").AddComponent<CapsuleCollider>();
                c.transform.SetParent(parent, false);
                c.transform.position = p + Vector3.up * h * 0.5f;
                c.radius = r;
                c.height = h;
            }
        }

        // ------------------------------------------------------------------ buildings

        private static void BuildBuildings(Transform world, CityMaterials m)
        {
            var parent = new GameObject("Buildings").transform;
            parent.SetParent(world, false);
            int seed = 100;
            int[] floors = { 5, 6, 4, 7, 5 };
            // Block frontage on the avenue (fronts face +Z, toward the avenue)
            for (int i = 0; i < 5; i++)
            {
                float cx = -80f + i * 40f;
                var spec = new BuildingKit.Spec { Width = 34f, Depth = 16f, Floors = floors[i], FloorHeight = 3.3f, GroundHeight = 4.6f, Shops = true, PlasterIndex = i, Seed = seed++, BalconyChance = 0.55f, AcChance = 0.6f, StoneGround = true };
                AddBuilding(parent, m, $"Block_Avenue_{i}", new Vector3(cx, Kerb, -SidewalkOuter - 9.5f), 0f, spec);
            }
            // Mosque facing the avenue side of the block interior
            {
                var batch = new MeshBatch();
                BuildingKit.Mosque(batch, m, new Vector3(70f, Kerb, -82f), 0f);
                Finish(parent, batch, "Mosque", new Bounds(new Vector3(70f, 6f, -82f), new Vector3(24f, 12f, 26f)));
            }
            // Villas along the south street (fronts face -Z, toward the street)
            for (int i = 0; i < 5; i++)
            {
                var batch = new MeshBatch();
                float cx = -84f + i * 34f;
                if (cx > 40f) cx += 6f;
                var c = new Vector3(cx, Kerb, SouthZ + CrossHalf + CrossWalk + 16f);
                BuildingKit.Villa(batch, m, c, 180f, 30f, 30f, seed++);
                Finish(parent, batch, $"Villa_Block_{i}", new Bounds(c + Vector3.up * 4f, new Vector3(30f, 8f, 30f)));
            }
            // Far side of the avenue (fronts face -Z, toward the avenue): taller commercial buildings
            float x = -Extent + 20f;
            int k = 0;
            while (x < Extent - 20f)
            {
                float width = 28f + (k * 7 % 13);
                if (Mathf.Abs(Mathf.Abs(x + width * 0.5f) - CrossX) < width * 0.5f + CrossHalf + CrossWalk + 1f) { x += 12f; continue; }
                var spec = new BuildingKit.Spec { Width = width, Depth = 18f, Floors = 6 + (k * 5 % 7), FloorHeight = 3.3f, GroundHeight = 5f, Shops = true, PlasterIndex = k + 1, Seed = seed++, BalconyChance = 0.35f, AcChance = 0.5f, StoneGround = true };
                AddBuilding(parent, m, $"North_{k}", new Vector3(x + width * 0.5f, Kerb, SidewalkOuter + 10.5f), 180f, spec);
                x += width + 4f + (k % 3) * 2f;
                k++;
            }
            // South side of the avenue outside the block (east and west)
            for (int i = 0; i < 4; i++)
            {
                float w = 30f;
                float cxW = -CrossX - CrossHalf - CrossWalk - 20f - i * 36f;
                float cxE = CrossX + CrossHalf + CrossWalk + 20f + i * 36f;
                var specW = new BuildingKit.Spec { Width = w, Depth = 16f, Floors = 4 + i, FloorHeight = 3.3f, GroundHeight = 4.6f, Shops = true, PlasterIndex = i + 2, Seed = seed++, BalconyChance = 0.5f, AcChance = 0.6f, StoneGround = i % 2 == 0 };
                var specE = specW; specE.Seed = seed++; specE.Floors = 7 - i; specE.PlasterIndex = i;
                AddBuilding(parent, m, $"SouthW_{i}", new Vector3(cxW, Kerb, -SidewalkOuter - 9.5f), 0f, specW);
                AddBuilding(parent, m, $"SouthE_{i}", new Vector3(cxE, Kerb, -SidewalkOuter - 9.5f), 0f, specE);
            }
            // Villas across the south street (fronts face +Z)
            for (int i = 0; i < 6; i++)
            {
                var batch = new MeshBatch();
                var c = new Vector3(-95f + i * 38f, Kerb, SouthZ - CrossHalf - CrossWalk - 17f);
                BuildingKit.Villa(batch, m, c, 0f, 32f, 30f, seed++);
                Finish(parent, batch, $"Villa_South_{i}", new Bounds(c + Vector3.up * 4f, new Vector3(32f, 8f, 30f)));
            }
            // Apartments facing the cross streets from outside the block
            for (int i = 0; i < 3; i++)
            {
                float z = -40f - i * 30f;
                var spec = new BuildingKit.Spec { Width = 24f, Depth = 14f, Floors = 4 + i, FloorHeight = 3.3f, GroundHeight = 4.4f, Shops = i == 0, PlasterIndex = i + 1, Seed = seed++, BalconyChance = 0.5f, AcChance = 0.7f };
                AddBuilding(parent, m, $"EastSide_{i}", new Vector3(CrossX + CrossHalf + CrossWalk + 8f, Kerb, z), -90f, spec);
                spec.Seed = seed++;
                AddBuilding(parent, m, $"WestSide_{i}", new Vector3(-CrossX - CrossHalf - CrossWalk - 8f, Kerb, z), 90f, spec);
            }
        }

        private static void AddBuilding(Transform parent, CityMaterials m, string name, Vector3 pos, float yaw, BuildingKit.Spec spec)
        {
            var batch = new MeshBatch();
            BuildingKit.Apartment(batch, m, pos, yaw, spec);
            float h = spec.GroundHeight + spec.Floors * spec.FloorHeight;
            var rot = Quaternion.Euler(0f, yaw, 0f);
            var size = rot * new Vector3(spec.Width, h, spec.Depth);
            Finish(parent, batch, name, new Bounds(pos + Vector3.up * h * 0.5f, new Vector3(Mathf.Abs(size.x), h, Mathf.Abs(size.z))));
        }

        private static void Finish(Transform parent, MeshBatch batch, string name, Bounds collider)
        {
            var root = new GameObject(name).transform;
            root.SetParent(parent, false);
            var all = new List<Renderer>();
            var core = new List<Renderer>();
            foreach (var g in batch.Flush(root, name, MeshDir, collider: false))
            {
                MarkStatic(g, occluder: true);
                var r = g.GetComponent<Renderer>();
                all.Add(r);
                if (!IsDetailMaterial(r.sharedMaterial)) core.Add(r);
            }
            // Distance-based detail: LOD0 = everything, LOD1 = walls/glass/roof only, then culled.
            // Thresholds are screen-height fractions; each quality tier scales them through its LOD bias.
            var lod = root.gameObject.AddComponent<LODGroup>();
            // Hard switch: the dropped parts are small details, so popping is negligible, and it avoids
            // the dithered cross-fade cost on the GPU (and the dither speckle seen in the review renders).
            lod.fadeMode = LODFadeMode.None;
            lod.SetLODs(new[] { new LOD(0.22f, all.ToArray()), new LOD(0.012f, core.ToArray()) });
            lod.RecalculateBounds();
            var col = root.gameObject.AddComponent<BoxCollider>();
            col.center = collider.center;
            col.size = collider.size;
        }

        /// <summary>Small facade details that are invisible at distance: frames, railings, AC units, tanks, gates, dishes.</summary>
        private static bool IsDetailMaterial(Material mat)
        {
            if (mat == null) return false;
            string n = mat.name;
            return n.StartsWith("City_Frame") || n.StartsWith("City_RailingMetal") || n.StartsWith("City_AcUnit") ||
                   n.StartsWith("City_WaterTank") || n.StartsWith("City_GateMetal") || n.StartsWith("City_PoleMetal") ||
                   n.StartsWith("City_SatelliteDish");
        }

        /// <summary>Street-level wear and furniture: asphalt repair patches, manhole covers, sidewalk bollards.</summary>
        private static void BuildRoadDetails(MeshBatch b, CityMaterials m)
        {
            var rnd = new System.Random(7);
            bool NearJunction(float x) => Mathf.Abs(Mathf.Abs(x) - CrossX) < CrossHalf + CrossWalk + 6f || (x > UTurnX0 - 2f && x < UTurnX1 + 2f);
            // Repair patches on the avenue carriageways
            for (int i = 0; i < 46; i++)
            {
                float x = Mathf.Lerp(-Extent + 10f, Extent - 20f, (float)rnd.NextDouble());
                if (NearJunction(x)) continue;
                int side = rnd.Next(2) == 0 ? -1 : 1;
                float zc = side * (MedianHalf + 1.0f + (float)rnd.NextDouble() * 9.0f);
                float w = 0.8f + (float)rnd.NextDouble() * 2.6f, len = 1.2f + (float)rnd.NextDouble() * 5.5f;
                b[m.AsphaltPatch].FloorRect(x, zc - w * 0.5f, x + len, zc + w * 0.5f, 0.003f, 7f);
            }
            // Manhole covers in the lanes
            for (int i = 0; i < 24; i++)
            {
                float x = Mathf.Lerp(-Extent + 10f, Extent - 10f, (float)rnd.NextDouble());
                if (NearJunction(x)) continue;
                int side = rnd.Next(2) == 0 ? -1 : 1;
                float z = side * (MedianHalf + 1.75f + rnd.Next(3) * 3.5f);
                b[m.Manhole].Cylinder(new Vector3(x, 0f, z), 0.33f, 0.33f, 0.008f, 18, true);
            }
            // Bollards along the avenue sidewalks (protecting the shopfronts), every 2.5 m
            for (float x = -Extent + 2f; x < Extent - 2f; x += 2.5f)
            {
                if (Mathf.Abs(Mathf.Abs(x) - CrossX) < CrossHalf + CrossWalk + 1.5f) continue;
                foreach (int side in new[] { -1, 1 })
                {
                    var p = new Vector3(x, Kerb, side * (CarriageOuter + 0.5f));
                    b[m.Bollard].Cylinder(p, 0.085f, 0.075f, 0.82f, 10, true);
                }
            }
        }

        // ------------------------------------------------------------------ signals

        private static void BuildSignals(Transform world, CityMaterials m)
        {
            var parent = new GameObject("SignalProps (visual only)").transform;
            parent.SetParent(world, false);
            int idx = 0;
            foreach (float cx in new[] { -CrossX, CrossX })
            {
                // Avenue approaches: eastbound (south carriageway) and westbound (north carriageway)
                Signal(parent, m, new Vector3(cx - CrossHalf - CrossWalk - 1.0f, Kerb, -SidewalkOuter + 1.2f), 90f, 0f, idx++);
                Signal(parent, m, new Vector3(cx + CrossHalf + CrossWalk + 1.0f, Kerb, SidewalkOuter - 1.2f), -90f, 0f, idx++);
                // Median heads
                Signal(parent, m, new Vector3(cx - CrossHalf - CrossWalk - 0.8f, MedianKerb, -1.2f), 90f, 0f, idx++);
                Signal(parent, m, new Vector3(cx + CrossHalf + CrossWalk + 0.8f, MedianKerb, 1.2f), -90f, 0f, idx++);
                // Cross-street approaches (opposite phase)
                Signal(parent, m, new Vector3(cx + CrossHalf + 1.2f, Kerb, -SidewalkOuter - 1.5f), 0f, 17f, idx++);
                Signal(parent, m, new Vector3(cx - CrossHalf - 1.2f, Kerb, SidewalkOuter + 1.5f), 180f, 17f, idx++);
            }
        }

        private static void Signal(Transform parent, CityMaterials m, Vector3 basePos, float facingYaw, float phase, int idx)
        {
            var root = new GameObject($"Signal_{idx}");
            root.transform.SetParent(parent, false);
            root.transform.position = basePos;
            root.transform.rotation = Quaternion.Euler(0f, facingYaw + 180f, 0f);
            var pole = new MeshBuilder();
            pole.Cylinder(Vector3.zero, 0.1f, 0.08f, 3.2f, 10, true);
            pole.BoxC(new Vector3(0f, 3.8f, 0f), new Vector3(0.36f, 1.05f, 0.28f));
            pole.BoxC(new Vector3(0f, 3.8f, 0.17f), new Vector3(0.5f, 1.2f, 0.04f)); // backboard
            Mesh(root.transform, "Pole", pole, m.SignalBox, $"Signal_{idx}_Pole");
            var red = Lamp(root.transform, m.SignalRed, 4.15f, idx, "R");
            var amber = Lamp(root.transform, m.SignalAmber, 3.8f, idx, "A");
            var green = Lamp(root.transform, m.SignalGreen, 3.45f, idx, "G");
            var cyc = root.AddComponent<SignalPropCycler>();
            cyc.red = red; cyc.amber = amber; cyc.green = green;
            cyc.phaseOffset = phase;
            var c = root.AddComponent<CapsuleCollider>();
            c.center = new Vector3(0f, 1.6f, 0f);
            c.radius = 0.12f;
            c.height = 3.2f;
        }

        private static Renderer Lamp(Transform parent, Material mat, float y, int idx, string tag)
        {
            var b = new MeshBuilder();
            b.CylinderAxis(new Vector3(0f, y, -0.15f), Vector3.forward, 0.11f, 0.04f, 16, true);
            return Mesh(parent, "Lamp" + tag, b, mat, $"Signal_{idx}_{tag}");
        }

        private static Renderer Mesh(Transform parent, string name, MeshBuilder b, Material mat, string assetName)
        {
            b.FixWinding();
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var mesh = b.ToMesh(assetName);
            string path = $"{MeshDir}/{assetName}.asset";
            if (AssetDatabase.LoadAssetAtPath<Mesh>(path) != null) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mesh, path);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            return r;
        }

        // ------------------------------------------------------------------ lighting & atmosphere

        private static void BuildLighting()
        {
            // Late-afternoon sun from the west-southwest (Red Sea side), warm and hazy
            var sunGo = new GameObject("Sun");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.90f, 0.76f);
            sun.intensity = 2.3f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.92f;
            sunGo.transform.rotation = Quaternion.Euler(30f, 62f, 0f);
            sunGo.AddComponent<UniversalAdditionalLightData>();
            RenderSettings.sun = sun;

            var sky = new Material(Shader.Find("Skybox/Procedural"));
            sky.SetFloat("_SunSize", 0.035f);
            sky.SetFloat("_SunSizeConvergence", 6f);
            sky.SetFloat("_AtmosphereThickness", 1.05f);
            sky.SetColor("_SkyTint", new Color(0.50f, 0.56f, 0.66f));
            sky.SetColor("_GroundColor", new Color(0.66f, 0.62f, 0.56f));
            sky.SetFloat("_Exposure", 1.2f);
            AssetDatabase.CreateAsset(sky, $"{MaterialLibrary.MatRoot}/Sky_JeddahAfternoon.mat");
            RenderSettings.skybox = sky;
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.ambientIntensity = 1.0f;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            RenderSettings.reflectionIntensity = 1.0f;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.0018f;
            RenderSettings.fogColor = new Color(0.78f, 0.77f, 0.74f);

            // Reflection probes along the avenue and over the block
            int i = 0;
            for (float x = -200f; x <= 200f; x += 100f)
                Probe(new Vector3(x, 4f, 0f), new Vector3(110f, 30f, 60f), i++);
            Probe(new Vector3(0f, 4f, -70f), new Vector3(240f, 30f, 130f), i++);

            // Post-processing
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, "Assets/Masar/Scenes/TestBlockPost.asset");
            var tone = profile.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.ACES);
            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(1.05f);
            bloom.intensity.Override(0.22f);
            bloom.scatter.Override(0.6f);
            var ca = profile.Add<ColorAdjustments>(true);
            ca.postExposure.Override(0.25f);
            ca.contrast.Override(10f);
            ca.saturation.Override(4f);
            var wb = profile.Add<WhiteBalance>(true);
            wb.temperature.Override(7f);
            var vig = profile.Add<Vignette>(true);
            vig.intensity.Override(0.16f);
            foreach (var c in profile.components) AssetDatabase.AddObjectToAsset(c, profile);
            EditorUtility.SetDirty(profile);
            var volGo = new GameObject("PostProcess");
            var vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.sharedProfile = profile;
        }

        private static void Probe(Vector3 center, Vector3 size, int i)
        {
            var go = new GameObject($"ReflectionProbe_{i}");
            go.transform.position = center;
            var p = go.AddComponent<ReflectionProbe>();
            p.mode = ReflectionProbeMode.Baked;
            p.size = size;
            p.boxProjection = true;
            p.resolution = 256;
            p.hdr = true;
            p.importance = 1;
            p.blendDistance = 6f;
        }

        private static void MarkStatic(GameObject go, bool occluder)
        {
            var flags = StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic;
            if (occluder) flags |= StaticEditorFlags.OccluderStatic;
            GameObjectUtility.SetStaticEditorFlags(go, flags);
        }
    }
}
