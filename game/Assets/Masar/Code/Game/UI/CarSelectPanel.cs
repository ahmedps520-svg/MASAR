using System;
using Masar.Vehicles;
using UnityEngine;
using UnityEngine.UI;

namespace Masar.Game.UI
{
    /// <summary>
    /// Car selector: a grid of cards (rendered thumbnail, name, class and license caveats).
    /// Tapping a card saves the choice and calls back; the caller decides whether to swap the
    /// car now (pause menu) or on the next drive (main menu).
    /// </summary>
    public sealed class CarSelectPanel : MonoBehaviour
    {
        public static CarSelectPanel Create(Transform parent, Action<CarCatalog.Entry> onChosen, Action onClose)
        {
            var (root, box) = UIKit.Modal(parent, "CarSelectPanel", new Vector2(0.05f, 0.05f), new Vector2(0.95f, 0.95f));
            var panel = root.gameObject.AddComponent<CarSelectPanel>();

            var title = UIKit.Label(box.transform, "Choose your car", 44, UIKit.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIKit.Anchor(title.rectTransform, new Vector2(0, 1), new Vector2(0.7f, 1), new Vector2(40, -96), new Vector2(0, -24));
            var close = UIKit.Button(box.transform, "Close", () => onClose?.Invoke(), 28);
            UIKit.Place((RectTransform)close.transform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-36, -26), new Vector2(200, 66));

            var catalog = CarCatalog.Instance;
            if (catalog == null || catalog.cars == null || catalog.cars.Length == 0)
            {
                var none = UIKit.Label(box.transform, "No car catalog in this build. Run the project setup to generate it.", 28, UIKit.Warn, TextAnchor.MiddleCenter);
                UIKit.Anchor(none.rectTransform, Vector2.zero, Vector2.one, new Vector2(40, 40), new Vector2(-40, -120));
                return panel;
            }

            UIKit.ScrollColumn(box.transform, "Scroll", out var content);
            var scrollRt = (RectTransform)content.parent.parent;
            UIKit.Anchor(scrollRt, Vector2.zero, Vector2.one, new Vector2(40, 120), new Vector2(-40, -116));

            // License credits (CC BY requires them wherever the model is shown)
            var credits = new System.Text.StringBuilder("Model credits: ");
            bool first = true;
            foreach (var e in catalog.cars)
            {
                if (e == null || !e.IsAvailable || string.IsNullOrEmpty(e.credit) || e.id == "test_sedan") continue;
                if (!first) credits.Append("  ·  ");
                credits.Append(e.credit).Append(" [").Append(e.license).Append(']');
                first = false;
            }
            if (!first)
            {
                var footer = UIKit.Label(box.transform, credits.ToString(), 16, UIKit.TextDim, TextAnchor.UpperLeft);
                UIKit.Anchor(footer.rectTransform, new Vector2(0, 0), new Vector2(1, 0), new Vector2(40, 12), new Vector2(-40, 112));
            }
            var grid = content.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(524, 476);
            grid.spacing = new Vector2(24, 24);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 3;
            grid.childAlignment = TextAnchor.UpperCenter;
            grid.padding = new RectOffset(0, 0, 4, 24);

            string current = catalog.FindOrDefault(SettingsStore.Current.selectedCar)?.id;
            foreach (var e in catalog.cars)
            {
                if (e == null || !e.IsAvailable) continue;
                var entry = e;
                bool selected = entry.id == current;
                var card = UIKit.Panel(content, "Card_" + entry.id, selected ? UIKit.AccentDim : UIKit.ButtonColor);
                var btn = card.gameObject.AddComponent<Button>();
                btn.onClick.AddListener(() =>
                {
                    SettingsStore.Current.selectedCar = entry.id;
                    SettingsStore.Save();
                    onChosen?.Invoke(entry);
                });

                var thumbBg = UIKit.Panel(card.transform, "ThumbBg", new Color(0, 0, 0, 0.35f));
                UIKit.Anchor(thumbBg.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(10, -322), new Vector2(-10, -10));
                thumbBg.raycastTarget = false;
                if (entry.thumbnail != null)
                {
                    var raw = new GameObject("Thumb", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
                    raw.transform.SetParent(thumbBg.transform, false);
                    raw.texture = entry.thumbnail;
                    raw.raycastTarget = false;
                    UIKit.Anchor(raw.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                    var fit = raw.gameObject.AddComponent<AspectRatioFitter>();
                    fit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
                    fit.aspectRatio = (float)entry.thumbnail.width / Mathf.Max(1, entry.thumbnail.height);
                    thumbBg.gameObject.AddComponent<RectMask2D>();
                }

                var name = UIKit.Label(card.transform, entry.displayName, 30, UIKit.Text, TextAnchor.UpperLeft, FontStyle.Bold);
                UIKit.Anchor(name.rectTransform, new Vector2(0, 0), new Vector2(1, 0), new Vector2(18, 98), new Vector2(-18, 148));
                string detail = entry.summary;
                if (!string.IsNullOrEmpty(entry.warning)) detail += $"\n<color=#FFB833>{entry.warning}</color>";
                var info = UIKit.Label(card.transform, detail, 21, UIKit.TextDim, TextAnchor.UpperLeft);
                info.supportRichText = true;
                UIKit.Anchor(info.rectTransform, new Vector2(0, 0), new Vector2(1, 0), new Vector2(18, 8), new Vector2(-18, 96));
                if (selected)
                {
                    var tag = UIKit.Panel(card.transform, "SelectedTag", UIKit.Accent);
                    UIKit.Place(tag.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-18, -18), new Vector2(150, 44));
                    tag.raycastTarget = false;
                    var t = UIKit.Label(tag.transform, "SELECTED", 20, UIKit.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
                    UIKit.Anchor(t.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                }
            }
            return panel;
        }
    }
}
