using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Masar.Core;
using Masar.Diagnostics;
using Masar.Vehicles;
using UnityEngine;

namespace Masar.Game
{
    /// <summary>
    /// A timed drive on the lane autopilot that measures what the player would feel: frame times
    /// (average, 1 % and 0.1 % worst, frames over 33 and 50 ms), memory, thermal state, distance,
    /// speed, collisions and how far the car strayed from its lane. Used by the smoke test
    /// (-masarAutopilot) and by the pause menu's Benchmark drive. The report is saved under
    /// persistentDataPath/benchmarks (Files app > MASAR > benchmarks on the iPad).
    /// </summary>
    public sealed class DriveBenchmark : MonoBehaviour
    {
        public float Seconds = 180f;
        public float WarmUp = 3f;
        public bool Running { get; private set; }
        public float Elapsed { get; private set; }
        public LaneAutopilot Autopilot { get; private set; }
        public string Report { get; private set; }
        public string ReportPath { get; private set; }
        public event Action<DriveBenchmark> Finished;

        VehicleController _car;
        IDriverInputSource _previousInput;
        readonly List<float> _frames = new List<float>(30000);
        float _distance, _maxSpeed, _offLaneSeconds, _maxLateral;
        Vector3 _lastPos;
        CollisionCounter _hits;
        long _peakFootprint = -1, _peakAlloc;
        string _label;
        int _laneChanges0, _overlaps0;
        TransmissionKind _previousMode;

        /// <summary>Starts the drive. Returns false (with Report set) if the map has no lane graph.</summary>
        public bool Begin(VehicleController car, string label)
        {
            _car = car; _label = label;
            var graph = LaneAutopilot.FindGraph();
            _previousMode = car.Simulation.Drivetrain.Mode;
            Autopilot = car.gameObject.GetComponent<LaneAutopilot>();
            if (Autopilot == null) Autopilot = car.gameObject.AddComponent<LaneAutopilot>();
            if (!Autopilot.Begin(car, graph)) { Report = "Benchmark drive not started: " + Autopilot.State; car.Simulation.Drivetrain.Mode = _previousMode; Destroy(Autopilot); return false; }
            _previousInput = car.Input;
            car.Input = Autopilot;
            _hits = car.gameObject.GetComponent<CollisionCounter>();
            if (_hits == null) _hits = car.gameObject.AddComponent<CollisionCounter>();
            _hits.Clear();
            _frames.Clear();
            _distance = _maxSpeed = _offLaneSeconds = _maxLateral = 0f;
            _lastPos = car.transform.position;
            Elapsed = 0f;
            var ts0 = Masar.World.Traffic.TrafficSystem.Instance;
            _laneChanges0 = ts0 != null ? ts0.LaneChanges : 0;
            _overlaps0 = ts0 != null ? ts0.Overlaps : 0;
            Running = true;
            Debug.Log($"[MASAR bench] start ({label}), {Seconds:F0} s on the lane autopilot");
            return true;
        }

