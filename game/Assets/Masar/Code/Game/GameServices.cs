using Masar.Diagnostics;
using Masar.Game.UI;
using Masar.Input;
using UnityEngine;

namespace Masar.Game
{
    /// <summary>
    /// Composition root. Creates the persistent services before the first scene loads:
    /// input, performance monitoring and the overlay, settings and the UI event system.
    /// </summary>
    public sealed class GameServices : MonoBehaviour
    {
        public static GameServices Instance { get; private set; }

        public InputRouter Input { get; private set; }
        public PerformanceMonitor Perf { get; private set; }
        public PerfOverlay Overlay { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Boot()
        {
            if (Instance != null) return;
            var go = new GameObject("MASAR Services");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<GameServices>();
            Instance.Init();
        }

        private void Init()
        {
            // Physics rate: 120 Hz chassis, with 4 sub-steps inside the vehicle model (480 Hz tires/drivetrain).
            Time.fixedDeltaTime = 1f / 120f;
            Time.maximumDeltaTime = 1f / 10f;

            var s = SettingsStore.Current;
            GraphicsQuality.Apply(s);

            Input = gameObject.AddComponent<InputRouter>();
            Input.keyboardCalibration = s.keyboard;
            Input.gamepadCalibration = s.gamepad;
            Input.wheelCalibration = s.wheel;
            Input.wheelMapping = s.wheelMapping;

            Perf = gameObject.AddComponent<PerformanceMonitor>();
            Perf.RenderScaleProvider = GraphicsQuality.CurrentRenderScale;

            UIKit.EnsureEventSystem();
            Overlay = gameObject.AddComponent<PerfOverlay>();
            Overlay.Visible = s.showPerfOverlay;

            Debug.Log($"[MASAR] Boot: {SystemInfo.deviceModel} | {SystemInfo.graphicsDeviceName} ({SystemInfo.graphicsDeviceType}) | " +
                      $"quality {QualitySettings.names[QualitySettings.GetQualityLevel()]} | settings {SettingsStore.FilePath}");
        }

        private void Update()
        {
            if (Input.Actions.TogglePerfOverlay)
            {
                Overlay.Visible = !Overlay.Visible;
                SettingsStore.Current.showPerfOverlay = Overlay.Visible;
                SettingsStore.Save();
            }
        }
    }
}
