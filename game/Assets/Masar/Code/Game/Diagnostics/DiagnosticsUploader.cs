using System;
using System.Collections;
using System.IO;
using System.Text;
using Masar.Diagnostics;
using UnityEngine;
using UnityEngine.Networking;

namespace Masar.Game.Diagnostics
{
    /// <summary>
    /// One tap: build the report, save a local copy (Files app → MASAR → diagnostics), and
    /// insert it into the ForgeBase diagnostics table. Status is reported truthfully:
    /// "not configured", an HTTP error, or sent.
    /// </summary>
    public sealed class DiagnosticsUploader : MonoBehaviour
    {
        public static DiagnosticsUploader Instance { get; private set; }
        public bool Busy { get; private set; }
        public string LastStatus { get; private set; } = "";
        public event Action<string> StatusChanged;

        public static DiagnosticsUploader Ensure()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("MASAR DiagnosticsUploader");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<DiagnosticsUploader>();
            return Instance;
        }

        public void Send(string trigger)
        {
            if (Busy) return;
            StartCoroutine(SendRoutine(trigger));
        }

        private void SetStatus(string s)
        {
            LastStatus = s;
            Debug.Log("[MASAR diagnostics] " + s);
            StatusChanged?.Invoke(s);
        }

        private IEnumerator SendRoutine(string trigger)
        {
            Busy = true;
            SetStatus("Collecting…");
            yield return null;

            string report;
            try { report = DiagnosticsReport.Build(trigger); }
            catch (Exception e) { SetStatus("Could not build the report: " + e.Message); Busy = false; yield break; }

            // Always keep a local copy first, so nothing is lost if the network fails
            string localPath = null;
            try
            {
                string dir = Path.Combine(Application.persistentDataPath, "diagnostics");
                Directory.CreateDirectory(dir);
                localPath = Path.Combine(dir, $"masar_diag_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json");
                File.WriteAllText(localPath, report, Encoding.UTF8);
            }
            catch (Exception e) { Debug.LogWarning("[MASAR diagnostics] local save failed: " + e.Message); }

            if (!DiagnosticsEndpoint.Configured)
            {
                SetStatus("Saved on this device. Upload isn't configured yet.");
                Busy = false;
                yield break;
            }

            var bi = DiagnosticsReport.ReadBuildInfo();
            var row = new JsonWriter().BeginObject()
                .Prop("app_version", Application.version).Prop("build", bi.build)
                .Prop("device_model", SystemInfo.deviceModel).Prop("platform", Application.platform.ToString())
                .Prop("trigger", trigger).Prop("payload", report)
                .EndObject().ToString();

            SetStatus($"Sending ({row.Length / 1024} KB)…");
            using (var req = new UnityWebRequest(DiagnosticsEndpoint.InsertUrl, "POST"))
            {
                req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(row));
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");
                req.SetRequestHeader("apikey", DiagnosticsEndpoint.AnonKey);
                req.SetRequestHeader("Authorization", "Bearer " + DiagnosticsEndpoint.AnonKey);
                req.SetRequestHeader("Prefer", "return=minimal");
                req.timeout = 30;
                yield return req.SendWebRequest();

                if (req.result == UnityWebRequest.Result.Success)
                    SetStatus("Sent. Thank you!");
                else
                {
                    string body = req.downloadHandler != null ? req.downloadHandler.text : "";
                    if (body != null && body.Length > 160) body = body.Substring(0, 160);
                    SetStatus($"Send failed ({req.responseCode} {req.error}). Saved on the device. {body}");
                }
            }
            Busy = false;
        }
    }
}
