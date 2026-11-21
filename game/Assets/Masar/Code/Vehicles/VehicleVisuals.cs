using UnityEngine;

namespace Masar.Vehicles
{
    /// <summary>
    /// Drives the visible parts of a vehicle from simulation state: wheel position,
    /// steer and spin, the steering wheel, and the brake, reverse and head lights.
    /// No physics here.
    /// </summary>
    [RequireComponent(typeof(VehicleController))]
    public sealed class VehicleVisuals : MonoBehaviour
    {
        [Tooltip("Wheel visual pivots in order FL, FR, RL, RR. Each pivot must be at the wheel center, rolling about local X.")]
        public Transform[] wheels = new Transform[4];
        [Tooltip("Optional parts that follow the wheel and steer with it but don't spin (brake calipers), FL, FR, RL, RR.")]
        public Transform[] wheelCarriers = new Transform[0];
        [Tooltip("Steering wheel pivot, rotating about its local Z (column axis).")]
        public Transform steeringWheel;
        public Renderer[] brakeLightRenderers;
        public Renderer[] reverseLightRenderers;
        public Light[] headlights;
        public Renderer[] headlightRenderers;
        [ColorUsage(false, true)] public Color brakeEmission = new Color(4f, 0.08f, 0.05f);
        [ColorUsage(false, true)] public Color tailEmission = new Color(0.9f, 0.02f, 0.01f);
        [ColorUsage(false, true)] public Color reverseEmission = new Color(3f, 3f, 3f);
        [ColorUsage(false, true)] public Color headlightEmission = new Color(6f, 6f, 5.5f);

        public bool HeadlightsOn { get; private set; }

        private VehicleController _vehicle;
        private MaterialPropertyBlock _mpb;
        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

        private void Awake()
        {
            _vehicle = GetComponent<VehicleController>();
            _mpb = new MaterialPropertyBlock();
            SetHeadlights(false);
        }

        public void SetHeadlights(bool on)
        {
            HeadlightsOn = on;
            if (headlights != null) foreach (var l in headlights) if (l) l.enabled = on;
            SetEmission(headlightRenderers, on ? headlightEmission : Color.black);
        }

        private void Update()
        {
            if (_vehicle.Input != null && _vehicle.Input.Actions.ToggleHeadlights) SetHeadlights(!HeadlightsOn);
        }

        private void LateUpdate()
        {
            var sim = _vehicle.Simulation;
            if (sim == null) return;

            for (int i = 0; i < 4 && i < wheels.Length; i++)
            {
                var w = wheels[i];
                if (w == null) continue;
                var state = sim.Wheels[i];
                var susp = sim.Suspension(i);
                Vector3 mount = sim.MountPoint(i);
                float ext = susp.restLength - Mathf.Clamp(state.Compression, 0f, susp.travel + 0.05f);
                w.localPosition = mount - Vector3.up * ext;
                w.localRotation = Quaternion.Euler(0f, state.SteerAngle, 0f) * Quaternion.Euler(state.SpinAngle, 0f, 0f);
                if (wheelCarriers != null && i < wheelCarriers.Length && wheelCarriers[i] != null)
                {
                    wheelCarriers[i].localPosition = w.localPosition;
                    wheelCarriers[i].localRotation = Quaternion.Euler(0f, state.SteerAngle, 0f);
                }
            }

            if (steeringWheel != null)
            {
                float angle = sim.Telemetry.SteeringWheelAngleDeg;
                steeringWheel.localRotation = Quaternion.Euler(0f, 0f, -angle);
            }

            var c = _vehicle.LastControls;
            bool braking = c.Brake > 0.05f;
            Color tail = HeadlightsOn ? tailEmission : Color.black;
            SetEmission(brakeLightRenderers, braking ? brakeEmission : tail);
            bool reversing = sim.Drivetrain.Gear < 0;
            SetEmission(reverseLightRenderers, reversing ? reverseEmission : Color.black);
        }

        private void SetEmission(Renderer[] renderers, Color c)
        {
            if (renderers == null) return;
            foreach (var r in renderers)
            {
                if (r == null) continue;
                r.GetPropertyBlock(_mpb);
                _mpb.SetColor(EmissionColor, c);
                r.SetPropertyBlock(_mpb);
            }
        }
    }
}
