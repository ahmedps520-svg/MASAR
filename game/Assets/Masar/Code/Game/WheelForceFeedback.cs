using System;
using Masar.Input;
using Masar.Vehicles;
using UnityEngine;

namespace Masar.Game
{
    /// <summary>
    /// Force feedback for a bridged wheel, computed from the simulation every frame:
    ///  - the front tires' self-aligning torque (pneumatic trail × lateral force), which
    ///    centers the wheel in a turn and goes light as the tires reach the limit of grip;
    ///  - tire scrub at parking speed (heavy damping that fades out with speed);
    ///  - the car's steering lock, so the rim stops where the real steering stops (soft lock,
    ///    applied by the bridge at 250 Hz).
    /// Nothing is sent while paused, in a menu, or with no wheel bridge connected.
    /// </summary>
    public sealed class WheelForceFeedback : MonoBehaviour
    {
        [Tooltip("Self-aligning torque at the front axle (N·m) that maps to full wheel force.")]
        public float fullScaleTorque = 350f;
        public float strength = 1f;

        public Func<VehicleController> Vehicle;
        public Func<bool> Paused;
        private float _smoothed;

        private void Update()
        {
            if (!NetworkWheel.Connected) return;
            var v = Vehicle?.Invoke();
            bool paused = Paused != null && Paused();
            if (v == null || v.Simulation == null || v.Body == null || paused)
            {
                NetworkWheel.SetForce(0f, 0f, 0.05f, 450f, false);
                return;
            }
            var t = v.Telemetry;
            float speed = v.Body.linearVelocity.magnitude;
            float target = Mathf.Clamp(t.SelfAligningTorque / fullScaleTorque, -1f, 1f);
            if (t.GroundedWheels == 0) target = 0f;
            // Low-pass at about 25 Hz: the physics runs in substeps and the raw torque is grainy
            _smoothed = Mathf.Lerp(_smoothed, target, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 25f * 2f * Mathf.PI / 6f));
            float damper = Mathf.Lerp(0.5f, 0.1f, Mathf.Clamp01(speed / 8f));   // parking-speed scrub
            float lockDeg = v.Definition != null ? v.Definition.steering.steeringWheelLockDegrees : 450f;
            NetworkWheel.SetForce(_smoothed * strength, 0f, damper * strength, lockDeg, true);
        }

        private void OnDisable() => NetworkWheel.SetForce(0f, 0f, 0f, 450f, false);
    }
}
