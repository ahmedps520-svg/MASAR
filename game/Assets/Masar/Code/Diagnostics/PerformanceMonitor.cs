using System;
using System.Globalization;
using System.IO;
using System.Text;
using Masar.Core;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Profiling;

namespace Masar.Diagnostics
{
    /// <summary>One sample of measured metrics. Negative values mean "not available on this platform".</summary>
    public struct PerfSample
    {
        /// <summary>Seconds since startup when the sample was published.</summary>
        public float Time;
        public float Fps;
        public float FrameMs;
        public float FrameMsWorst;      // worst frame in the window (1% low proxy)
        public double CpuMainMs;
        public double CpuRenderMs;
        public double GpuMs;
        public long ManagedHeapBytes;
        public long UnityAllocatedBytes;
        public long UnityReservedBytes;
        public long FootprintBytes;
        public long AvailableBytes;
        public int ThermalState;
        public long DrawCalls;
        public long Batches;
        public long SetPassCalls;
        public long Triangles;
        public int Vehicles;
        public int Pedestrians;
        public int VisibleRenderers;
        public string QualityName;
        public float RenderScale;
        public int ScreenWidth, ScreenHeight;
    }

    /// <summary>
    /// Collects frame timing, memory, thermal and rendering counters, and can record them
    /// to CSV for soak and thermal tests. Values come straight from engine or OS APIs.
    /// If an API returns nothing, the value is reported as unavailable, never estimated.
    /// </summary>
    public sealed class PerformanceMonitor : MonoBehaviour
    {
        public static PerformanceMonitor Instance { get; private set; }

        [Tooltip("Seconds per published sample.")]
        public float sampleWindow = 0.5f;

        public PerfSample Latest { get; private set; }

        /// <summary>The last 10 minutes of samples (one every sampleWindow), oldest first. Used by diagnostics reports.</summary>
        public readonly System.Collections.Generic.List<PerfSample> History = new System.Collections.Generic.List<PerfSample>(1200);
        private const int HistoryCapacity = 1200;
        /// <summary>Highest thermal state seen this session (-1 = not available).</summary>
        public int PeakThermalState { get; private set; } = -1;
        /// <summary>Lowest available-memory reading this session (bytes, -1 = not available).</summary>
        public long MinAvailableBytes { get; private set; } = -1;
        public bool Recording => _csv != null;
        public string RecordingPath { get; private set; }
        /// <summary>Supplied by the game layer (e.g. the URP render scale), since diagnostics must not depend on rendering code.</summary>
        public Func<float> RenderScaleProvider;
        /// <summary>Supplied by the game layer: number of renderers currently visible.</summary>
        public Func<int> VisibleRendererCounter;

        private readonly FrameTiming[] _timings = new FrameTiming[1];
        private ProfilerRecorder _drawCalls, _batches, _setPass, _tris;
        private float _accum, _worst;
        private int _frames;
        private StreamWriter _csv;
        private float _csvTimer;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        private void OnEnable()
        {
            _drawCalls = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            _batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
            _setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            _tris = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
        }

        private void OnDisable()
        {
            _drawCalls.Dispose(); _batches.Dispose(); _setPass.Dispose(); _tris.Dispose();
            StopRecording();
        }

