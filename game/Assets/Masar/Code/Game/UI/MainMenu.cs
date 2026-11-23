using System;
using System.Collections;
using Masar.Game.Diagnostics;
using Masar.Vehicles;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Masar.Game.UI
{
    /// <summary>
    /// Main menu, built in code over the showroom backdrop (MenuShowroom). Plays the launch intro
    /// once per launch. Every entry here does something real.
    /// </summary>
    public sealed class MainMenu : MonoBehaviour
    {
        public string driveScene = "TestBlock";
        [Tooltip("Rounded logo tile (Art/Brand/MasarLogoTile.png), set by the menu scene builder")]
        public Texture2D logo;

        private Canvas _canvas;
        private CanvasGroup _menuGroup;
        private RectTransform _list;
        private CanvasGroup[] _rowGroups;
        private Button _firstRow;
        private Text _garageSub, _carCaption;
        private GameObject _panel;
        private MenuShowroom _showroom;
        private bool _loading;

        private void Start()
        {
            Time.timeScale = 1f;
            UIKit.EnsureEventSystem();
            _showroom = FindFirstObjectByType<MenuShowroom>();
            _canvas = UIKit.CreateCanvas("MainMenu", 10);
            var root = _canvas.transform;
            _menuGroup = root.gameObject.AddComponent<CanvasGroup>();
            root.gameObject.AddComponent<UIKit.ModalToken>();   // a full-screen menu: the perf overlay stays hidden

            // Shading so text reads over the 3D backdrop: from the left edge, and along the bottom
            Shade(root, "LeftShade", new Vector2(0f, 0f), new Vector2(0.62f, 1f), true, 0.9f);
            Shade(root, "BottomShade", new Vector2(0f, 0f), new Vector2(1f, 0.22f), false, 0.8f);

            // Brand block, top left
            if (logo != null)
            {
                var img = UIKit.Rect(root, "Logo").gameObject.AddComponent<RawImage>();
                img.texture = logo;
                img.raycastTarget = false;
                UIKit.Place(img.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(84, -70), new Vector2(140, 140));
            }
            float bx = logo != null ? 252f : 84f;
            var word = UIKit.Label(root, "MASAR", 88, UIKit.Text, TextAnchor.UpperLeft, FontStyle.Bold);
            UIKit.Place(word.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(bx - 4f, -66), new Vector2(700, 100));
            var tag = UIKit.Label(root, "JEDDAH DRIVING SIMULATOR", 22, UIKit.TextDim, TextAnchor.UpperLeft, FontStyle.Bold);
            UIKit.Place(tag.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(bx, -172), new Vector2(700, 30));
            var chip = UIKit.Panel(root, "AlphaChip", UIKit.BrandRed);
            UIKit.Place(chip.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(bx + word.preferredWidth + 18f, -90), new Vector2(104, 36));
            var chipText = UIKit.Label(chip.transform, "ALPHA", 20, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.Anchor(chipText.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            // Menu list
            _list = UIKit.Rect(root, "Menu");
            UIKit.Place(_list, new Vector2(0, 1), new Vector2(0, 1), new Vector2(84, -290), new Vector2(680, 820));
            UIKit.VStack(_list, 10, new RectOffset(0, 0, 0, 0));
            _firstRow = Row(_list, "FREE DRIVE", "Tahlia Street · Al Rawdah · real Jeddah roads", () => OnDrive("Tahlia"), true).btn;
            Row(_list, "TEST BLOCK", "A small Jeddah-style block for testing", () => OnDrive("TestBlock"), false);
            _garageSub = Row(_list, "GARAGE", CarName(), ToggleCars, false).sub;
            Row(_list, "SETTINGS", "Graphics, driving, city and cameras", ToggleSettings, false);
            Row(_list, "CONTROLLERS", "Input devices and wheel test", ToggleDevices, false);
            Row(_list, "ABOUT THIS ALPHA", "What works, what doesn't yet, diagnostics", ToggleAbout, false);
#if !UNITY_IOS
            Row(_list, "QUIT", "Back to the desktop", Application.Quit, false);
#endif
            _rowGroups = _list.GetComponentsInChildren<CanvasGroup>();

            // Caption of the car on show, bottom right
            var onShow = UIKit.Label(root, "ON DISPLAY", 20, UIKit.TextDim, TextAnchor.LowerRight, FontStyle.Bold);
            UIKit.Place(onShow.rectTransform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-84, 150), new Vector2(900, 28));
            _carCaption = UIKit.Label(root, "", 46, UIKit.Text, TextAnchor.LowerRight, FontStyle.Bold);
            UIKit.Place(_carCaption.rectTransform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-84, 92), new Vector2(1100, 58));
            var line = UIKit.Panel(root, "CaptionLine", UIKit.Brand);
            UIKit.Place(line.rectTransform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-84, 80), new Vector2(150, 5));
            RefreshCar();

            // Footer
            var build = DiagnosticsReport.ReadBuildInfo().build;
            var footer = UIKit.Label(root, $"Alpha · build {build}      Map data © OpenStreetMap contributors (ODbL)", 18, UIKit.TextDim, TextAnchor.LowerLeft);
            UIKit.Place(footer.rectTransform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(84, 40), new Vector2(1200, 26));

            if (!IntroSequence.Played)
            {
                _menuGroup.alpha = 0f;
                IntroSequence.Play(logo, () => StartCoroutine(Enter()));
            }
            else StartCoroutine(Enter());
        }

        /// <summary>Menu entrance: the list slides in and its rows fade up one after another.</summary>
        private IEnumerator Enter()
        {
            _menuGroup.alpha = 1f;
            foreach (var g in _rowGroups) g.alpha = 0f;
            var end = _list.anchoredPosition;
            float t = 0f;
            while (t < 1f)
            {
                t += Time.unscaledDeltaTime;
                float e = 1f - Mathf.Pow(1f - Mathf.Clamp01(t / 0.5f), 3f);
                _list.anchoredPosition = end + new Vector2(-48f * (1f - e), 0f);
                for (int i = 0; i < _rowGroups.Length; i++) _rowGroups[i].alpha = Mathf.Clamp01((t - i * 0.06f) / 0.3f);
                yield return null;
            }
            _list.anchoredPosition = end;
            foreach (var g in _rowGroups) g.alpha = 1f;
            // Keyboard, gamepad and wheel navigation start on the first entry
            if (EventSystem.current != null && _panel == null) EventSystem.current.SetSelectedGameObject(_firstRow.gameObject);
        }

        /// <summary>A menu entry: bold title, one-line description, chevron, hover/selection highlight.</summary>
        private static (Button btn, Text sub) Row(Transform parent, string title, string sub, Action onClick, bool primary)
        {
            var bg = UIKit.Panel(parent, "Row_" + title, Color.white);
            UIKit.Size(bg, primary ? 132f : 92f);
            bg.gameObject.AddComponent<CanvasGroup>();
            var btn = bg.gameObject.AddComponent<Button>();
            btn.targetGraphic = bg;
            float a = primary ? 0.06f : 0f;
            var cb = btn.colors;
            cb.normalColor = new Color(1f, 1f, 1f, a);
            cb.highlightedColor = new Color(1f, 1f, 1f, a + 0.06f);
            cb.selectedColor = new Color(1f, 1f, 1f, a + 0.05f);
            cb.pressedColor = new Color(1f, 1f, 1f, a + 0.14f);
            cb.disabledColor = new Color(1f, 1f, 1f, 0f);
            cb.fadeDuration = 0.08f;
            btn.colors = cb;
            btn.onClick.AddListener(() => onClick());

            var bar = UIKit.Panel(bg.transform, "Bar", primary ? UIKit.Brand : new Color(1f, 1f, 1f, 0.16f));
            UIKit.Anchor(bar.rectTransform, Vector2.zero, new Vector2(0f, 1f), Vector2.zero, new Vector2(primary ? 8f : 3f, 0f));
            var t = UIKit.Label(bg.transform, title, primary ? 52 : 36, UIKit.Text, TextAnchor.LowerLeft, FontStyle.Bold);
            UIKit.Anchor(t.rectTransform, new Vector2(0f, 0.44f), Vector2.one, new Vector2(34f, 0f), new Vector2(-70f, -6f));
            var s = UIKit.Label(bg.transform, sub, primary ? 24 : 21, primary ? UIKit.Brand : UIKit.TextDim, TextAnchor.UpperLeft);
            s.horizontalOverflow = HorizontalWrapMode.Overflow;
            UIKit.Anchor(s.rectTransform, Vector2.zero, new Vector2(1f, 0.44f), new Vector2(34f, 6f), new Vector2(-70f, -4f));
            var chev = UIKit.Label(bg.transform, "›", primary ? 76 : 54, primary ? UIKit.Brand : UIKit.TextDim, TextAnchor.MiddleRight, FontStyle.Bold);
            UIKit.Anchor(chev.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, new Vector2(-24f, 6f));
            return (btn, s);
        }

        /// <summary>A black gradient over part of the screen, fading out to the right (or upward).</summary>
        private static void Shade(Transform parent, string name, Vector2 min, Vector2 max, bool horizontal, float alpha)
        {
            const int n = 64;
            var tex = new Texture2D(horizontal ? n : 1, horizontal ? 1 : n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int i = 0; i < n; i++)
            {
                float u = i / (n - 1f);
                float k = alpha * (1f - Mathf.SmoothStep(0f, 1f, u));
                if (horizontal) tex.SetPixel(i, 0, new Color(0f, 0f, 0f, k)); else tex.SetPixel(0, i, new Color(0f, 0f, 0f, k));
            }
            tex.Apply();
            var img = UIKit.Rect(parent, name).gameObject.AddComponent<RawImage>();
            img.texture = tex;
            img.raycastTarget = false;
            UIKit.Anchor(img.rectTransform, min, max, Vector2.zero, Vector2.zero);
        }

        private string CarName()
        {
            var e = CarCatalog.Instance != null ? CarCatalog.Instance.FindOrDefault(SettingsStore.Current.selectedCar) : null;
            return e != null ? e.displayName : "Test sedan";
        }

        private void RefreshCar()
        {
            if (_showroom != null) _showroom.ShowSelectedCar();
            string name = CarName();
            _garageSub.text = name;
            _carCaption.text = name.ToUpperInvariant();
        }

        // ------------------------------------------------------------------ drive

        /// <summary>Loads a drive scene behind the loading screen (the menu's drive entries; UI capture).</summary>
        public void OnDrive(string scene)
        {
            if (_loading) return;
            _loading = true;
            driveScene = Application.CanStreamedLevelBeLoaded(scene) ? scene : "TestBlock";
            SettingsStore.Current.selectedMap = driveScene;
            SettingsStore.Save();
            Load();
        }

        private void Load()
        {
            bool city = driveScene == "Tahlia";
            var canvas = UIKit.CreateCanvas("Loading", 150);
            DontDestroyOnLoad(canvas.gameObject);
            canvas.gameObject.AddComponent<UIKit.ModalToken>();
            var group = canvas.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;   // faded in by LoadingRun
            var root = canvas.transform;
            var black = UIKit.Panel(root, "Black", new Color(0.03f, 0.035f, 0.045f, 1f));
            UIKit.Anchor(black.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            if (logo != null)
            {
                var img = UIKit.Rect(root, "Logo").gameObject.AddComponent<RawImage>();
                img.texture = logo;
                UIKit.Place(img.rectTransform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-84, 150), new Vector2(120, 120));
            }
            var kicker = UIKit.Label(root, "LOADING", 22, UIKit.Brand, TextAnchor.LowerLeft, FontStyle.Bold);
            UIKit.Place(kicker.rectTransform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(84, 386), new Vector2(1400, 30));
            var title = UIKit.Label(root, city ? "TAHLIA · AL RAWDAH" : "TEST BLOCK", 84, UIKit.Text, TextAnchor.LowerLeft, FontStyle.Bold);
            UIKit.Place(title.rectTransform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(80, 262), new Vector2(1500, 110));
            var sub = UIKit.Label(root, city
                    ? "Jeddah  ·  4.4 × 2.6 km of real OpenStreetMap roads, with the flyovers and tunnels in the map"
                    : "A small Jeddah-style street block for testing the car and the cameras",
                26, UIKit.TextDim, TextAnchor.UpperLeft);
            UIKit.Place(sub.rectTransform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(84, 216), new Vector2(1500, 40));
            var bar = UIKit.Panel(root, "Bar", new Color(1f, 1f, 1f, 0.1f));
            UIKit.Anchor(bar.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(84f, 120f), new Vector2(-260f, 126f));
            var fill = UIKit.Panel(bar.transform, "Fill", UIKit.Brand);
            UIKit.Anchor(fill.rectTransform, Vector2.zero, new Vector2(0f, 1f), Vector2.zero, Vector2.zero);
            var pct = UIKit.Label(root, "0%", 22, UIKit.TextDim, TextAnchor.LowerLeft, FontStyle.Bold);
            UIKit.Place(pct.rectTransform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(84, 74), new Vector2(1200, 30));

            // The loading screen runs the load itself: this menu is destroyed when the drive scene activates
            var run = canvas.gameObject.AddComponent<LoadingRun>();
            run.scene = driveScene; run.group = group; run.fill = fill; run.pct = pct;
            run.from = city ? 0.4f : 1f;   // the city: the map scene is the first 40% of the bar, its building blocks the rest
        }

        /// <summary>
        /// Runs a drive-scene load behind the loading screen (a DontDestroyOnLoad canvas): fades in, loads the
        /// scene, then keeps the screen up with the game paused until the city's building blocks around the
        /// car are loaded (so the drive starts with the street in place, not boxes turning into buildings),
        /// then fades out and destroys itself. Scenes without streamed blocks fade out at once.
        /// </summary>
        private sealed class LoadingRun : MonoBehaviour
        {
            public string scene;
            public CanvasGroup group;
            public Image fill;
            public Text pct;
            public float from;
            private float _t0, _shown;
            private const float Timeout = 120f;

            private IEnumerator Start()
            {
                for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / 0.3f) { group.alpha = t; yield return null; }
                group.alpha = 1f;
                _t0 = Time.realtimeSinceStartup;
                Application.backgroundLoadingPriority = ThreadPriority.High;
                var op = SceneManager.LoadSceneAsync(scene);
                while (!op.isDone)
                {
                    Show(Mathf.Clamp01(op.progress / 0.9f) * from, null);
                    yield return null;
                }
                Debug.Log($"[MASAR] Scene '{scene}' loaded in {Time.realtimeSinceStartup - _t0:F2}s");
                yield return null;   // the scene's Start (DriveSession) runs first
                if (FindAnyObjectByType<Masar.World.CityLodManager>() != null)
                {
                    Time.timeScale = 0f;
                    while (!Masar.World.CityLodManager.Settled && Time.realtimeSinceStartup - _t0 < Timeout)
                    {
                        int needed = Masar.World.CityLodManager.Needed, missing = Masar.World.CityLodManager.Missing;
                        float target = from + (1f - from) * (needed == 0 ? 0f : (needed - missing) / (float)needed);
                        Show(Mathf.MoveTowards(_shown, target, Time.unscaledDeltaTime * 1.5f),
                            needed > 0 ? $"city blocks {needed - missing} of {needed}" : null);
                        yield return null;
                    }
                    if (!Masar.World.CityLodManager.Settled) Debug.LogWarning("[MASAR] City blocks still loading after 120 s: starting anyway");
                }
                Done();
                for (float a = 1f; a > 0f; a -= Time.unscaledDeltaTime / 0.4f) { group.alpha = a; yield return null; }
                Destroy(gameObject);
            }

            private void Show(float p, string detail)
            {
                _shown = Mathf.Max(_shown, p);
                fill.rectTransform.anchorMax = new Vector2(_shown, 1f);
                pct.text = detail != null ? $"{_shown * 100f:F0}%   ·   {detail}" : $"{_shown * 100f:F0}%";
            }

            private void Done()
            {
                Show(1f, null);
                Time.timeScale = 1f;
                Application.backgroundLoadingPriority = ThreadPriority.BelowNormal;
                Debug.Log($"[MASAR] Ready to drive {Time.realtimeSinceStartup - _t0:F2}s after the load started");
            }
        }

        // ------------------------------------------------------------------ panels

        private void Open(Func<Action, GameObject> create)
        {
            if (_panel != null) { Destroy(_panel); _panel = null; }
            _panel = create(Close);
        }

        private void Close()
        {
            if (_panel != null) Destroy(_panel);
            _panel = null;
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(_firstRow.gameObject);
        }

        private void Toggle<T>(Func<Action, GameObject> create) where T : Component
        {
            bool same = _panel != null && _panel.GetComponent<T>() != null;
            if (same) Close(); else Open(create);
        }

        public void ToggleCars() => Toggle<CarSelectPanel>(close => CarSelectPanel.Create(_canvas.transform,
            e => { RefreshCar(); close(); }, close).gameObject);

        public void ToggleSettings() => Toggle<SettingsPanel>(close => SettingsPanel.Create(_canvas.transform, null, close).gameObject);

        public void ToggleDevices() => Toggle<DevicePanel>(close => DevicePanel.Create(_canvas.transform, close).gameObject);

        public void ToggleAbout() => Toggle<AboutPanel>(close => AboutPanel.Create(_canvas.transform, close).gameObject);
    }
}
