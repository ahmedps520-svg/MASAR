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
        /// <summary>Data credit shown on screen while driving (generated maps: OpenStreetMap, ODbL).</summary>
        public string mapAttribution = "";

        private Canvas _canvas;
        private DriveHud _hud;
        private TouchControls _touch;
        private GameObject _pause;
        private GameObject _subPanel;
        private bool _paused;
        private readonly List<Renderer> _renderers = new List<Renderer>();

        /// <summary>
        /// Swaps the scene's placeholder car for the selected one before anything else starts.
        /// The command line (-masarCar id) overrides the saved choice, for perf runs.
        /// </summary>
        private void Awake()
        {
            if (vehicle == null) vehicle = FindFirstObjectByType<VehicleController>();
            var catalog = CarCatalog.Instance;
            if (catalog == null || vehicle == null) return;
            string id = SettingsStore.Current.selectedCar;
            var args = System.Environment.GetCommandLineArgs();
            int ai = System.Array.IndexOf(args, "-masarCar");
            if (ai >= 0 && ai + 1 < args.Length) id = args[ai + 1];
            else if (System.Array.IndexOf(args, "-masarSmokeTest") >= 0) id = "test_sedan";   // deterministic perf baselines
            var entry = catalog.FindOrDefault(id);
            if (entry == null || (entry.prefab != null && entry.prefab.name == vehicle.gameObject.name)) return;
            var v = Replace(vehicle, entry, vehicle.transform.position, vehicle.transform.rotation);
            if (v == null) return;
            vehicle = v;
            if (cameraRig != null) cameraRig.target = vehicle;
            Debug.Log($"[MASAR] Car: {entry.displayName} ({entry.id})");
        }

        private static VehicleController Replace(VehicleController old, CarCatalog.Entry entry, Vector3 pos, Quaternion rot)
        {
            var prefab = entry.LoadPrefab();
            if (prefab == null) { Debug.LogError($"[MASAR] Car {entry.id} could not be loaded"); return null; }
            var go = Instantiate(prefab, pos, rot);
            go.name = prefab.name;
            old.gameObject.SetActive(false);
            Destroy(old.gameObject);
            // Free the previous car's meshes and textures (only the chosen car stays in memory)
            Resources.UnloadUnusedAssets();
            return go.GetComponent<VehicleController>();
        }

        /// <summary>Pause-menu car change: the new car appears where the old one is, at rest.</summary>
        public void SwitchCar(CarCatalog.Entry entry)
        {
            if (entry == null || !entry.IsAvailable) return;
            var t = vehicle.transform;
            var fwd = Vector3.ProjectOnPlane(t.forward, Vector3.up);
            var rot = Quaternion.LookRotation(fwd.sqrMagnitude > 1e-4f ? fwd : Vector3.forward, Vector3.up);
            var v = Replace(vehicle, entry, t.position + Vector3.up * 0.15f, rot);
            if (v == null) return;
            vehicle = v;
            var router = GameServices.Instance.Input;
            v.Input = router;
            if (v.Simulation != null) v.Simulation.Drivetrain.Mode = SettingsStore.Current.transmission;
            router.VehicleSteeringLockDeg = v.Definition.steering.steeringWheelLockDegrees;
            Masar.World.Traffic.TrafficSystem.Player = v.GetComponent<Rigidbody>();
            cameraRig.SetTarget(v);
            if (_hud) _hud.vehicle = v;
            GetComponentsInScene(_renderers);
            Debug.Log($"[MASAR] Switched car: {entry.displayName} ({entry.id})");
        }

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
            router.VehicleSteeringLockDeg = vehicle.Definition.steering.steeringWheelLockDegrees;
            Masar.World.Traffic.TrafficSystem.Player = vehicle.GetComponent<Rigidbody>();
            ApplyPopulation(s);
            var ffb = gameObject.AddComponent<WheelForceFeedback>();
            ffb.Vehicle = () => vehicle;
            ffb.Paused = () => _paused;

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

            if (!string.IsNullOrEmpty(mapAttribution))
            {
                var credit = UIKit.Label(_canvas.transform, mapAttribution, 18, new Color(1f, 1f, 1f, 0.55f), TextAnchor.LowerRight);
                UIKit.Anchor(credit.rectTransform, new Vector2(0.55f, 0f), new Vector2(0.995f, 0.03f), Vector2.zero, Vector2.zero);
                credit.raycastTarget = false;
            }
        }

        private static void ApplyPopulation(MasarSettings s)
        {
            var args = System.Environment.GetCommandLineArgs();
            int ti = System.Array.IndexOf(args, "-masarTraffic");
            Masar.World.Traffic.TrafficSystem.Density = ti >= 0 && ti + 1 < args.Length && int.TryParse(args[ti + 1], out var td) ? td : s.trafficDensity;
            int pi = System.Array.IndexOf(args, "-masarPedestrians");
            Masar.World.Traffic.PedestrianSystem.Density = pi >= 0 && pi + 1 < args.Length && int.TryParse(args[pi + 1], out var pd) ? pd : s.pedestrianDensity;
        }

        private void OnDestroy()
        {
            SettingsStore.Changed -= OnSettingsChanged;
            Masar.World.Traffic.TrafficSystem.Player = null;
            if (GameServices.Instance)
            {
                GameServices.Instance.Perf.VisibleRendererCounter = null;
                GameServices.Instance.Input.MenuOpen = false;
                GameServices.Instance.Input.FreeCameraCapture = false;
            }
            Time.timeScale = 1f;
        }

        private void OnSettingsChanged(MasarSettings s) { RefreshTouchControls(); ApplyPopulation(s); }

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
            var current = CarCatalog.Instance != null ? CarCatalog.Instance.FindOrDefault(SettingsStore.Current.selectedCar) : null;
            UIKit.Size(UIKit.Button(col, "Change car" + (current != null ? $" ({current.displayName})" : ""), OpenCars, 28), 76);
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

        private void OpenCars()
        {
            if (_subPanel) Destroy(_subPanel);
            if (_pause) _pause.SetActive(false);
            _subPanel = CarSelectPanel.Create(_canvas.transform, e =>
            {
                SwitchCar(e);
                SetPaused(false);
            }, CloseSubPanel).gameObject;
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
