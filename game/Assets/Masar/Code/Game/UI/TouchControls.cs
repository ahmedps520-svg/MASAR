using Masar.Input;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Masar.Game.UI
{
    /// <summary>
    /// On-screen driving controls for the iPad when no controller is connected.
    /// Left half: drag to steer. Right side: analog throttle and brake pedals
    /// (pressing higher on a pedal gives more input). Buttons: gear lever, handbrake, camera, lights, reset, pause.
    /// </summary>
    public sealed class TouchControls : MonoBehaviour
    {
        public static TouchControls Create(Transform parent)
        {
            var root = UIKit.Rect(parent, "TouchControls");
            UIKit.Anchor(root, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var tc = root.gameObject.AddComponent<TouchControls>();

            // Steering zone
            var steerZone = UIKit.Panel(root, "SteerZone", new Color(1, 1, 1, 0.0f));
            UIKit.Anchor(steerZone.rectTransform, new Vector2(0, 0), new Vector2(0.45f, 0.62f), Vector2.zero, Vector2.zero);
            var knobTrack = UIKit.Panel(steerZone.transform, "Track", new Color(1, 1, 1, 0.10f));
            UIKit.Place(knobTrack.rectTransform, new Vector2(0.5f, 0.22f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(560, 14));
            var knob = UIKit.Panel(steerZone.transform, "Knob", new Color(1, 1, 1, 0.35f));
            UIKit.Place(knob.rectTransform, new Vector2(0.5f, 0.22f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(90, 90));
            var label = UIKit.Label(steerZone.transform, "drag to steer", 22, new Color(1, 1, 1, 0.45f), TextAnchor.MiddleCenter);
            UIKit.Place(label.rectTransform, new Vector2(0.5f, 0.22f), new Vector2(0.5f, 0.5f), new Vector2(0, 70), new Vector2(400, 40));
            var steer = steerZone.gameObject.AddComponent<SteerZone>();
            steer.knob = knob.rectTransform;

            // Pedals
            var brake = MakePedal(root, "BRAKE", new Vector2(1, 0), new Vector2(-330, 40), new Color(0.85f, 0.25f, 0.2f, 0.38f), v => TouchInputState.Brake = v);
            var gas = MakePedal(root, "GAS", new Vector2(1, 0), new Vector2(-40, 40), new Color(0.1f, 0.75f, 0.45f, 0.38f), v => TouchInputState.Throttle = v);
            _ = brake; _ = gas;

            // Buttons
            MakeButton(root, "GEAR +", new Vector2(1, 0), new Vector2(-640, 230), () => TouchInputState.RequestShiftUp = true);
            MakeButton(root, "GEAR -", new Vector2(1, 0), new Vector2(-640, 120), () => TouchInputState.RequestShiftDown = true);
            var hb = MakeButton(root, "HANDBRAKE", new Vector2(1, 0), new Vector2(-640, 10), null, 150, 96);
            var hold = hb.gameObject.AddComponent<HoldButton>();
            hold.onChange = v => TouchInputState.Handbrake = v ? 1f : 0f;
            MakeButton(root, "CAM", new Vector2(1, 1), new Vector2(-230, -30), () => TouchInputState.RequestNextCamera = true, 120, 80);
            MakeButton(root, "II", new Vector2(1, 1), new Vector2(-90, -30), () => TouchInputState.RequestPause = true, 110, 80);
            MakeButton(root, "LIGHTS", new Vector2(1, 1), new Vector2(-370, -30), () => TouchInputState.RequestHeadlights = true, 130, 80);
            MakeButton(root, "RESET", new Vector2(1, 1), new Vector2(-520, -30), () => TouchInputState.RequestReset = true, 130, 80);

            // Look zone (top middle): drag to look around in interior cameras / orbit the chase camera
            var lookZone = UIKit.Panel(root, "LookZone", new Color(1, 1, 1, 0f));
            UIKit.Anchor(lookZone.rectTransform, new Vector2(0.2f, 0.65f), new Vector2(0.62f, 0.95f), Vector2.zero, Vector2.zero);
            lookZone.gameObject.AddComponent<LookZone>();
            return tc;
        }

        private static Pedal MakePedal(Transform root, string text, Vector2 anchor, Vector2 pos, Color color, System.Action<float> write)
        {
            var img = UIKit.Panel(root, text, color);
            UIKit.Place(img.rectTransform, anchor, new Vector2(1, 0), pos, new Vector2(260, 420));
            var l = UIKit.Label(img.transform, text, 30, new Color(1, 1, 1, 0.85f), TextAnchor.LowerCenter, FontStyle.Bold);
            UIKit.Anchor(l.rectTransform, Vector2.zero, Vector2.one, new Vector2(0, 18), Vector2.zero);
            var fill = UIKit.Panel(img.transform, "Fill", new Color(1, 1, 1, 0.25f));
            UIKit.Anchor(fill.rectTransform, Vector2.zero, new Vector2(1, 0), Vector2.zero, Vector2.zero);
            var p = img.gameObject.AddComponent<Pedal>();
            p.write = write;
            p.fill = fill.rectTransform;
            return p;
        }

        private static Button MakeButton(Transform root, string text, Vector2 anchor, Vector2 pos, System.Action onClick, float w = 150, float h = 96)
        {
            var b = UIKit.Button(root, text, onClick, 24, new Color(0.1f, 0.1f, 0.12f, 0.55f));
            UIKit.Place((RectTransform)b.transform, anchor, new Vector2(1, anchor.y), pos, new Vector2(w, h));
            b.gameObject.AddComponent<TouchMarker>();
            return b;
        }

        private void OnDisable() => TouchInputState.ClearContinuous();

        // ------------------------------------------------------------------ widgets

        private sealed class TouchMarker : MonoBehaviour, IPointerDownHandler
        {
            public void OnPointerDown(PointerEventData e) => TouchInputState.MarkActive();
        }

        private sealed class SteerZone : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
        {
            public RectTransform knob;
            private Vector2 _down;
            private int _pointer = int.MinValue;
            private const float Range = 260f; // canvas units for full lock

            public void OnPointerDown(PointerEventData e)
            {
                if (_pointer != int.MinValue) return;
                _pointer = e.pointerId;
                RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)transform, e.position, e.pressEventCamera, out _down);
                TouchInputState.MarkActive();
            }

            public void OnDrag(PointerEventData e)
            {
                if (e.pointerId != _pointer) return;
                RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)transform, e.position, e.pressEventCamera, out var p);
                float s = Mathf.Clamp((p.x - _down.x) / Range, -1f, 1f);
                TouchInputState.Steer = s;
                if (knob) knob.anchoredPosition = new Vector2(s * Range, 0f);
                TouchInputState.MarkActive();
            }

            public void OnPointerUp(PointerEventData e)
            {
                if (e.pointerId != _pointer) return;
                _pointer = int.MinValue;
                TouchInputState.Steer = 0f;
                if (knob) knob.anchoredPosition = Vector2.zero;
            }
        }

        private sealed class Pedal : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
        {
            public System.Action<float> write;
            public RectTransform fill;
            private int _pointer = int.MinValue;

            private void Set(PointerEventData e)
            {
                var rt = (RectTransform)transform;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, e.position, e.pressEventCamera, out var p);
                float h = Mathf.Clamp01((p.y - rt.rect.yMin) / Mathf.Max(1f, rt.rect.height));
                float v = Mathf.Clamp01(0.3f + h * 0.85f);
                write(v);
                if (fill) fill.anchorMax = new Vector2(1, v);
                TouchInputState.MarkActive();
            }

            public void OnPointerDown(PointerEventData e) { if (_pointer != int.MinValue) return; _pointer = e.pointerId; Set(e); }
            public void OnDrag(PointerEventData e) { if (e.pointerId == _pointer) Set(e); }
            public void OnPointerUp(PointerEventData e)
            {
                if (e.pointerId != _pointer) return;
                _pointer = int.MinValue;
                write(0f);
                if (fill) fill.anchorMax = new Vector2(1, 0);
            }
        }

        private sealed class HoldButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
        {
            public System.Action<bool> onChange;
            public void OnPointerDown(PointerEventData e) { onChange?.Invoke(true); TouchInputState.MarkActive(); }
            public void OnPointerUp(PointerEventData e) => onChange?.Invoke(false);
        }

        private sealed class LookZone : MonoBehaviour, IDragHandler
        {
            public void OnDrag(PointerEventData e)
            {
                TouchInputState.LookDelta += e.delta * 0.15f;
                TouchInputState.MarkActive();
            }
        }
    }
}