        public void Stop(bool completed)
        {
            if (!Running) return;
            Running = false;
            if (_car != null)
            {
                _car.Input = _previousInput;
                _car.Simulation.Drivetrain.Mode = _previousMode;
                if (Autopilot != null) Destroy(Autopilot);
            }
            Report = BuildReport(completed);
            try
            {
                string dir = Path.Combine(Application.persistentDataPath, "benchmarks");
                Directory.CreateDirectory(dir);
                ReportPath = Path.Combine(dir, $"masar_bench_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
                File.WriteAllText(ReportPath, Report);
            }
            catch (Exception e) { Debug.LogWarning("[MASAR bench] report not saved: " + e.Message); }
            Debug.Log("[MASAR bench] report\n" + Report);
            Finished?.Invoke(this);
        }

        void Update()
        {
            if (!Running || _car == null) return;
            if (Time.timeScale == 0f) return;   // paused: not measured
            float dt = Time.unscaledDeltaTime;
            Elapsed += dt;
            var p = _car.transform.position;
            _distance += Vector3.Distance(p, _lastPos);
            _lastPos = p;
            if (Elapsed < WarmUp) return;
            _frames.Add(dt * 1000f);
            _maxSpeed = Mathf.Max(_maxSpeed, _car.Telemetry.SpeedMs);
            float lat = Autopilot != null ? Autopilot.LateralError : 0f;
            _maxLateral = Mathf.Max(_maxLateral, lat);
            if (lat > 2.5f) _offLaneSeconds += dt;
            var perf = PerformanceMonitor.Instance;
            if (perf != null)
            {
                _peakFootprint = Math.Max(_peakFootprint, perf.Latest.FootprintBytes);
                _peakAlloc = Math.Max(_peakAlloc, perf.Latest.UnityAllocatedBytes);
            }
            if (Elapsed >= Seconds) Stop(true);
        }

        string BuildReport(bool completed)
        {
            var inv = CultureInfo.InvariantCulture;
            var sorted = new List<float>(_frames);
            sorted.Sort();
            int n = sorted.Count;
            double sum = 0; foreach (var f in sorted) sum += f;
            float Pct(float q) => n == 0 ? -1f : sorted[Mathf.Clamp(Mathf.CeilToInt(q * n) - 1, 0, n - 1)];
            int over33 = 0, over50 = 0, over100 = 0;
            foreach (var f in sorted) { if (f > 33.4f) over33++; if (f > 50f) over50++; if (f > 100f) over100++; }
            float measured = Mathf.Max(0.01f, (float)(sum / 1000.0));
            var perf = PerformanceMonitor.Instance;
            var ts = Masar.World.Traffic.TrafficSystem.Instance;
            var tod = FindFirstObjectByType<TimeOfDay>();
            string F(float v, string fmt = "F1") => v.ToString(fmt, inv);
            string MB(long b) => b < 0 ? "n/a" : (b / 1048576.0).ToString("F0", inv);
            return
                $"MASAR benchmark drive ({_label})\n" +
                $"date={DateTime.Now:yyyy-MM-dd HH:mm}\ncompleted={(completed ? "yes" : "no (stopped early)")}\n" +
                $"device={SystemInfo.deviceModel}\ngpu={SystemInfo.graphicsDeviceName}\nresolution={Screen.width}x{Screen.height}\n" +
                $"quality={QualitySettings.names[QualitySettings.GetQualityLevel()]}\nrenderScale={F(GraphicsQuality.CurrentRenderScale(), "F2")}\n" +
                $"targetFps={Application.targetFrameRate}\n" +
                $"seconds={F(measured)}\nframes={n}\navgFps={F(n / measured)}\n" +
                $"frameMsMedian={F(Pct(0.5f), "F2")}\nframeMs99={F(Pct(0.99f), "F2")}\nframeMs999={F(Pct(0.999f), "F2")}\nframeMsWorst={F(n > 0 ? sorted[n - 1] : -1f, "F1")}\n" +
                $"framesOver33ms={over33}\nframesOver50ms={over50}\nframesOver100ms={over100}\n" +
                $"peakFootprintMB={MB(_peakFootprint)}\npeakUnityAllocMB={MB(_peakAlloc)}\n" +
                $"minAvailableMB={MB(perf != null ? perf.MinAvailableBytes : -1)}\n" +
                $"peakThermal={(perf != null ? NativeMetrics.ThermalStateName(perf.PeakThermalState) : "n/a")}\n" +
                $"distanceKm={F(_distance / 1000f, "F2")}\navgSpeedKmh={F(_distance / Mathf.Max(0.1f, Elapsed) * 3.6f)}\nmaxSpeedKmh={F(_maxSpeed * 3.6f)}\n" +
                $"collisions={(_hits != null ? _hits.Count : 0)}{(_hits != null && _hits.Count > 0 ? " (" + _hits.Names + ")" : "")}\n" +
                $"maxLaneErrorM={F(_maxLateral, "F2")}\nsecondsOffLane={F(_offLaneSeconds)}\n" +
                $"trafficCars={(ts != null ? ts.CarCount : 0)}\naiLaneChanges={(ts != null ? ts.LaneChanges - _laneChanges0 : 0)}\naiOverlaps={(ts != null ? ts.Overlaps - _overlaps0 : 0)}\n" +
                $"timeOfDay={(tod != null ? tod.Current.ToString() : "n/a")}\nstreetLamps={(tod != null ? tod.LampCount : 0)}\n";
        }

        /// <summary>Report lines as "key=value" pairs, for the result panel.</summary>
        public static Dictionary<string, string> Parse(string report)
        {
            var d = new Dictionary<string, string>();
            if (report == null) return d;
            foreach (var line in report.Split('\n'))
            {
                int i = line.IndexOf('=');
                if (i > 0) d[line.Substring(0, i)] = line.Substring(i + 1);
            }
            return d;
        }
    }

    /// <summary>Counts the car's impacts with anything but the ground (contact normal mostly sideways).</summary>
    public sealed class CollisionCounter : MonoBehaviour
    {
        public int Count { get; private set; }
        readonly HashSet<string> _names = new HashSet<string>();
        public string Names => string.Join(", ", _names);
        float _last;

        public void Clear() { Count = 0; _names.Clear(); }

        void OnCollisionEnter(Collision c)
        {
            if (c.contactCount == 0) return;
            if (Mathf.Abs(c.GetContact(0).normal.y) > 0.6f) return;   // road, kerb tops
            if (c.relativeVelocity.magnitude < 1.5f) return;
            if (Time.time - _last < 0.5f) return;   // one impact, several contacts
            _last = Time.time;
            Count++;
            if (_names.Count < 8) _names.Add(c.collider.name);
        }
    }
}
