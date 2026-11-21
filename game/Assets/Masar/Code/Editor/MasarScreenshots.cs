using System.IO;
using Masar.Cameras;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Masar.EditorTools
{
    /// <summary>
    /// Renders review screenshots of the test block from fixed viewpoints, through the real
    /// URP pipeline with post-processing, at a chosen quality tier.
    ///   -executeMethod Masar.EditorTools.MasarScreenshots.CaptureAll [-quality High]
    /// Output: build/screenshots/*.png (repo root). Edit-mode renders: the car is at rest,
    /// and animated props (signal lamps) show their initial state.
    /// </summary>
    public static class MasarScreenshots
    {
        private struct View
        {
            public string Name;
            public Vector3 Pos;
            public Vector3 LookAt;
            public float Fov;
            public bool FromCarInterior;
        }

        [MenuItem("MASAR/Review/Capture Screenshots")]
        public static void CaptureAll()
        {
            string quality = "High";
            var args = System.Environment.GetCommandLineArgs();
            int qi = System.Array.IndexOf(args, "-quality");
            if (qi >= 0 && qi + 1 < args.Length) quality = args[qi + 1];
            int level = System.Array.IndexOf(QualitySettings.names, quality);
            if (level >= 0) QualitySettings.SetQualityLevel(level, true);

            // -masarScene Tahlia: the generated city map instead of the test block
            int si = System.Array.IndexOf(args, "-masarScene");
            bool city = si >= 0 && si + 1 < args.Length && args[si + 1] == "Tahlia";
            var scene = EditorSceneManager.OpenScene(city ? City.OsmCityBuilder.TahliaScene : TestBlockBuilder.ScenePath, OpenSceneMode.Single);
            if (city) quality = "Tahlia_" + quality;
            var car = Object.FindFirstObjectByType<Masar.Vehicles.VehicleController>();

            // -masarCar <id>: review a catalog car in place of the scene's test sedan
            string carId = null;
            int ci = System.Array.IndexOf(args, "-masarCar");
            if (ci >= 0 && ci + 1 < args.Length) carId = args[ci + 1];
            var catalog = AssetDatabase.LoadAssetAtPath<Masar.Vehicles.CarCatalog>(ImportedCarBuilder.CatalogPath);
            var entry = carId != null && catalog != null ? catalog.Find(carId) : null;
            if (entry != null && car != null)
            {
                var swap = (GameObject)PrefabUtility.InstantiatePrefab(entry.LoadPrefab(), scene);
                swap.transform.SetPositionAndRotation(car.transform.position, car.transform.rotation);
                car.gameObject.SetActive(false);
                car = swap.GetComponent<Masar.Vehicles.VehicleController>();
                quality += "_" + carId;
            }
            Vector3 c = car != null ? car.transform.position : Vector3.zero;
            Vector3 fwd = car != null ? car.transform.forward : Vector3.right;

            Vector3 rgt = Vector3.Cross(Vector3.up, fwd).normalized;
            var cityViews = new[]
            {
                new View { Name = "01_chase", Pos = c - fwd * 6.2f + Vector3.up * 2.0f, LookAt = c + fwd * 4f + Vector3.up * 0.9f, Fov = 58f },
                new View { Name = "02_cockpit", FromCarInterior = true, Fov = 62f },
                new View { Name = "03_street_ahead", Pos = c + fwd * 3f + rgt * 9f + Vector3.up * 1.7f, LookAt = c + fwd * 80f + Vector3.up * 4f, Fov = 60f },
                new View { Name = "04_street_behind", Pos = c + fwd * 40f - rgt * 4f + Vector3.up * 1.7f, LookAt = c - fwd * 40f + Vector3.up * 5f, Fov = 60f },
                new View { Name = "05_frontage", Pos = c + rgt * 2f + Vector3.up * 1.7f, LookAt = c + fwd * 25f + rgt * 30f + Vector3.up * 8f, Fov = 60f },
                new View { Name = "06_aerial_low", Pos = c - fwd * 60f + rgt * 40f + Vector3.up * 45f, LookAt = c + fwd * 120f, Fov = 55f },
                new View { Name = "07_aerial_high", Pos = c - fwd * 250f + Vector3.up * 260f, LookAt = c + fwd * 200f, Fov = 55f },
                new View { Name = "08_overhead", Pos = c + Vector3.up * 420f + fwd * 0.1f, LookAt = c, Fov = 60f },
                new View { Name = "09_junction_ahead", Pos = c + fwd * 120f + rgt * 25f + Vector3.up * 18f, LookAt = c + fwd * 220f, Fov = 60f },
                new View { Name = "10_car_side", Pos = c + rgt * -5.5f + Vector3.up * 1.1f + fwd * 0.5f, LookAt = c + Vector3.up * 0.7f, Fov = 45f },
                new View { Name = "11_pedestrians", Pos = c + fwd * 4f - rgt * 3f + Vector3.up * 1.6f, LookAt = c + fwd * 14f - rgt * 3f + Vector3.up * 1.0f, Fov = 55f },
            };
            if (city)
            {
                // Line-up of every pedestrian prefab (bind pose) to check textures and heights
                int k = 0;
                foreach (var g in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Masar/Prefabs/Pedestrians" }))
                {
                    var pf = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g));
                    if (pf == null || !pf.name.StartsWith("Ped_Mixamo")) continue;
                    var inst = (GameObject)PrefabUtility.InstantiatePrefab(pf, scene);
                    var basePos = c + fwd * 14f + rgt * (-8f + k * 0.9f);
                    inst.transform.SetPositionAndRotation(new Vector3(basePos.x, 0f, basePos.z), Quaternion.LookRotation(-fwd));
                    k++;
                }
            }
            var views = new[]
            {
                new View { Name = "01_chase", Pos = c - fwd * 6.2f + Vector3.up * 2.0f, LookAt = c + fwd * 4f + Vector3.up * 0.9f, Fov = 58f },
                new View { Name = "02_cockpit", FromCarInterior = true, Fov = 62f },
                new View { Name = "03_avenue_street_level", Pos = new Vector3(-40f, 1.7f, -15.5f), LookAt = new Vector3(20f, 4f, -4f), Fov = 60f },
                new View { Name = "04_shops_closeup", Pos = new Vector3(-62f, 1.6f, -14f), LookAt = new Vector3(-48f, 5f, -27f), Fov = 55f },
                new View { Name = "05_median_palms_uturn", Pos = new Vector3(5f, 1.5f, -4.5f), LookAt = new Vector3(40f, 3f, 1f), Fov = 58f },
                new View { Name = "06_intersection", Pos = new Vector3(85f, 6f, -11f), LookAt = new Vector3(112f, 2f, 0f), Fov = 60f },
                new View { Name = "07_mosque", Pos = new Vector3(40f, 2f, -48f), LookAt = new Vector3(72f, 12f, -80f), Fov = 60f },
                new View { Name = "08_villa_street", Pos = new Vector3(-60f, 1.7f, -126f), LookAt = new Vector3(-10f, 3f, -118f), Fov = 60f },
                new View { Name = "09_aerial", Pos = new Vector3(-150f, 70f, 60f), LookAt = new Vector3(0f, 0f, -50f), Fov = 50f },
                new View { Name = "12_facade_closeup", Pos = new Vector3(-55f, 1.7f, -12f), LookAt = new Vector3(-45f, 12f, -28f), Fov = 62f },
                new View { Name = "11_palm_closeup", Pos = new Vector3(-18f, 1.7f, -6.5f), LookAt = new Vector3(-6f, 6.5f, 0f), Fov = 58f },
                new View { Name = "10_car_side", Pos = c + Vector3.Cross(Vector3.up, fwd) * -5.5f + Vector3.up * 1.1f + fwd * 0.5f, LookAt = c + Vector3.up * 0.7f, Fov = 45f },
            };

            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "build", "screenshots"));
            Directory.CreateDirectory(dir);

            var go = new GameObject("ScreenshotCamera");
            var cam = go.AddComponent<Camera>();
            var data = go.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.antialiasingQuality = AntialiasingQuality.High;
            cam.farClipPlane = 1800f;
            const int W = 2420, H = 1668; // iPad Pro 11" (M5) native resolution
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            cam.targetTexture = rt;

            // -masarViews 01,02,10 renders only those views
            int vi = System.Array.IndexOf(args, "-masarViews");
            var only = vi >= 0 && vi + 1 < args.Length ? args[vi + 1].Split(',') : null;
            foreach (var v in city ? cityViews : views)
            {
                if (only != null && System.Array.IndexOf(only, v.Name.Substring(0, 2)) < 0) continue;
                if (v.FromCarInterior && car != null)
                {
                    var anchors = car.GetComponentInChildren<VehicleCameraAnchors>();
                    var eye = anchors != null && anchors.driverEye != null ? anchors.driverEye : car.transform;
                    go.transform.SetPositionAndRotation(eye.position, car.transform.rotation * Quaternion.Euler(4f, 0f, 0f));
                    cam.nearClipPlane = 0.03f;
                }
                else
                {
                    go.transform.position = v.Pos;
                    go.transform.rotation = Quaternion.LookRotation(v.LookAt - v.Pos, Vector3.up);
                    cam.nearClipPlane = 0.2f;
                }
                cam.fieldOfView = v.Fov;
                // Render twice: the first render warms up temporal effects and shadows
                cam.Render();
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
                tex.Apply();
                RenderTexture.active = null;
                string path = Path.Combine(dir, $"{v.Name}_{quality}.png");
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                Debug.Log($"[MASAR] Screenshot {path}");
            }
            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(go);
        }
    }
}