        private void Update()
        {
            FrameTimingManager.CaptureFrameTimings();

            float dt = Time.unscaledDeltaTime;
            _accum += dt;
            _frames++;
            _worst = Mathf.Max(_worst, dt);
            if (_accum < sampleWindow) return;

            var s = new PerfSample
            {
                Time = UnityEngine.Time.realtimeSinceStartup,
                Fps = _frames / _accum,
                FrameMs = _accum / _frames * 1000f,
                FrameMsWorst = _worst * 1000f,
                CpuMainMs = -1, CpuRenderMs = -1, GpuMs = -1,
                ManagedHeapBytes = GC.GetTotalMemory(false),
                UnityAllocatedBytes = Profiler.GetTotalAllocatedMemoryLong(),
                UnityReservedBytes = Profiler.GetTotalReservedMemoryLong(),
                FootprintBytes = NativeMetrics.FootprintBytes,
                AvailableBytes = NativeMetrics.AvailableMemoryBytes,
                ThermalState = NativeMetrics.ThermalState,
                DrawCalls = _drawCalls.Valid ? _drawCalls.LastValue : -1,
                Batches = _batches.Valid ? _batches.LastValue : -1,
                SetPassCalls = _setPass.Valid ? _setPass.LastValue : -1,
                Triangles = _tris.Valid ? _tris.LastValue : -1,
                Vehicles = SimRegistry.VehicleCount,
                Pedestrians = SimRegistry.PedestrianCount,
                VisibleRenderers = VisibleRendererCounter != null ? VisibleRendererCounter() : -1,
                QualityName = QualitySettings.names.Length > 0 ? QualitySettings.names[QualitySettings.GetQualityLevel()] : "?",
                RenderScale = RenderScaleProvider != null ? RenderScaleProvider() : -1f,
                ScreenWidth = Screen.width,
                ScreenHeight = Screen.height,
            };

            if (FrameTimingManager.IsFeatureEnabled() && FrameTimingManager.GetLatestTimings(1, _timings) > 0)
            {
                var ft = _timings[0];
                if (ft.cpuMainThreadFrameTime > 0) s.CpuMainMs = ft.cpuMainThreadFrameTime;
                if (ft.cpuRenderThreadFrameTime > 0) s.CpuRenderMs = ft.cpuRenderThreadFrameTime;
                if (ft.gpuFrameTime > 0) s.GpuMs = ft.gpuFrameTime;
            }

            Latest = s;
            _accum = 0f; _frames = 0; _worst = 0f;
            if (History.Count >= HistoryCapacity) History.RemoveAt(0);
            History.Add(s);
            if (s.ThermalState > PeakThermalState) PeakThermalState = s.ThermalState;
            if (s.AvailableBytes >= 0 && (MinAvailableBytes < 0 || s.AvailableBytes < MinAvailableBytes)) MinAvailableBytes = s.AvailableBytes;

            if (_csv != null)
            {
                _csvTimer += sampleWindow;
                WriteCsv(s);
            }
        }

        // ------------------------------------------------------------------ CSV

        public void StartRecording()
        {
            if (_csv != null) return;
            string dir = Application.persistentDataPath;
            RecordingPath = Path.Combine(dir, $"masar_perf_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
            _csv = new StreamWriter(RecordingPath, false, Encoding.UTF8);
            _csv.WriteLine("t_s,fps,frame_ms,worst_ms,cpu_main_ms,cpu_render_ms,gpu_ms,footprint_mb,available_mb,unity_alloc_mb,thermal,draw_calls,batches,tris,vehicles,quality,render_scale,width,height,device");
            _csvTimer = 0f;
        }

        public void StopRecording()
        {
            if (_csv == null) return;
            _csv.Flush();
            _csv.Dispose();
            _csv = null;
        }

        private void WriteCsv(in PerfSample s)
        {
            var inv = CultureInfo.InvariantCulture;
            _csv.WriteLine(string.Join(",",
                _csvTimer.ToString("F1", inv), s.Fps.ToString("F1", inv), s.FrameMs.ToString("F2", inv), s.FrameMsWorst.ToString("F2", inv),
                s.CpuMainMs.ToString("F2", inv), s.CpuRenderMs.ToString("F2", inv), s.GpuMs.ToString("F2", inv),
                Mb(s.FootprintBytes), Mb(s.AvailableBytes), Mb(s.UnityAllocatedBytes), s.ThermalState.ToString(inv),
                s.DrawCalls.ToString(inv), s.Batches.ToString(inv), s.Triangles.ToString(inv), s.Vehicles.ToString(inv),
                s.QualityName, s.RenderScale.ToString("F2", inv), s.ScreenWidth.ToString(inv), s.ScreenHeight.ToString(inv),
                SystemInfo.deviceModel.Replace(',', ' ')));
            if (Time.frameCount % 20 == 0) _csv.Flush();
        }

        private static string Mb(long bytes) => bytes < 0 ? "-1" : (bytes / 1048576.0).ToString("F0", CultureInfo.InvariantCulture);
    }
}
