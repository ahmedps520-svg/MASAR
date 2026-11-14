using System;
using UnityEngine;

namespace Masar.Cameras
{
    public enum CameraMode
    {
        Cockpit = 0,
        Hood = 1,
        Chase = 2,
        Cinematic = 3,
        Free = 4,
        Passenger = 5
    }

    /// <summary>Player-adjustable settings for one camera mode. Saved per mode.</summary>
    [Serializable]
    public class CameraModeSettings
    {
        public CameraMode mode;
        [Range(30f, 100f)] public float fieldOfView = 60f;
        [Tooltip("Offset from the mode's anchor in vehicle space (m).")]
        public Vector3 offset;
        [Tooltip("Pitch tilt (degrees, positive looks down).")]
        public float pitch;
        [Tooltip("Chase only: distance behind the vehicle (m).")]
        public float followDistance = 5.6f;
        [Tooltip("Chase only: height above the vehicle origin (m).")]
        public float height = 1.75f;
        [Range(0f, 1f)] public float smoothing = 0.5f;
        [Range(0.1f, 3f)] public float lookSensitivity = 1f;
        [Range(0f, 1f)] public float shake = 0.3f;
        [Tooltip("Cockpit/passenger: how much the head moves with G-forces.")]
        [Range(0f, 1f)] public float headMotion = 0.5f;

        public static CameraModeSettings DefaultFor(CameraMode m)
        {
            switch (m)
            {
                case CameraMode.Cockpit: return new CameraModeSettings { mode = m, fieldOfView = 58f, shake = 0.25f, headMotion = 0.5f, smoothing = 0.2f };
                case CameraMode.Hood: return new CameraModeSettings { mode = m, fieldOfView = 62f, shake = 0.2f, smoothing = 0.15f };
                case CameraMode.Chase: return new CameraModeSettings { mode = m, fieldOfView = 60f, followDistance = 5.6f, height = 1.75f, pitch = 6f, smoothing = 0.55f, shake = 0.1f };
                case CameraMode.Cinematic: return new CameraModeSettings { mode = m, fieldOfView = 35f, smoothing = 0.7f, shake = 0f };
                case CameraMode.Free: return new CameraModeSettings { mode = m, fieldOfView = 60f, smoothing = 0.6f, lookSensitivity = 1f, shake = 0f };
                case CameraMode.Passenger: return new CameraModeSettings { mode = m, fieldOfView = 62f, shake = 0.25f, headMotion = 0.6f, smoothing = 0.2f };
                default: return new CameraModeSettings { mode = m };
            }
        }

        public CameraModeSettings Clone() => (CameraModeSettings)MemberwiseClone();
    }
}
