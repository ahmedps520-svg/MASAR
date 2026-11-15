using System;
using System.IO;
using System.Linq;
using Masar.Core;
using Masar.Diagnostics;
using Masar.Vehicles;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Masar.Game.Diagnostics
{
    /// <summary>
    /// Builds the one-tap diagnostics report: device and OS facts, the performance history
    /// (FPS, frame/CPU/GPU time, memory, thermal), every input device the OS exposes (the
    /// on-device G923 test), the current vehicle state, settings and the recent log.
    ///
    /// Privacy: no device name (it often contains the owner's name), no account data, no location.
    /// </summary>
    public static class DiagnosticsReport
    {
        public const int Schema = 1;

        public struct BuildInfo
        {
            public string build;
            public string commit;
            public string date;
        }

        public static BuildInfo ReadBuildInfo()
        {
            try
            {
                string p = Path.Combine(Application.streamingAssetsPath, "build_info.json");
                if (File.Exists(p)) return JsonUtility.FromJson<BuildInfo>(File.ReadAllText(p));
            }
            catch { }
            return new BuildInfo { build = "dev", commit = "unknown", date = "" };
        }

        public static string Build(string trigger)
        {
            var bi = ReadBuildInfo();
            var perf = PerformanceMonitor.Instance;
            var w = new JsonWriter();
            w.BeginObject();
            w.Prop("app", "MASAR").Prop("schema", Schema).Prop("trigger", trigger).Prop("sentUtc", DateTime.UtcNow.ToString("o"));

            // ---- meta
            w.Key("meta").BeginObject()
                .Prop("version", Application.version).Prop("build", bi.build).Prop("commit", bi.commit).Prop("buildDate", bi.date)
                .Prop("unity", Application.unityVersion).Prop("platform", Application.platform.ToString())
                .Prop("deviceModel", SystemInfo.deviceModel).Prop("os", SystemInfo.operatingSystem)
                .Prop("cpu", SystemInfo.processorType).Prop("cpuCount", SystemInfo.processorCount)
                .Prop("systemMemoryMB", SystemInfo.systemMemorySize)
                .Prop("gpu", SystemInfo.graphicsDeviceName).Prop("gpuApi", SystemInfo.graphicsDeviceType.ToString())
                .Prop("gpuMemoryMB", SystemInfo.graphicsMemorySize)
                .Prop("screenW", Screen.width).Prop("screenH", Screen.height).Prop("dpi", Screen.dpi)
                .Prop("refreshHz", Screen.currentResolution.refreshRateRatio.value)
                .Prop("quality", QualitySettings.names[QualitySettings.GetQualityLevel()])
                .Prop("renderScale", GraphicsQuality.CurrentRenderScale())
                .Prop("targetFps", Application.targetFrameRate)
                .Prop("batteryLevel", SystemInfo.batteryLevel).Prop("batteryStatus", SystemInfo.batteryStatus.ToString())
                .Prop("lowPowerMode", NativeMetrics.LowPowerMode)
                .Prop("thermalNow", NativeMetrics.ThermalStateName(NativeMetrics.ThermalState))
                .Prop("availableMB", Mb(NativeMetrics.AvailableMemoryBytes))
                .Prop("footprintMB", Mb(NativeMetrics.FootprintBytes))
                .Prop("sessionSeconds", Time.realtimeSinceStartup)
                .Prop("scene", SceneManager.GetActiveScene().name)
                .Prop("previousSessionUnclean", LogBuffer.PreviousSessionUnclean)
                .Prop("logErrors", LogBuffer.ErrorCount).Prop("logExceptions", LogBuffer.ExceptionCount).Prop("logWarnings", LogBuffer.WarningCount)
                .EndObject();

            // ---- performance
            w.Key("perf").BeginObject();
            if (perf != null && perf.History.Count > 0)
            {
                var h = perf.History;
                var fpsSorted = h.Select(s => s.Fps).OrderBy(x => x).ToArray();
                double p5 = fpsSorted[Mathf.Clamp(Mathf.FloorToInt(fpsSorted.Length * 0.05f), 0, fpsSorted.Length - 1)];
                w.Key("summary").BeginObject()
                    .Prop("samples", h.Count).Prop("windowSeconds", perf.sampleWindow)
                    .Prop("avgFps", h.Average(s => s.Fps)).Prop("p5Fps", p5).Prop("minFps", fpsSorted[0])
                    .Prop("maxFrameMs", h.Max(s => s.FrameMsWorst))
                    .Prop("avgCpuMainMs", AvgPositive(h.Select(s => s.CpuMainMs)))
                    .Prop("avgGpuMs", AvgPositive(h.Select(s => s.GpuMs)))
                    .Prop("peakThermal", NativeMetrics.ThermalStateName(perf.PeakThermalState))
                    .Prop("minAvailableMB", Mb(perf.MinAvailableBytes))
                    .Prop("maxFootprintMB", Mb(h.Max(s => s.FootprintBytes)))
                    .Prop("maxDrawCalls", h.Max(s => s.DrawCalls))
                    .EndObject();
                w.Key("series").BeginObject();
                Series(w, "t", h, s => s.Time);
                Series(w, "fps", h, s => s.Fps);
                Series(w, "worstMs", h, s => s.FrameMsWorst);
                Series(w, "cpuMainMs", h, s => s.CpuMainMs);
                Series(w, "gpuMs", h, s => s.GpuMs);
                Series(w, "footprintMB", h, s => s.FootprintBytes < 0 ? -1 : s.FootprintBytes / 1048576.0);
                Series(w, "availMB", h, s => s.AvailableBytes < 0 ? -1 : s.AvailableBytes / 1048576.0);
                Series(w, "thermal", h, s => s.ThermalState);
                Series(w, "drawCalls", h, s => s.DrawCalls);
                w.EndObject();
            }
            w.EndObject();

            // ---- input (includes the native GameController scan = on-device G923 test)
            var router = GameServices.Instance != null ? GameServices.Instance.Input : null;
            w.Key("input").BeginObject().Prop("activeKind", router != null ? router.ActiveKind.ToString() : "none");
            w.Key("unityDevices").BeginArray();
            foreach (var d in InputSystem.devices)
            {
                w.BeginObject().Prop("name", d.displayName).Prop("layout", d.layout)
                    .Prop("interface", d.description.interfaceName).Prop("manufacturer", d.description.manufacturer)
                    .Prop("product", d.description.product).Prop("isJoystick", d is Joystick).Prop("isGamepad", d is Gamepad)
                    .EndObject();
            }
            w.EndArray();
            w.Key("gameControllerFramework").Raw(NativeMetrics.ControllersJson);
            w.EndObject();

            // ---- vehicle (if driving)
            var car = UnityEngine.Object.FindFirstObjectByType<VehicleController>();
            w.Key("vehicle");
            if (car != null && car.Simulation != null)
            {
                var t = car.Telemetry;
                w.BeginObject().Prop("speedKmh", t.ForwardSpeedMs * 3.6).Prop("gear", t.Gear).Prop("selector", t.Selector.ToString())
                    .Prop("transmission", t.TransmissionMode.ToString()).Prop("rpm", t.EngineRpm).Prop("engineRunning", t.EngineRunning)
                    .Prop("groundedWheels", t.GroundedWheels).Prop("upright", car.transform.up.y)
                    .Prop("posX", car.transform.position.x).Prop("posZ", car.transform.position.z).EndObject();
            }
            else w.Raw("null");

            // ---- settings + log
            w.Key("settings").Raw(JsonUtility.ToJson(SettingsStore.Current));
            w.Key("log").BeginArray();
            var lines = LogBuffer.Snapshot();
            foreach (var l in lines.Skip(Math.Max(0, lines.Length - 300)))
                w.BeginObject().Prop("t", l.Time).Prop("type", l.Type.ToString()).Prop("text", l.Text).EndObject();
            w.EndArray();

            w.EndObject();
            return w.ToString();
        }

        private static double Mb(long bytes) => bytes < 0 ? -1 : bytes / 1048576.0;

        private static double AvgPositive(System.Collections.Generic.IEnumerable<double> v)
        {
            var arr = v.Where(x => x > 0).ToArray();
            return arr.Length == 0 ? -1 : arr.Average();
        }

        private static void Series(JsonWriter w, string key, System.Collections.Generic.List<PerfSample> h, Func<PerfSample, double> f)
        {
            w.Key(key).BeginArray();
            foreach (var s in h) w.Value(f(s));
            w.EndArray();
        }
    }
}
