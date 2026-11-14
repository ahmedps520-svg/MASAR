using UnityEngine;

namespace Masar.Cameras
{
    /// <summary>Camera mount points on a vehicle prefab, placed by the vehicle builder.</summary>
    public sealed class VehicleCameraAnchors : MonoBehaviour
    {
        public Transform driverEye;
        public Transform passengerEye;
        public Transform hood;
        [Tooltip("Point the chase and cinematic cameras look at.")]
        public Transform lookTarget;
    }
}
