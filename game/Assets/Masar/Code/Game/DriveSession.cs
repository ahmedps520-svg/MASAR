using System.Collections.Generic;
using Masar.Cameras;
using Masar.Game.UI;
using Masar.Vehicles;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Masar.Game
{
    /// <summary>
    /// Free-drive session in a driving scene. It wires the vehicle, camera rig, HUD,
    /// touch controls and pause menu to the persistent services.
    /// </summary>
    public sealed class DriveSession : MonoBehaviour
    {
        public VehicleController vehicle;
        public CameraRig cameraRig;
        public Transform spawnPoint;
        public string menuScene = "MainMenu";

        private Canvas _canvas;
        private DriveHud _hud;
        private TouchControls _touch;
        private GameObject _pause;
        private GameObject _subPanel;
        private bool _paused;
        private readonly List<Renderer> _renderers = new List<Renderer>();

        private void Start()
        {
            var services = GameServices.Instance;
            var router = services.Input;
            var s = SettingsStore.Current;

            if (vehicle == null) vehicle = FindFirstObjectByType<VehicleController>();
            if (cameraRig == null) cameraRig = FindFirstObjectByType<CameraRig>();
            if (spawnPoint != null) vehicle.Teleport(spawnPoint.position, spawnPoint.rotation);

            vehicle.Input = router;
            vehicle.Simulation.Drivetrain.Mode = s.transmission;

            cameraRig.Input = router;
            cameraRig.settings = s.cameras;
            cameraRig.EnsureSettings();
            cameraRig.SetTarget(vehicle);
            cameraRig.SetMode(s.cameraMode);
            cameraRig.FreeCameraChanged += free => router.FreeCameraCapture = free;
            router.FreeCameraCapture = cameraRig.mode == CameraMode.Free;
            cameraRig.ModeChanged += m =>
            {
                s.cameraMode = m;
                SettingsStore.Save();
                if (_hud) _hud.ShowCameraName(CameraName(m));
            };

            _canvas = UIKit.CreateCanvas("DriveUI", 50);
            _hud = DriveHud.Create(_canvas.transform, vehicle);
            RefreshTouchControls();
            SettingsStore.Changed += OnSettingsChanged;

            // Count visible renderers for the perf overlay (real Renderer.isVisible, sampled twice a second)
            GetComponentsInScene(_renderers);
            services.Perf.VisibleRendererCounter = CountVisible;

            _hud.ShowCameraName(CameraName(cameraRig.mode));
        }

        private void OnDestroy()
        {
            SettingsStore.Changed -= OnSettingsChanged;
            if (GameServices.Instance)
            {
                GameServices.Instance.Perf.VisibleRendererCounter = null;
                GameServices.Instance.Input.MenuOpen = false;
                GameServices.Instance.Input.FreeCameraCapture = false;
            }
            Time.timeScale = 1f;
        }

        private void OnSettingsChanged(MasarSettings s) => RefreshTouchControls();

        /// <summary>QA only: show the touch controls even without a touchscreen (UI capture on desktop).</summary>
        public void ForceTouchControls(bool on)
        {
            _forceTouch = on;
            RefreshTouchControls();
        }

        private bool _forceTouch;

        private void RefreshTouchControls()
        {
            bool want = (_forceTouch || (SettingsStore.Current.showTouchControls && Touchscreen.current != null)) && !_paused;
            if (want && _touch == null) _touch = TouchControls.Create(_canvas.transform);
            else if (!want && _touch != null) { Destroy(_touch.gameObject); _touch = null; }
            if (_hud) _hud.SetTouchLayout(_touch != null);
        }

        private void Update()
        {
            var router = GameServices.Instance.Input;
            if (router.Actions.Pause) SetPaused(!_paused);
        }

        // ------------------------------------------------------------------ pause menu

        public void SetPaused(bool paused)
        {
            _paused = paused;
            Time.timeScale = paused ? 0f : 1f;
            GameServices.Instance.Input.MenuOpen = paused;
            if (_subPanel) { Destroy(_subPanel); _subPanel = null; }
            if (paused) BuildPauseMenu();
            else if (_pause) { Destroy(_pause); _pause = null; }
            RefreshTouchControls();
        }

        private void BuildPauseMenu()
        {
            var bg = UIKit.Panel(_canvas.transform, "Pause", new Color(0, 0, 0, 0.62f));
            UIKit.Anchor(bg.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            bg.gameObject.AddComponent<UIKit.ModalToken>();
            _pause = bg.gameObject;

            var col = UIKit.Rect(bg.transform, "Column");
            UIKit.Anchor(col, new Vector2(0.36f, 0.16f), new Vector2(0.64f, 0.84f), Vector2.zero, Vector2.zero);
            UIKit.VStack(col, 16, new RectOffset(0, 0, 0, 0), false);
            var title = UIKit.Label(col, "Paused", 54, UIKit.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.Size(title, 90);
            UIKit.Size(UIKit.Button(col, "Resume", () => SetPaused(false), 30, UIKit.Accent), 84);
            UIKit.Size(UIKit.Button(col, $"Settings (camera: {CameraName(cameraRig.mode)})", OpenSettings, 28), 76);
            UIKit.Size(UIKit.Button(col, "Input devices & wheel test", OpenDevices, 28), 76);
            DiagnosticsButton.Create(col, "pause-menu", 76f);
            UIKit.Size(UIKit.Button(col, "Reset vehicle to start", () =>
            {
                if (spawnPoint) vehicle.Teleport(spawnPoint.position, spawnPoint.rotation); else vehicle.ResetUpright();
                SetPaused(false);
            }, 28), 76);
            UIKit.Size(UIKit.Button(col, "Main menu", () => SceneManager.LoadScene(menuScene), 28), 76);

            var help = UIKit.Label(bg.transform,
                "Keyboard: W/S throttle-brake · A/D steer · Space handbrake · E/Q gear lever (auto) or shift (manual) · M auto/manual · " +
                "C/V camera · L lights · Backspace reset · I ignition · F3 overlay · Esc pause · Free camera: WASD/arrows + PgUp/PgDn, hold right mouse to look\n" +
                "Gamepad: RT/LT throttle-brake · left stick steer · A handbrake · RB/LB gear · Y camera · X lights · View reset · Menu pause · right stick look",
                20, UIKit.TextDim, TextAnchor.LowerCenter);
            UIKit.Anchor(help.rectTransform, new Vector2(0.05f, 0.02f), new Vector2(0.95f, 0.14f), Vector2.zero, Vector2.zero);
        }

        public void OpenSettings()
        {
            if (_subPanel) Destroy(_subPanel);
            if (_pause) _pause.SetActive(false);   // one menu on screen at a time
            _subPanel = SettingsPanel.Create(_canvas.transform, cameraRig, CloseSubPanel,
                t => vehicle.Simulation.Drivetrain.Mode = t).gameObject;
        }

        private void OpenDevices()
        {
            if (_subPanel) Destroy(_subPanel);
            if (_pause) _pause.SetActive(false);
            _subPanel = DevicePanel.Create(_canvas.transform, CloseSubPanel).gameObject;
        }

        private void CloseSubPanel()
        {
            if (_subPanel) Destroy(_subPanel);
            _subPanel = null;
            if (_pause) _pause.SetActive(true);
        }

        // ------------------------------------------------------------------ helpers

        public static string CameraName(CameraMode m)
        {
            switch (m)
            {
                case CameraMode.Cockpit: return "Cockpit";
                case CameraMode.Hood: return "Hood";
                case CameraMode.Chase: return "Chase";
                case CameraMode.Cinematic: return "Cinematic";
                case CameraMode.Free: return "Free camera";
                case CameraMode.Passenger: return "Passenger seat";
                default: return m.ToString();
            }
        }

        private static void GetComponentsInScene(List<Renderer> list)
        {
            list.Clear();
            list.AddRange(FindObjectsByType<Renderer>(FindObjectsSortMode.None));
        }

        private int CountVisible()
        {
            int n = 0;
            for (int i = 0; i < _renderers.Count; i++)
                if (_renderers[i] != null && _renderers[i].isVisible) n++;
            return n;
        }
    }
}
