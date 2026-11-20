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

            EditorSceneManager.OpenScene(TestBlockBuilder.ScenePath, OpenSceneMode.Single);
            var car = Object.FindFirstObjectByType<Masar.Vehicles.VehicleController>();
            Vector3 c = car != null ? car.transform.position : Vector3.zero;
            Vector3 fwd = car != null ? car.transform.forward : Vector3.right;

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
            cam.farClipPlane = 1500f;
            const int W = 2420, H = 1668; // iPad Pro 11" (M5) native resolution
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            cam.targetTexture = rt;

            foreach (var v in views)
            {
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
