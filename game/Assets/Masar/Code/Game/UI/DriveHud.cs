using Masar.Core;
using Masar.Vehicles;
using UnityEngine;
using UnityEngine.UI;

namespace Masar.Game.UI
{
    /// <summary>Minimal driving HUD (bottom right). It stays out of the driver's view.</summary>
    public sealed class DriveHud : MonoBehaviour
    {
        public VehicleController vehicle;
        private Text _speed, _gear, _info;
        private Image _rpmFill;
        private Text _camera;
        private float _cameraMsgUntil;

        public static DriveHud Create(Transform parent, VehicleController v)
        {
            var root = UIKit.Rect(parent, "HUD");
            UIKit.Anchor(root, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var hud = root.gameObject.AddComponent<DriveHud>();
            hud.vehicle = v;

            var card = UIKit.Panel(root, "Cluster", new Color(0, 0, 0, 0.42f));
            UIKit.Place(card.rectTransform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-28, 28), new Vector2(360, 190));

            hud._speed = UIKit.Label(card.transform, "0", 92, UIKit.Text, TextAnchor.MiddleRight, FontStyle.Bold);
            UIKit.Anchor(hud._speed.rectTransform, new Vector2(0, 0.35f), new Vector2(0.72f, 1f), Vector2.zero, new Vector2(0, -6));
            var unit = UIKit.Label(card.transform, "km/h", 24, UIKit.TextDim, TextAnchor.UpperLeft);
            UIKit.Anchor(unit.rectTransform, new Vector2(0.74f, 0.55f), new Vector2(1f, 0.8f), Vector2.zero, Vector2.zero);
            hud._gear = UIKit.Label(card.transform, "P", 64, UIKit.Accent, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.Anchor(hud._gear.rectTransform, new Vector2(0.74f, 0.32f), new Vector2(1f, 0.62f), Vector2.zero, Vector2.zero);

            var rpmBg = UIKit.Panel(card.transform, "RpmBg", new Color(1, 1, 1, 0.12f));
            UIKit.Anchor(rpmBg.rectTransform, new Vector2(0.06f, 0.24f), new Vector2(0.94f, 0.30f), Vector2.zero, Vector2.zero);
            hud._rpmFill = UIKit.Panel(rpmBg.transform, "RpmFill", UIKit.Accent);
            UIKit.Anchor(hud._rpmFill.rectTransform, Vector2.zero, new Vector2(0, 1), Vector2.zero, Vector2.zero);

            hud._info = UIKit.Label(card.transform, "", 20, UIKit.TextDim, TextAnchor.MiddleLeft);
            UIKit.Anchor(hud._info.rectTransform, new Vector2(0.06f, 0.02f), new Vector2(0.98f, 0.22f), Vector2.zero, Vector2.zero);

            hud._camera = UIKit.Label(root, "", 30, UIKit.Text, TextAnchor.UpperCenter, FontStyle.Bold);
            UIKit.Anchor(hud._camera.rectTransform, new Vector2(0.3f, 0.86f), new Vector2(0.7f, 0.95f), Vector2.zero, Vector2.zero);
            return hud;
        }

        public void ShowCameraName(string name)
        {
            _camera.text = name;
            _cameraMsgUntil = Time.unscaledTime + 1.6f;
        }

        private void Update()
        {
            if (vehicle == null || vehicle.Simulation == null) return;
            var t = vehicle.Telemetry;
            _speed.text = Mathf.RoundToInt(Mathf.Abs(t.ForwardSpeedMs) * 3.6f).ToString();

            string gear;
            if (t.TransmissionMode == TransmissionKind.Automatic)
            {
                switch (t.Selector)
                {
                    case GearSelector.Park: gear = "P"; break;
                    case GearSelector.Reverse: gear = "R"; break;
                    case GearSelector.Neutral: gear = "N"; break;
                    default: gear = "D" + Mathf.Max(1, t.Gear); break;
                }
            }
            else gear = t.Gear < 0 ? "R" : t.Gear == 0 ? "N" : t.Gear.ToString();
            _gear.text = gear;

            var def = vehicle.Definition;
            float frac = def != null ? Mathf.Clamp01(t.EngineRpm / def.engine.revLimiterRpm) : 0f;
            _rpmFill.rectTransform.anchorMax = new Vector2(frac, 1f);
            _rpmFill.color = def != null && t.EngineRpm > def.engine.redlineRpm ? new Color(0.9f, 0.2f, 0.15f) : UIKit.Accent;

            var router = GameServices.Instance ? GameServices.Instance.Input : null;
            string src = router != null ? router.ActiveKind.ToString() : "-";
            _info.text = $"{(t.TransmissionMode == TransmissionKind.Automatic ? "AUTO" : "MANUAL")}  {t.EngineRpm:F0} rpm" +
                         $"{(t.AbsActive ? "  ABS" : "")}{(t.EngineRunning ? "" : "  STALLED (I)")}  ·  {src}";

            if (_camera.text.Length > 0 && Time.unscaledTime > _cameraMsgUntil) _camera.text = "";
        }
    }
}
