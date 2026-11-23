using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Masar.Game.UI
{
    /// <summary>
    /// Minimal code-built uGUI kit: no prefabs or binary UI assets, so all UI lives in source control as text.
    /// Phase 0 uses the built-in Latin font only. Arabic shaping and RTL are a
    /// Month 2 item (see KNOWN_ISSUES.md).
    /// </summary>
    public static class UIKit
    {
        public static readonly Color PanelColor = new Color(0.055f, 0.062f, 0.072f, 1f);
        public static readonly Color Scrim = new Color(0f, 0f, 0f, 0.78f);

        /// <summary>Number of modal panels/menus open. The perf overlay and HUD hide while it is above zero.</summary>
        public static int ModalCount;

        /// <summary>
        /// Full-screen dimmed backdrop that blocks input to what's behind it, with a centered
        /// opaque box inside. Returns (root, box). Destroy the root to close.
        /// </summary>
        public static (RectTransform root, Image box) Modal(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax)
        {
            var scrim = Panel(parent, name, Scrim);
            Anchor(scrim.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            scrim.gameObject.AddComponent<ModalToken>();
            var box = Panel(scrim.transform, "Box", PanelColor);
            Anchor(box.rectTransform, anchorMin, anchorMax, Vector2.zero, Vector2.zero);
            return (scrim.rectTransform, box);
        }

        /// <summary>Counts an open modal for as long as its GameObject lives.</summary>
        public sealed class ModalToken : MonoBehaviour
        {
            private void OnEnable() => ModalCount++;
            private void OnDisable() => ModalCount = Mathf.Max(0, ModalCount - 1);
        }

        /// <summary>The kerb-stripe motif (alternating black and yellow), as used on Jeddah medians.</summary>
        public static RectTransform KerbStripe(Transform parent, int segments, float segW, float h)
        {
            var row = Rect(parent, "KerbStripe");
            row.sizeDelta = new Vector2(segments * segW, h);
            for (int i = 0; i < segments; i++)
            {
                var s = Panel(row, "Seg", i % 2 == 0 ? new Color(0.89f, 0.66f, 0.07f) : new Color(0.06f, 0.06f, 0.07f));
                var rt = s.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0, 0.5f);
                rt.pivot = new Vector2(0, 0.5f);
                rt.anchoredPosition = new Vector2(i * segW, 0);
                rt.sizeDelta = new Vector2(segW, h);
            }
            return row;
        }
        public static readonly Color PanelLight = new Color(0.10f, 0.11f, 0.13f, 0.92f);
        // Brand colors, taken from the logo: lane-paint amber and stop-sign red
        public static readonly Color Brand = new Color(0.996f, 0.737f, 0.353f, 1f);
        public static readonly Color BrandRed = new Color(0.94f, 0.20f, 0.20f, 1f);
        public static readonly Color Accent = Brand;
        public static readonly Color AccentDim = new Color(0.42f, 0.29f, 0.10f, 1f);
        public static readonly Color TextOnLight = new Color(0.07f, 0.07f, 0.08f, 1f);

        /// <summary>Label color that reads on a background: dark on light (amber), light on dark.</summary>
        public static Color OnColor(Color bg) => 0.2126f * bg.r + 0.7152f * bg.g + 0.0722f * bg.b > 0.5f ? TextOnLight : Text;

        /// <summary>Recolors a button and keeps its label readable.</summary>
        public static void Paint(Button b, Color c)
        {
            b.GetComponent<Image>().color = c;
            var label = ButtonLabel(b);
            if (label != null) label.color = OnColor(c);
        }
        public static readonly Color ButtonColor = new Color(0.16f, 0.17f, 0.20f, 0.95f);
        public static readonly Color Text = new Color(0.94f, 0.95f, 0.96f, 1f);
        public static readonly Color TextDim = new Color(0.66f, 0.69f, 0.73f, 1f);
        public static readonly Color Warn = new Color(1f, 0.72f, 0.2f, 1f);

        private static Font s_Font;
        public static Font Font => s_Font != null ? s_Font : (s_Font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));

        public static void EnsureEventSystem()
        {
            if (EventSystem.current != null) return;
            var go = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            UnityEngine.Object.DontDestroyOnLoad(go);
        }

        public static Canvas CreateCanvas(string name, int sortingOrder, Transform parent = null)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            if (parent != null) go.transform.SetParent(parent, false);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1320f);   // ≈ iPad 11" aspect
            scaler.matchWidthOrHeight = 0.5f;
            return canvas;
        }

        public static RectTransform Rect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        public static Image Panel(Transform parent, string name, Color color)
        {
            var rt = Rect(parent, name);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            return img;
        }

        public static void Anchor(RectTransform rt, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax)
        {
            rt.anchorMin = min; rt.anchorMax = max; rt.offsetMin = offsetMin; rt.offsetMax = offsetMax;
        }

        public static void Place(RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = anchor; rt.pivot = pivot; rt.anchoredPosition = pos; rt.sizeDelta = size;
        }

        public static Text Label(Transform parent, string text, int size, Color color, TextAnchor align = TextAnchor.MiddleLeft, FontStyle style = FontStyle.Normal)
        {
            var rt = Rect(parent, "Text");
            var t = rt.gameObject.AddComponent<Text>();
            t.font = Font;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.fontStyle = style;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }

        public static VerticalLayoutGroup VStack(RectTransform rt, float spacing, RectOffset padding, bool controlHeight = true)
        {
            var v = rt.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = spacing;
            v.padding = padding;
            v.childControlWidth = true;
            v.childControlHeight = controlHeight;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            return v;
        }

        public static HorizontalLayoutGroup HStack(RectTransform rt, float spacing)
        {
            var h = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = spacing;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = true;
            h.childForceExpandHeight = true;
            return h;
        }

        public static LayoutElement Size(Component c, float preferredHeight, float preferredWidth = -1f)
        {
            var le = c.gameObject.GetComponent<LayoutElement>();
            if (le == null) le = c.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = preferredHeight;
            le.minHeight = preferredHeight;
            if (preferredWidth > 0f) { le.preferredWidth = preferredWidth; le.minWidth = preferredWidth; }
            return le;
        }

        public static Button Button(Transform parent, string text, Action onClick, int fontSize = 30, Color? color = null)
        {
            var img = Panel(parent, "Button", color ?? ButtonColor);
            var btn = img.gameObject.AddComponent<Button>();
            var colors = btn.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f);
            colors.pressedColor = new Color(0.75f, 0.75f, 0.75f);
            colors.selectedColor = Color.white;
            btn.colors = colors;
            var label = Label(img.transform, text, fontSize, OnColor(img.color), TextAnchor.MiddleCenter, FontStyle.Bold);
            Anchor(label.rectTransform, Vector2.zero, Vector2.one, new Vector2(12, 4), new Vector2(-12, -4));
            if (onClick != null) btn.onClick.AddListener(() => onClick());
            return btn;
        }

        public static Text ButtonLabel(Button b) => b.GetComponentInChildren<Text>();

        /// <summary>A labeled slider row: "Label ............ value" with a slider underneath.</summary>
        public static Slider SliderRow(Transform parent, string label, float min, float max, float value, Func<float, string> format, Action<float> onChange)
        {
            var row = Rect(parent, "SliderRow");
            VStack(row, 4, new RectOffset(0, 0, 0, 0));
            Size(row, 74);

            var header = Rect(row, "Header");
            Size(header, 30);
            var title = Label(header, label, 24, TextDim);
            Anchor(title.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var valueText = Label(header, format(value), 24, Text, TextAnchor.MiddleRight, FontStyle.Bold);
            Anchor(valueText.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            var res = new DefaultControls.Resources();
            var sliderGo = DefaultControls.CreateSlider(res);
            sliderGo.transform.SetParent(row, false);
            Size(sliderGo.GetComponent<RectTransform>(), 34);
            var slider = sliderGo.GetComponent<Slider>();
            foreach (var img in sliderGo.GetComponentsInChildren<Image>())
            {
                if (img.gameObject.name == "Background") img.color = new Color(1f, 1f, 1f, 0.12f);
                else if (img.gameObject.name == "Fill") img.color = Accent;
                else if (img.gameObject.name == "Handle") img.color = Text;
            }
            var handle = slider.handleRect;
            if (handle != null) handle.sizeDelta = new Vector2(28f, 0f);
            slider.minValue = min;
            slider.maxValue = max;
            slider.SetValueWithoutNotify(value);
            slider.onValueChanged.AddListener(v => { valueText.text = format(v); onChange(v); });
            return slider;
        }

        /// <summary>A row of mutually exclusive option buttons. Returns a refresh action.</summary>
        public static Action Segmented(Transform parent, string label, string[] options, Func<int> getIndex, Action<int> setIndex)
        {
            var row = Rect(parent, "Segmented");
            VStack(row, 6, new RectOffset(0, 0, 0, 0));
            Size(row, 98);
            var title = Label(row, label, 24, TextDim);
            Size(title, 30);
            var bar = Rect(row, "Options");
            Size(bar, 56);
            HStack(bar, 6);
            var buttons = new Button[options.Length];
            Action refresh = null;
            for (int i = 0; i < options.Length; i++)
            {
                int idx = i;
                buttons[i] = Button(bar, options[i], () => { setIndex(idx); refresh?.Invoke(); }, 24);
            }
            refresh = () =>
            {
                int cur = getIndex();
                for (int i = 0; i < buttons.Length; i++)
                    Paint(buttons[i], i == cur ? Accent : ButtonColor);
            };
            refresh();
            return refresh;
        }

        public static ScrollRect ScrollColumn(Transform parent, string name, out RectTransform content)
        {
            var root = Panel(parent, name, new Color(0, 0, 0, 0));
            var scroll = root.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 40f;
            var viewport = Panel(root.transform, "Viewport", new Color(0, 0, 0, 0.001f));
            viewport.gameObject.AddComponent<RectMask2D>();
            Anchor(viewport.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            content = Rect(viewport.transform, "Content");
            content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(1, 1); content.pivot = new Vector2(0.5f, 1);
            content.offsetMin = Vector2.zero; content.offsetMax = Vector2.zero;
            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = viewport.rectTransform;
            scroll.content = content;
            return scroll;
        }
    }
}
