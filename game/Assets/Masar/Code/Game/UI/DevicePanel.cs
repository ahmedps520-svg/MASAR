using System.Text;
using Masar.Input;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.UI;

namespace Masar.Game.UI
{
    /// <summary>
    /// Live list of every input device the engine sees, with raw axis and button values.
    /// This is the Phase 0 wheel test: it shows what the OS actually exposes, so the
    /// G923 pedal and button mapping can be confirmed with the hardware connected.
    /// </summary>
    public sealed class DevicePanel : MonoBehaviour
    {
        private Text _text;
        private readonly StringBuilder _sb = new StringBuilder(2048);
        private float _next;

        public static DevicePanel Create(Transform parent, System.Action onClose)
        {
            var bg = UIKit.Panel(parent, "DevicePanel", UIKit.PanelColor);
            UIKit.Anchor(bg.rectTransform, new Vector2(0.08f, 0.06f), new Vector2(0.92f, 0.94f), Vector2.zero, Vector2.zero);
            var panel = bg.gameObject.AddComponent<DevicePanel>();

            var title = UIKit.Label(bg.transform, "Input devices & wheel test", 38, UIKit.Text, TextAnchor.UpperLeft, FontStyle.Bold);
            UIKit.Anchor(title.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(36, -80), new Vector2(-36, -24));

            panel._text = UIKit.Label(bg.transform, "", 22, UIKit.Text, TextAnchor.UpperLeft);
            UIKit.Anchor(panel._text.rectTransform, Vector2.zero, Vector2.one, new Vector2(36, 110), new Vector2(-36, -96));

            var close = UIKit.Button(bg.transform, "Close", onClose, 28, UIKit.Accent);
            UIKit.Place((RectTransform)close.transform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-36, 28), new Vector2(240, 70));
            return panel;
        }

        private void Update()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 0.1f;
            var router = GameServices.Instance != null ? GameServices.Instance.Input : null;

            _sb.Clear();
            _sb.Append("Active driving input: ").Append(router != null ? router.ActiveKind.ToString() : "?").Append("\n\n");

            var devices = InputSystem.devices;
            if (devices.Count == 0) _sb.Append("No input devices reported by the engine.\n");
            foreach (var d in devices)
            {
                _sb.Append("- ").Append(d.displayName).Append("  [").Append(d.layout).Append("]  ")
                   .Append(d.description.interfaceName).Append(' ').Append(d.description.manufacturer).Append('\n');
                if (d is Joystick)
                {
                    int shown = 0;
                    foreach (var c in d.allControls)
                    {
                        if (c.synthetic) continue;
                        string rel = c.path.Length > d.path.Length ? c.path.Substring(d.path.Length + 1) : c.path;
                        // ButtonControl derives from AxisControl, so check buttons first.
                        if (c is ButtonControl b)
                        {
                            if (b.isPressed) _sb.Append("    ").Append(rel).Append(" PRESSED\n");
                        }
                        else if (c is AxisControl a)
                        {
                            _sb.Append("    ").Append(rel).Append(" = ").Append(a.ReadValue().ToString("+0.000;-0.000")).Append('\n');
                            shown++;
                        }
                        if (shown > 24) break;
                    }
                }
            }

            _sb.Append("\nForce feedback: NOT implemented in Phase 0. No FFB settings are shown until real force reaches the wheel.\n");
#if UNITY_IOS && !UNITY_EDITOR
            _sb.Append("iPadOS: wheels are only visible here if the GameController framework exposes them. Apple documents no racing-wheel API on iPadOS (see docs/research).\n");
#else
            _sb.Append("Desktop: wheels appear as Joystick devices. Turn the wheel and press each pedal to see which axis moves, then record it in WheelMapping.\n");
#endif
            _text.text = _sb.ToString();
        }
    }
}
