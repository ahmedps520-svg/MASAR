using System;
using UnityEngine;
using UnityEngine.UI;

namespace Masar.Game.UI
{
    /// <summary>"About this alpha": an honest list of what works and what isn't real yet, plus diagnostics.</summary>
    public sealed class AboutPanel : MonoBehaviour
    {
        public static AboutPanel Create(Transform parent, Action onClose)
        {
            var (root, box) = UIKit.Modal(parent, "AboutPanel", new Vector2(0.2f, 0.07f), new Vector2(0.8f, 0.93f));
            var panel = root.gameObject.AddComponent<AboutPanel>();

            var title = UIKit.Label(box.transform, "About this alpha", 40, UIKit.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIKit.Anchor(title.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(40, -88), new Vector2(-40, -24));

            UIKit.ScrollColumn(box.transform, "Scroll", out var content);
            var scrollRt = (RectTransform)content.parent.parent;
            UIKit.Anchor(scrollRt, Vector2.zero, Vector2.one, new Vector2(40, 116), new Vector2(-40, -100));
            UIKit.VStack(content, 14, new RectOffset(0, 12, 0, 16));

            Section(content, "What this build is");
            Body(content, "A Month 2 alpha. Tahlia Street and Al Rawdah (4.4 × 2.6 km) built from real OpenStreetMap roads at real scale, " +
                          "with flyovers, ramps and tunnels where the map has them. AI traffic obeys the signals and makes U-turns; " +
                          "pedestrians walk the sidewalks. Selectable cars, six cameras and live performance metrics.");
            Section(content, "Not real yet");
            Body(content, "Only some buildings are surveyed in OpenStreetMap; the rest are generated to fill the plots. " +
                          "Bridge heights and ramp slopes use standard engineering values, not survey data. " +
                          "Traffic doesn't change lanes. No damage, sound, driving school or Arabic interface yet. " +
                          "Imported cars are for private testing only.");
            Section(content, "Logitech G923");
            Body(content, "On the iPad through the wheel bridge (Windows PC on the same Wi-Fi), with force feedback.");
            Section(content, "Credits");
            Body(content, "Map data © OpenStreetMap contributors (ODbL). Textures and sky: Poly Haven and ambientCG (CC0). Characters: Mixamo.");
            Section(content, "Diagnostics");
            DiagnosticsButton.Create(content, "about", 72f);

            var close = UIKit.Button(box.transform, "Close", onClose, 28, UIKit.Accent);
            UIKit.Place((RectTransform)close.transform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-40, 26), new Vector2(240, 70));
            return panel;
        }

        private static void Section(Transform parent, string text)
        {
            var t = UIKit.Label(parent, text.ToUpperInvariant(), 24, UIKit.Accent, TextAnchor.LowerLeft, FontStyle.Bold);
            UIKit.Size(t, 44);
        }

        private static void Body(Transform parent, string text)
        {
            var t = UIKit.Label(parent, text, 24, UIKit.TextDim, TextAnchor.UpperLeft);
            t.lineSpacing = 1.1f;
        }
    }
}
