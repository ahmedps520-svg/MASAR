using System.Collections;
using System.Globalization;
using System.IO;
using Masar.Core;
using Masar.Diagnostics;
using Masar.Vehicles;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Masar.Game
{
    /// <summary>
    /// Unattended QA run for player builds, enabled only by the "-masarSmokeTest" launch argument:
    /// load the test block, drive straight with scripted inputs for a fixed time, record the
    /// measured performance, save a screenshot (with HUD and overlay), write a report, and quit.
    /// It never runs in normal play.
    /// </summary>
    public sealed class SmokeTest : MonoBehaviour
    {
        private sealed class ScriptedDriver : IDriverInputSource
        {
            public DriverControls Controls { get; set; }
            public DriverActions Actions { get; set; }
            public Vector2 Look => Vector2.zero;
            public Vector3 Move => Vector3.zero;
        }

        public static bool Requested => System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-masarSmokeTest") >= 0;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void MaybeStart()
        {
            if (!Requested) return;
            var go = new GameObject("MASAR SmokeTest");
            DontDestroyOnLoad(go);
            go.AddComponent<SmokeTest>();
        }

        private static string Arg(string key)
        {
            var a = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(a, key);
            return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
        }

        private static void SwitchOff(string feature)
        {
            var urp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
            switch (feature)
            {
                case "stp":
                    if (urp != null) urp.upscalingFilter = UnityEngine.Rendering.Universal.UpscalingFilterSelection.Auto;
                    break;
                case "ssao":
                    var field = typeof(UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset).GetField("m_RendererDataList",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (urp != null && field?.GetValue(urp) is UnityEngine.Rendering.Universal.ScriptableRendererData[] list)
                        foreach (var data in list)
                            if (data != null)
                                foreach (var feat in data.rendererFeatures)
                                    if (feat != null && feat.GetType().Name.Contains("AmbientOcclusion")) feat.SetActive(false);
                    break;
                case "palms":
                    var palms = GameObject.Find("DatePalms");
                    if (palms != null) palms.SetActive(false);
                    break;
                case "wear":
                    foreach (var r in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
                        if (r.gameObject.name.StartsWith("RoadWear")) r.enabled = false;
                    break;
                case "grime":
                    foreach (var r in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
                        if (r.gameObject.name.Contains("City_FacadeGrime")) r.enabled = false;
                    break;
                case "detail":
                    foreach (var r in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
                        foreach (var m in r.sharedMaterials)
                            if (m != null) m.DisableKeyword("_DETAIL_MULX2");
                    break;
                case "sky":
                    RenderSettings.skybox = null;
                    break;
                default:
                    // "go.<name>": a scene root or a child of World, by name (case-insensitive), e.g. go.traffic
                    if (feature.StartsWith("go."))
                    {
                        string n = feature.Substring(3);
                        foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
                        {
                            if (root.name.ToLowerInvariant() == n) { root.SetActive(false); goto found; }
                            foreach (Transform c in root.transform)
                                if (root.name == "World" && c.name.ToLowerInvariant() == n) { c.gameObject.SetActive(false); goto found; }
                        }
                    }
                    Debug.LogWarning($"[MASAR smoke] unknown -masarOff feature '{feature}'");
                    return;
                    found:
                    break;
            }
            Debug.Log($"[MASAR smoke] switched off: {feature}");
        }

        private static int PedCount()
        {
            var ps = FindFirstObjectByType<Masar.World.Traffic.PedestrianSystem>();
            return ps != null ? ps.Count : 0;
        }

        private IEnumerator Start()
        {
            string dir = Application.persistentDataPath;
            Debug.Log($"[MASAR smoke] start, output to {dir}");
            string map = Arg("-masarMap") ?? "TestBlock";
            SceneManager.LoadScene(Application.CanStreamedLevelBeLoaded(map) ? map : "TestBlock");
            yield return null;
            yield return null;
            // The city streams its building blocks: measure once the blocks around the car are in
            if (FindFirstObjectByType<Masar.World.CityLodManager>() != null)
            {
                float w0 = Time.realtimeSinceStartup;
                while (!Masar.World.CityLodManager.Settled && Time.realtimeSinceStartup - w0 < 120f) yield return null;
                Debug.Log($"[MASAR smoke] city blocks loaded: {Masar.World.CityLodManager.Needed} in {Time.realtimeSinceStartup - w0:F1}s (settled {Masar.World.CityLodManager.Settled})");
            }
            // Optional overrides for comparable per-tier measurements
            string q = Arg("-masarQuality");
            if (q != null)
            {
                int qi = System.Array.IndexOf(QualitySettings.names, q);
                if (qi >= 0) QualitySettings.SetQualityLevel(qi, true);
            }
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-masarUncapped") >= 0)
            {
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = 1000;
            }
            float duration = float.TryParse(Arg("-masarSmokeSeconds"), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 10f;
            string tag = QualitySettings.names[QualitySettings.GetQualityLevel()];
            // A/B cost measurement: "-masarOff stp,ssao,palms,wear,grime,detail,sky" switches features off
            string off = Arg("-masarOff");
            if (!string.IsNullOrEmpty(off))
            {
                foreach (var f in off.Split(',')) SwitchOff(f.Trim().ToLowerInvariant());
                tag += "_no-" + off.Replace(',', '-');
            }
            // "-masarCar id" is applied by DriveSession; it only changes the report name here
            string carArg = Arg("-masarCar");
            if (!string.IsNullOrEmpty(carArg)) tag += "_car-" + carArg;
            string mapArg = Arg("-masarMap");
            if (!string.IsNullOrEmpty(mapArg)) tag += "_map-" + mapArg;
            var car = FindFirstObjectByType<VehicleController>();
            if (car == null) { Debug.LogError("[MASAR smoke] no vehicle found"); Application.Quit(2); yield break; }
            var driver = new ScriptedDriver();
            yield return new WaitForSeconds(1.5f);
            car.Input = driver;
            driver.Actions = new DriverActions { SelectDrive = true };
            yield return null;
            driver.Actions = default;
            driver.Controls = new DriverControls { Throttle = 0.6f };

            var perf = PerformanceMonitor.Instance;
            float t = 0f, sumFps = 0f, worst = 0f; int n = 0;
            double sumGpu = 0, sumCpu = 0; int nGpu = 0, nCpu = 0;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                if (t > 2f && perf != null)  // skip warm-up
                {
                    var s = perf.Latest;
                    sumFps += s.Fps; n++;
                    worst = Mathf.Max(worst, s.FrameMsWorst);
                    if (s.GpuMs > 0) { sumGpu += s.GpuMs; nGpu++; }
                    if (s.CpuMainMs > 0) { sumCpu += s.CpuMainMs; nCpu++; }
                }
                yield return null;
            }

            string shot = Path.Combine(dir, $"masar_smoke_{tag}.png");
            ScreenCapture.CaptureScreenshot(shot);
            yield return new WaitForSeconds(0.5f);

            var last = perf != null ? perf.Latest : default;
            var inv = CultureInfo.InvariantCulture;
            string report =
                $"device={SystemInfo.deviceModel}\ngpu={SystemInfo.graphicsDeviceName} ({SystemInfo.graphicsDeviceType})\n" +
                $"resolution={Screen.width}x{Screen.height}\nquality={QualitySettings.names[QualitySettings.GetQualityLevel()]}\n" +
                $"renderScale={GraphicsQuality.CurrentRenderScale().ToString("F2", inv)}\n" +
                $"avgFps={(n > 0 ? sumFps / n : -1).ToString("F1", inv)}\nworstFrameMs={worst.ToString("F1", inv)}\n" +
                $"avgCpuMainMs={(nCpu > 0 ? sumCpu / nCpu : -1).ToString("F2", inv)}\navgGpuMs={(nGpu > 0 ? sumGpu / nGpu : -1).ToString("F2", inv)}\n" +
                $"unityAllocMB={(last.UnityAllocatedBytes / 1048576.0).ToString("F0", inv)}\nfootprintMB={(last.FootprintBytes < 0 ? -1 : last.FootprintBytes / 1048576.0).ToString("F0", inv)}\n" +
                $"drawCalls={last.DrawCalls}\nbatches={last.Batches}\ntriangles={last.Triangles}\n" +
                $"carSpeedKmh={(car.Telemetry.ForwardSpeedMs * 3.6f).ToString("F0", inv)}\ncarGear={car.Telemetry.Gear}\ncarUpright={car.transform.up.y.ToString("F3", inv)}\n" +
                $"trafficCars={(Masar.World.Traffic.TrafficSystem.Instance != null ? Masar.World.Traffic.TrafficSystem.Instance.CarCount : 0)}\n" +
                $"pedestrians={PedCount()}\n";
            File.WriteAllText(Path.Combine(dir, $"masar_smoke_{tag}.txt"), report);
            Debug.Log("[MASAR smoke] report\n" + report);

            // Optional: exercise the one-tap diagnostics path (local save + upload if configured)
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-masarDiagnostics") >= 0)
            {
                var up = Masar.Game.Diagnostics.DiagnosticsUploader.Ensure();
                up.Send("smoke-test");
                float wait = 0f;
                while ((up.Busy || wait < 0.2f) && wait < 40f) { wait += Time.unscaledDeltaTime; yield return null; }
                Debug.Log("[MASAR smoke] diagnostics: " + up.LastStatus);
            }
            Application.Quit(0);
        }
    }
}
