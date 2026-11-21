using System;
using System.IO;
using Masar.Cameras;
using Masar.Input;
using Masar.Vehicles;
using UnityEngine;

namespace Masar.Game
{
    /// <summary>Everything the player can change. Serialized to JSON in persistentDataPath.</summary>
    [Serializable]
    public class MasarSettings
    {
        public int version = 1;
        public int qualityLevel = -1;                 // -1 = platform default
        public int targetFrameRate = 60;
        public bool showPerfOverlay = true;           // Phase 0: on by default (technical build)
        public bool showTouchControls = true;
        public TransmissionKind transmission = TransmissionKind.Automatic;
        public CameraMode cameraMode = CameraMode.Chase;
        public string selectedCar = "";               // CarCatalog entry id; empty = first car
        public string selectedMap = "Tahlia";         // drive scene: "Tahlia" (real map, OSM) or "TestBlock"
        public int trafficDensity = 2;                // 0 off, 1 light, 2 normal, 3 heavy
        public int pedestrianDensity = 2;             // 0 off, 1 few, 2 normal, 3 busy
        public CameraModeSettings[] cameras;
        public InputCalibration keyboard = new InputCalibration();
        public InputCalibration gamepad = new InputCalibration { steerDeadzone = 0.08f, steerExponent = 1.4f };
        public InputCalibration wheel = new InputCalibration { steerDeadzone = 0f };
        public WheelMapping wheelMapping = new WheelMapping();
    }

    public static class SettingsStore
    {
        private static MasarSettings s_Current;
        public static event Action<MasarSettings> Changed;

        public static string FilePath => Path.Combine(Application.persistentDataPath, "settings.json");

        public static MasarSettings Current
        {
            get
            {
                if (s_Current == null) Load();
                return s_Current;
            }
        }

        public static void Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    s_Current = JsonUtility.FromJson<MasarSettings>(File.ReadAllText(FilePath));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[MASAR] Settings could not be read, using defaults: {e.Message}");
                s_Current = null;
            }
            if (s_Current == null) s_Current = new MasarSettings();
            if (s_Current.cameras == null || s_Current.cameras.Length != Enum.GetValues(typeof(CameraMode)).Length)
            {
                var n = Enum.GetValues(typeof(CameraMode)).Length;
                s_Current.cameras = new CameraModeSettings[n];
                for (int i = 0; i < n; i++) s_Current.cameras[i] = CameraModeSettings.DefaultFor((CameraMode)i);
            }
        }

        public static void Save()
        {
            try
            {
                File.WriteAllText(FilePath, JsonUtility.ToJson(Current, true));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[MASAR] Settings could not be saved: {e.Message}");
            }
            Changed?.Invoke(Current);
        }

        public static void ResetCameras()
        {
            var n = Enum.GetValues(typeof(CameraMode)).Length;
            for (int i = 0; i < n; i++) Current.cameras[i] = CameraModeSettings.DefaultFor((CameraMode)i);
            Save();
        }
    }
}
