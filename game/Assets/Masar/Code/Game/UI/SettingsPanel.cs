using System;
using Masar.Cameras;
using Masar.Diagnostics;
using Masar.Vehicles;
using UnityEngine;
using UnityEngine.UI;

namespace Masar.Game.UI
{
    /// <summary>
    /// Settings, used both from the main menu and from the pause menu. Each control
    /// changes a real engine parameter. Camera settings appear only when a camera rig is passed.
    /// </summary>
    public sealed class SettingsPanel : MonoBehaviour
    {
        public static SettingsPanel Create(Transform parent, CameraRig rig, Action onClose, Action<TransmissionKind> onTransmission = null)
        {
            var (root, bg) = UIKit.Modal(parent, "SettingsPanel", new Vector2(0.24f, 0.05f), new Vector2(0.76f, 0.95f));
            var panel = root.gameObject.AddComponent<SettingsPanel>();

            var title = UIKit.Label(bg.transform, "Settings", 40, UIKit.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIKit.Anchor(title.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(36, -84), new Vector2(-36, -20));

            UIKit.ScrollColumn(bg.transform, "Scroll", out var content);
            var scrollRt = (RectTransform)content.parent.parent;
            UIKit.Anchor(scrollRt, Vector2.zero, Vector2.one, new Vector2(36, 110), new Vector2(-36, -96));
            UIKit.VStack(content, 18, new RectOffset(0, 12, 0, 24));

            var s = SettingsStore.Current;

            // ---- graphics
            Section(content, "Graphics");
            UIKit.Segmented(content, "Quality tier (changes render scale, shadows, SSAO, HDR, anti-aliasing)",
                GraphicsQuality.Names,
                () => QualitySettings.GetQualityLevel(),
                i => { s.qualityLevel = i; GraphicsQuality.Apply(s); SettingsStore.Save(); });
            int[] caps = { 30, 60, 120 };
            UIKit.Segmented(content, "Frame-rate cap", new[] { "30", "60", "120" },
                () => Array.IndexOf(caps, s.targetFrameRate),
                i => { s.targetFrameRate = caps[i]; GraphicsQuality.Apply(s); SettingsStore.Save(); });

            // ---- driving
            Section(content, "Driving");
            UIKit.Segmented(content, "Transmission", new[] { "Automatic", "Manual (sequential)" },
                () => (int)s.transmission,
                i => { s.transmission = (TransmissionKind)i; SettingsStore.Save(); onTransmission?.Invoke(s.transmission); });
            UIKit.Segmented(content, "On-screen touch controls", new[] { "Show", "Hide" },
                () => s.showTouchControls ? 0 : 1,
                i => { s.showTouchControls = i == 0; SettingsStore.Save(); });

            // ---- sound
            Section(content, "Sound");
            UIKit.SliderRow(content, "Car sound volume (engine, tyres, wind)", 0f, 1f, s.soundVolume, v => (v * 100f).ToString("F0") + "%",
                v => { s.soundVolume = v; SettingsStore.Save(); });

            // ---- world
            Section(content, "World");
            UIKit.Segmented(content, "Time of day", new[] { "Day", "Sunset", "Night" },
                () => s.timeOfDay,
                i => { s.timeOfDay = i; SettingsStore.Save(); });

            // ---- city (generated maps)
            Section(content, "City");
            UIKit.Segmented(content, "AI traffic", new[] { "Off", "Light", "Normal", "Heavy" },
                () => s.trafficDensity,
                i => { s.trafficDensity = i; SettingsStore.Save(); });
            UIKit.Segmented(content, "Pedestrians", new[] { "Off", "Few", "Normal", "Busy" },
                () => s.pedestrianDensity,
                i => { s.pedestrianDensity = i; SettingsStore.Save(); });

            // ---- camera (in-game only)
            if (rig != null)
            {
                Section(content, "Camera: " + rig.mode);
                var cs = rig.Current;
                UIKit.SliderRow(content, "Field of view", 35f, 95f, cs.fieldOfView, v => v.ToString("F0") + "°", v => { cs.fieldOfView = v; SaveCameras(rig); });
                if (rig.mode == CameraMode.Chase)
                {
                    UIKit.SliderRow(content, "Follow distance", 3f, 12f, cs.followDistance, v => v.ToString("F1") + " m", v => { cs.followDistance = v; SaveCameras(rig); });
                    UIKit.SliderRow(content, "Height", 0.8f, 4f, cs.height, v => v.ToString("F2") + " m", v => { cs.height = v; SaveCameras(rig); });
                }
                if (rig.mode == CameraMode.Cockpit || rig.mode == CameraMode.Passenger || rig.mode == CameraMode.Hood)
                {
                    UIKit.SliderRow(content, "Seat position: forward/back", -0.25f, 0.25f, cs.offset.z, v => v.ToString("+0.00;-0.00") + " m", v => { cs.offset.z = v; SaveCameras(rig); });
                    UIKit.SliderRow(content, "Seat position: height", -0.15f, 0.15f, cs.offset.y, v => v.ToString("+0.00;-0.00") + " m", v => { cs.offset.y = v; SaveCameras(rig); });
                    UIKit.SliderRow(content, "View tilt", -15f, 15f, cs.pitch, v => v.ToString("+0;-0") + "°", v => { cs.pitch = v; SaveCameras(rig); });
                }
                if (rig.mode == CameraMode.Cockpit || rig.mode == CameraMode.Passenger)
                    UIKit.SliderRow(content, "Head motion (G-forces)", 0f, 1f, cs.headMotion, v => (v * 100f).ToString("F0") + "%", v => { cs.headMotion = v; SaveCameras(rig); });
                UIKit.SliderRow(content, "Smoothing", 0f, 1f, cs.smoothing, v => (v * 100f).ToString("F0") + "%", v => { cs.smoothing = v; SaveCameras(rig); });
                UIKit.SliderRow(content, "Camera shake", 0f, 1f, cs.shake, v => (v * 100f).ToString("F0") + "%", v => { cs.shake = v; SaveCameras(rig); });
                UIKit.SliderRow(content, "Look sensitivity", 0.2f, 3f, cs.lookSensitivity, v => v.ToString("F1") + "×", v => { cs.lookSensitivity = v; SaveCameras(rig); });
                UIKit.Size(UIKit.Button(content, "Reset all cameras to defaults", () =>
                {
                    SettingsStore.ResetCameras();
                    rig.settings = SettingsStore.Current.cameras;
                    onClose?.Invoke();
                }, 24), 64);
            }

            // ---- diagnostics
            Section(content, "Diagnostics");
            UIKit.Segmented(content, "Performance overlay (F3)", new[] { "On", "Off" },
                () => s.showPerfOverlay ? 0 : 1,
                i =>
                {
                    s.showPerfOverlay = i == 0;
                    if (GameServices.Instance) GameServices.Instance.Overlay.Visible = s.showPerfOverlay;
                    SettingsStore.Save();
                });
            var perf = PerformanceMonitor.Instance;
            Text recLabel = null;
            var rec = UIKit.Button(content, perf != null && perf.Recording ? "Stop CSV recording" : "Start CSV performance recording", () =>
            {
                if (perf == null) return;
                if (perf.Recording) perf.StopRecording(); else perf.StartRecording();
                if (recLabel) recLabel.text = perf.Recording ? "Stop CSV recording" : "Start CSV performance recording";
            }, 24);
            recLabel = UIKit.ButtonLabel(rec);
            UIKit.Size(rec, 64);
            var hint = UIKit.Label(content, "CSV files are written to the app's Documents folder (visible in the Files app on iPad).", 20, UIKit.TextDim);
            UIKit.Size(hint, 54);

            var close = UIKit.Button(bg.transform, "Close", onClose, 28, UIKit.Accent);
            UIKit.Place((RectTransform)close.transform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-36, 24), new Vector2(240, 70));
            return panel;
        }

        private static void Section(Transform parent, string text)
        {
            var t = UIKit.Label(parent, text.ToUpperInvariant(), 26, UIKit.Accent, TextAnchor.LowerLeft, FontStyle.Bold);
            UIKit.Size(t, 52);
        }

        private static void SaveCameras(CameraRig rig)
        {
            SettingsStore.Current.cameras = rig.settings;
            SettingsStore.Save();
        }
    }
}
