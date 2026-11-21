using System.Collections;
using Masar.Vehicles;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Masar.Game.UI
{
    /// <summary>Main menu, built in code. Every button here does something real.</summary>
    public sealed class MainMenu : MonoBehaviour
    {
        public string driveScene = "TestBlock";

        private Canvas _canvas;
        private RectTransform _menuColumn;
        private GameObject _settings;
        private GameObject _devices;
        private GameObject _cars;
        private Button _carButton;
        private Image _progressFill;
        private Text _progressText;

        private void Start()
        {
            Time.timeScale = 1f;
            _canvas = UIKit.CreateCanvas("MainMenu", 10);
            var root = _canvas.transform;

            // Backdrop: deep asphalt with a slightly lighter band behind the content
            var top = UIKit.Panel(root, "Backdrop", new Color(0.055f, 0.065f, 0.08f, 1f));
            UIKit.Anchor(top.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var band = UIKit.Panel(root, "Band", new Color(0.075f, 0.085f, 0.10f, 1f));
            UIKit.Anchor(band.rectTransform, new Vector2(0, 0.08f), new Vector2(1, 0.62f), Vector2.zero, Vector2.zero);

            // Title block on the right, clear of the performance overlay (top-left)
            var title = UIKit.Label(root, "MASAR", 150, UIKit.Text, TextAnchor.LowerLeft, FontStyle.Bold);
            UIKit.Anchor(title.rectTransform, new Vector2(0.42f, 0.74f), new Vector2(0.96f, 0.94f), Vector2.zero, Vector2.zero);
            var stripe = UIKit.KerbStripe(root, 8, 56f, 14f);
            stripe.anchorMin = stripe.anchorMax = new Vector2(0.423f, 0.735f);
            stripe.pivot = new Vector2(0f, 0.5f);
            stripe.anchoredPosition = Vector2.zero;
            var subtitle = UIKit.Label(root, "Jeddah urban driving simulator  ·  Phase 0 technical build", 32, UIKit.TextDim, TextAnchor.UpperLeft);
            UIKit.Anchor(subtitle.rectTransform, new Vector2(0.423f, 0.64f), new Vector2(0.98f, 0.715f), Vector2.zero, Vector2.zero);

            // Menu column
            _menuColumn = UIKit.Rect(root, "Menu");
            UIKit.Anchor(_menuColumn, new Vector2(0.06f, 0.06f), new Vector2(0.36f, 0.58f), Vector2.zero, Vector2.zero);
            UIKit.VStack(_menuColumn, 18, new RectOffset(0, 0, 0, 0), false);
            UIKit.Size(UIKit.Button(_menuColumn, "Free drive: Jeddah test block", OnDrive, 32, UIKit.Accent), 92);
            _carButton = UIKit.Button(_menuColumn, CarLabel(), ToggleCars, 30);
            UIKit.Size(_carButton, 80);
            UIKit.Size(UIKit.Button(_menuColumn, "Settings", ToggleSettings, 30), 80);
            UIKit.Size(UIKit.Button(_menuColumn, "Input devices & wheel test", ToggleDevices, 30), 80);
            DiagnosticsButton.Create(_menuColumn, "main-menu", 80f);
#if !UNITY_IOS
            UIKit.Size(UIKit.Button(_menuColumn, "Quit", Application.Quit, 30), 80);
#endif

            // Honest status card
            var card = UIKit.Panel(root, "StatusCard", UIKit.PanelColor);
            UIKit.Anchor(card.rectTransform, new Vector2(0.42f, 0.12f), new Vector2(0.94f, 0.58f), Vector2.zero, Vector2.zero);
            var status = UIKit.Label(card.transform,
                "<b>What this build is</b>\n" +
                "A feasibility build: selectable cars on one custom physics model, one small Jeddah-style street block, six cameras, input abstraction and live performance metrics.\n\n" +
                "<b>Placeholders</b>\n" +
                "Imported cars are third-party models for private testing only (licenses in the asset inventory); their handling values are class estimates. Procedural buildings. Traffic lights are visual props only. There is no traffic AI, pedestrians, audio, damage, driving school or Arabic UI yet.\n\n" +
                "<b>Logitech G923</b>\n" +
                "Desktop: read as a generic HID joystick (mapping unverified). iPad: Apple documents no public racing-wheel API on iPadOS. No force feedback is implemented.",
                24, UIKit.TextDim, TextAnchor.UpperLeft);
            status.supportRichText = true;
            UIKit.Anchor(status.rectTransform, Vector2.zero, Vector2.one, new Vector2(32, 28), new Vector2(-32, -28));

            // Loading bar (hidden)
            var bar = UIKit.Panel(root, "LoadingBar", new Color(1, 1, 1, 0.1f));
            UIKit.Anchor(bar.rectTransform, new Vector2(0.06f, 0.05f), new Vector2(0.94f, 0.065f), Vector2.zero, Vector2.zero);
            _progressFill = UIKit.Panel(bar.transform, "Fill", UIKit.Accent);
            UIKit.Anchor(_progressFill.rectTransform, Vector2.zero, new Vector2(0, 1), Vector2.zero, Vector2.zero);
            _progressText = UIKit.Label(root, "", 24, UIKit.TextDim, TextAnchor.LowerLeft);
            UIKit.Anchor(_progressText.rectTransform, new Vector2(0.06f, 0.07f), new Vector2(0.6f, 0.11f), Vector2.zero, Vector2.zero);
            bar.gameObject.SetActive(false);
        }

        private void OnDrive()
        {
            StartCoroutine(Load());
        }

        private IEnumerator Load()
        {
            _menuColumn.gameObject.SetActive(false);
            _progressFill.transform.parent.gameObject.SetActive(true);
            float t0 = Time.realtimeSinceStartup;
            var op = SceneManager.LoadSceneAsync(driveScene);
            while (!op.isDone)
            {
                float p = Mathf.Clamp01(op.progress / 0.9f);
                _progressFill.rectTransform.anchorMax = new Vector2(p, 1);
                _progressText.text = $"Loading Jeddah test block… {p * 100f:F0}%";
                yield return null;
            }
            Debug.Log($"[MASAR] Scene '{driveScene}' loaded in {Time.realtimeSinceStartup - t0:F2}s");
        }

        private static string CarLabel()
        {
            var e = CarCatalog.Instance != null ? CarCatalog.Instance.FindOrDefault(SettingsStore.Current.selectedCar) : null;
            return "Car: " + (e != null ? e.displayName : "Test sedan");
        }

        public void ToggleCars()
        {
            if (_settings) { Destroy(_settings); _settings = null; }
            if (_devices) { Destroy(_devices); _devices = null; }
            if (_cars) { Destroy(_cars); _cars = null; return; }
            _cars = CarSelectPanel.Create(_canvas.transform,
                e => { UIKit.ButtonLabel(_carButton).text = CarLabel(); Destroy(_cars); _cars = null; },
                () => { Destroy(_cars); _cars = null; }).gameObject;
        }

        public void ToggleSettings()
        {
            if (_cars) { Destroy(_cars); _cars = null; }
            if (_devices) { Destroy(_devices); _devices = null; }
            if (_settings) { Destroy(_settings); _settings = null; return; }
            _settings = SettingsPanel.Create(_canvas.transform, null, () => { Destroy(_settings); _settings = null; }).gameObject;
        }

        public void ToggleDevices()
        {
            if (_cars) { Destroy(_cars); _cars = null; }
            if (_settings) { Destroy(_settings); _settings = null; }
            if (_devices) { Destroy(_devices); _devices = null; return; }
            _devices = DevicePanel.Create(_canvas.transform, () => { Destroy(_devices); _devices = null; }).gameObject;
        }
    }
}
