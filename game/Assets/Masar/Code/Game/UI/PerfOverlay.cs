using System.Text;
using Masar.Diagnostics;
using UnityEngine;
using UnityEngine.UI;

namespace Masar.Game.UI
{
    /// <summary>Top-left performance readout. Values marked "n/a" are not exposed by the platform.</summary>
    public sealed class PerfOverlay : MonoBehaviour
    {
        private Canvas _canvas;
        private Text _text;
        private Image _bg;
        private readonly StringBuilder _sb = new StringBuilder(512);
        private float _next;
        private bool _visible = true;

        public bool Visible
        {
            get => _visible;
            set { _visible = value; if (_canvas) _canvas.enabled = value; }
        }

        private void Awake()
        {
            _canvas = UIKit.CreateCanvas("PerfOverlay", 1000, transform);
            _bg = UIKit.Panel(_canvas.transform, "Bg", new Color(0f, 0f, 0f, 0.55f));
            UIKit.Place(_bg.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(16, -16), new Vector2(560, 400));
            _text = UIKit.Label(_bg.transform, "", 20, UIKit.Text, TextAnchor.UpperLeft);
            UIKit.Anchor(_text.rectTransform, Vector2.zero, Vector2.one, new Vector2(14, 10), new Vector2(-10, -10));
            _canvas.enabled = _visible;
        }

        private void Update()
        {
            if (!_visible || Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 0.25f;
            var p = PerformanceMonitor.Instance;
            if (p == null) return;
            var s = p.Latest;
            _sb.Clear();
            _sb.Append("MASAR Phase 0  |  ").Append(SystemInfo.deviceModel).Append('\n');
            _sb.Append("FPS ").Append(s.Fps.ToString("F0")).Append("   frame ").Append(s.FrameMs.ToString("F1")).Append(" ms   worst ").Append(s.FrameMsWorst.ToString("F1")).Append(" ms\n");
            _sb.Append("CPU main ").Append(Ms(s.CpuMainMs)).Append("   render ").Append(Ms(s.CpuRenderMs)).Append("   GPU ").Append(Ms(s.GpuMs)).Append('\n');
            _sb.Append("Footprint ").Append(Mb(s.FootprintBytes)).Append("   avail ").Append(Mb(s.AvailableBytes)).Append('\n');
            _sb.Append("Unity alloc ").Append(Mb(s.UnityAllocatedBytes)).Append("   reserved ").Append(Mb(s.UnityReservedBytes)).Append("   GC ").Append(Mb(s.ManagedHeapBytes)).Append('\n');
            _sb.Append("Thermal ").Append(NativeMetrics.ThermalStateName(s.ThermalState));
            if (NativeMetrics.LowPowerMode == 1) _sb.Append("   LOW POWER MODE");
            _sb.Append('\n');
            _sb.Append("Draw calls ").Append(Num(s.DrawCalls)).Append("   batches ").Append(Num(s.Batches)).Append("   setpass ").Append(Num(s.SetPassCalls)).Append('\n');
            _sb.Append("Triangles ").Append(s.Triangles < 0 ? "n/a" : (s.Triangles / 1000f).ToString("F0") + "k").Append("   visible renderers ").Append(Num(s.VisibleRenderers)).Append('\n');
            _sb.Append("Vehicles ").Append(s.Vehicles).Append("   pedestrians ").Append(s.Pedestrians).Append('\n');
            _sb.Append("Quality ").Append(s.QualityName).Append("   scale ").Append(s.RenderScale < 0 ? "n/a" : s.RenderScale.ToString("F2"))
               .Append("   ").Append(s.ScreenWidth).Append('x').Append(s.ScreenHeight).Append('\n');
            if (p.Recording) _sb.Append("[REC] ").Append(System.IO.Path.GetFileName(p.RecordingPath)).Append('\n');
            _sb.Append("<F3 / D-pad up: toggle>");
            _text.text = _sb.ToString();
        }

        private static string Ms(double v) => v < 0 ? "n/a" : v.ToString("F1") + " ms";
        private static string Mb(long b) => b < 0 ? "n/a" : (b / 1048576.0).ToString("F0") + " MB";
        private static string Num(long v) => v < 0 ? "n/a" : v.ToString();
    }
}
