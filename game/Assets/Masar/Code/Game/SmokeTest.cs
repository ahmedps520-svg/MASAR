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

        private IEnumerator Start()
        {
            string dir = Application.persistentDataPath;
            Debug.Log($"[MASAR smoke] start, output to {dir}");
            SceneManager.LoadScene("TestBlock");
            yield return null;
            yield return null;
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
            while (t < 10f)
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

            string shot = Path.Combine(dir, "masar_smoke.png");
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
                $"carSpeedKmh={(car.Telemetry.ForwardSpeedMs * 3.6f).ToString("F0", inv)}\ncarGear={car.Telemetry.Gear}\ncarUpright={car.transform.up.y.ToString("F3", inv)}\n";
            File.WriteAllText(Path.Combine(dir, "masar_smoke.txt"), report);
            Debug.Log("[MASAR smoke] report\n" + report);
            Application.Quit(0);
        }
    }
}
