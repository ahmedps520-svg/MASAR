using Masar.Game.Diagnostics;
using UnityEngine;
using UnityEngine.UI;

namespace Masar.Game.UI
{
    /// <summary>"Send diagnostics" button plus a status line. One tap, no forms.</summary>
    public sealed class DiagnosticsButton : MonoBehaviour
    {
        private Text _status;
        private Text _label;
        private DiagnosticsUploader _up;

        public static void Create(Transform column, string trigger, float height = 76f)
        {
            var holder = UIKit.Rect(column, "DiagnosticsButton");
            UIKit.VStack(holder, 6, new RectOffset(0, 0, 0, 0));
            UIKit.Size(holder, height + 38f);
            var comp = holder.gameObject.AddComponent<DiagnosticsButton>();
            comp._up = DiagnosticsUploader.Ensure();
            var btn = UIKit.Button(holder, "Send diagnostics", () => comp._up.Send(trigger), 28, new Color(0.10f, 0.26f, 0.42f, 0.98f));
            UIKit.Size(btn, height);
            comp._label = UIKit.ButtonLabel(btn);
            comp._status = UIKit.Label(holder, comp._up.LastStatus, 20, UIKit.TextDim, TextAnchor.MiddleCenter);
            UIKit.Size(comp._status, 32f);
            comp._up.StatusChanged += comp.OnStatus;
        }

        private void OnStatus(string s)
        {
            if (_status) _status.text = s;
            if (_label) _label.text = _up.Busy ? "Sending…" : "Send diagnostics";
        }

        private void OnDestroy()
        {
            if (_up != null) _up.StatusChanged -= OnStatus;
        }
    }
}
