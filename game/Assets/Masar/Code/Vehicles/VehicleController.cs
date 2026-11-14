using Masar.Core;
using UnityEngine;

namespace Masar.Vehicles
{
    /// <summary>
    /// Thin Unity adapter around <see cref="VehicleSimulation"/>. It casts the wheels,
    /// feeds the contacts to the simulation and applies the resulting forces to the
    /// Rigidbody. All vehicle behavior lives in the simulation and the definition.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [DefaultExecutionOrder(-50)]
    public sealed class VehicleController : MonoBehaviour
    {
        [SerializeField] private VehicleDefinition definition;
        [Tooltip("Sub-steps per physics step for wheel, tire and drivetrain integration.")]
        [SerializeField, Range(1, 8)] private int substeps = 4;
        [Tooltip("Height above the contact patch where tire forces are applied (m). 0 = physically at the patch.")]
        [SerializeField] private float tireForceHeight = 0.08f;

        public VehicleDefinition Definition => definition;
        public VehicleSimulation Simulation { get; private set; }
        public Rigidbody Body { get; private set; }
        public IDriverInputSource Input { get; set; }
        public VehicleTelemetry Telemetry => Simulation != null ? Simulation.Telemetry : default;
        public DriverControls LastControls { get; private set; }

        /// <summary>Per-wheel contact point in world space (valid when grounded), for debug and effects.</summary>
        public Vector3[] ContactPoints { get; } = new Vector3[4];

        private readonly WheelContact[] _contacts = new WheelContact[4];
        private readonly WheelOutput[] _outputs = new WheelOutput[4];
        private readonly float[] _prevCompression = new float[4];
        private readonly RaycastHit[] _hits = new RaycastHit[8];
        private Vector3 _prevVelocity;
        private Vector3 _localAccel;

        public void SetDefinition(VehicleDefinition def)
        {
            definition = def;
            if (isActiveAndEnabled) Initialize();
        }

        private void Awake()
        {
            Body = GetComponent<Rigidbody>();
            Initialize();
        }

        private void Initialize()
        {
            if (definition == null) definition = VehicleDefinition.CreateDefault();
            Simulation = new VehicleSimulation(definition);
            var b = definition.body;
            Body.mass = b.massKg;
            Body.interpolation = RigidbodyInterpolation.Interpolate;
            Body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            Body.maxAngularVelocity = 30f;
            Body.linearDamping = 0f;
            Body.angularDamping = 0.05f;
            Body.ResetCenterOfMass();
            Body.ResetInertiaTensor();
            Body.centerOfMass = b.centerOfMass;
            Body.inertiaTensor = Vector3.Scale(Body.inertiaTensor, b.inertiaScale);
        }

        private void OnEnable() => SimRegistry.RegisterVehicle(this);
        private void OnDisable() => SimRegistry.UnregisterVehicle(this);

        private void Update()
        {
            if (Input == null || Simulation == null) return;
            var a = Input.Actions;
            var dt = Simulation.Drivetrain;
            float fwd = Vector3.Dot(Body.linearVelocity, transform.forward);
            if (a.SelectDrive) dt.SetSelector(GearSelector.Drive, fwd);
            if (a.SelectReverse) dt.SetSelector(GearSelector.Reverse, fwd);
            if (a.SelectNeutral) dt.SetSelector(GearSelector.Neutral, fwd);
            if (a.SelectPark) dt.SetSelector(GearSelector.Park, fwd);
            // Manual: sequential gear change. Automatic: move the selector lever along P-R-N-D.
            if (a.ShiftUp)
            {
                if (dt.Mode == TransmissionKind.Manual) dt.ShiftUp();
                else dt.SetSelector(StepSelector(dt.Selector, +1), fwd);
            }
            if (a.ShiftDown)
            {
                if (dt.Mode == TransmissionKind.Manual) dt.ShiftDown();
                else dt.SetSelector(StepSelector(dt.Selector, -1), fwd);
            }
            if (a.ToggleTransmissionMode)
                dt.Mode = dt.Mode == TransmissionKind.Automatic ? TransmissionKind.Manual : TransmissionKind.Automatic;
            if (a.Ignition) dt.StartEngine();
            if (a.ResetVehicle) ResetUpright();
        }

        private void FixedUpdate()
        {
            if (Simulation == null) return;
            float dtFixed = Time.fixedDeltaTime;
            var controls = Input != null ? Input.Controls : DriverControls.Neutral;

            // Park: the transmission is locked, so treat it as a strong brake on the driven wheels.
            if (Simulation.Drivetrain.Selector == GearSelector.Park)
                controls.Handbrake = Mathf.Max(controls.Handbrake, 1f);
            LastControls = controls;

            var tr = transform;
            Vector3 up = tr.up;
            float fwdSpeed = Vector3.Dot(Body.linearVelocity, tr.forward);
            Simulation.UpdateSteering(controls, Body.linearVelocity.magnitude);

            // ---- cast wheels and build contact frames
            for (int i = 0; i < 4; i++)
            {
                var tire = Simulation.Tire(i);
                var susp = Simulation.Suspension(i);
                Vector3 mount = tr.TransformPoint(Simulation.MountPoint(i));
                float maxDist = susp.restLength + tire.radius;
                ref var c = ref _contacts[i];

                if (CastWheel(mount, -up, maxDist, out RaycastHit hit))
                {
                    float compression = Mathf.Max(0f, susp.restLength - (hit.distance - tire.radius));
                    c.Grounded = true;
                    c.Compression = compression;
                    c.CompressionVelocity = (compression - _prevCompression[i]) / dtFixed;
                    _prevCompression[i] = compression;

                    Quaternion steer = Quaternion.AngleAxis(Simulation.Wheels[i].SteerAngle, up);
                    Vector3 fwd = Vector3.ProjectOnPlane(steer * tr.forward, hit.normal).normalized;
                    Vector3 side = Vector3.Cross(hit.normal, fwd);
                    Vector3 pv = Body.GetPointVelocity(hit.point);
                    c.Vx = Vector3.Dot(pv, fwd);
                    c.Vy = Vector3.Dot(pv, side);
                    c.SurfaceGrip = SurfaceGripFor(hit.collider);
                    ContactPoints[i] = hit.point;
                    _frames[i] = new Frame { Fwd = fwd, Side = side, Point = hit.point };
                }
                else
                {
                    c.Grounded = false;
                    c.Compression = 0f;
                    c.CompressionVelocity = 0f;
                    _prevCompression[i] = 0f;
                    c.Vx = Vector3.Dot(Body.GetPointVelocity(mount), tr.forward);
                    c.Vy = 0f;
                    c.SurfaceGrip = 1f;
                }
            }

            // ---- simulate
            Simulation.Step(dtFixed, substeps, controls, fwdSpeed, _contacts, _outputs);

            // ---- apply forces
            for (int i = 0; i < 4; i++)
            {
                if (!_contacts[i].Grounded) continue;
                var o = _outputs[i];
                var f = _frames[i];
                Vector3 p = f.Point + up * tireForceHeight;
                Body.AddForceAtPosition(up * o.SuspensionForce, f.Point, ForceMode.Force);
                Body.AddForceAtPosition(f.Fwd * o.Fx + f.Side * o.Fy, p, ForceMode.Force);
            }

            // Aerodynamic drag
            var b = definition.body;
            Vector3 v = Body.linearVelocity;
            Body.AddForce(-0.5f * 1.2f * b.dragCoefficient * b.frontalArea * v.magnitude * v, ForceMode.Force);

            // ---- telemetry
            Vector3 accel = (v - _prevVelocity) / dtFixed;
            _prevVelocity = v;
            _localAccel = Vector3.Lerp(_localAccel, tr.InverseTransformDirection(accel) / 9.81f, 0.2f);
            ref var t = ref Simulation.Telemetry;
            t.SpeedMs = v.magnitude;
            t.ForwardSpeedMs = fwdSpeed;
            t.LocalAccelerationG = _localAccel;
        }

        private static GearSelector StepSelector(GearSelector s, int dir)
        {
            // Lever order: P, R, N, D
            int i = Mathf.Clamp((int)s + dir, (int)GearSelector.Park, (int)GearSelector.Drive);
            return (GearSelector)i;
        }

        private struct Frame { public Vector3 Fwd, Side, Point; }
        private readonly Frame[] _frames = new Frame[4];

        private bool CastWheel(Vector3 origin, Vector3 dir, float maxDist, out RaycastHit best)
        {
            int n = Physics.RaycastNonAlloc(origin, dir, _hits, maxDist, ~0, QueryTriggerInteraction.Ignore);
            best = default;
            float bestDist = float.MaxValue;
            bool found = false;
            for (int k = 0; k < n; k++)
            {
                var h = _hits[k];
                if (h.collider.attachedRigidbody == Body) continue;
                if (h.distance < bestDist) { bestDist = h.distance; best = h; found = true; }
            }
            return found;
        }

        private static float SurfaceGripFor(Collider col)
        {
            var surface = col.sharedMaterial;
            // Surfaces are tagged by their PhysicsMaterial name. Defaults to asphalt.
            if (surface == null) return 1f;
            string n = surface.name;
            if (n.StartsWith("Sand")) return 0.55f;
            if (n.StartsWith("Grass")) return 0.6f;
            if (n.StartsWith("Wet")) return 0.75f;
            if (n.StartsWith("Paint")) return 0.85f;
            return 1f;
        }

        /// <summary>Puts the car back on its wheels at the current location.</summary>
        public void ResetUpright()
        {
            Vector3 fwd = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            if (fwd.sqrMagnitude < 1e-3f) fwd = Vector3.forward;
            Teleport(transform.position + Vector3.up * 0.6f, Quaternion.LookRotation(fwd.normalized, Vector3.up));
        }

        public void Teleport(Vector3 position, Quaternion rotation)
        {
            Body.linearVelocity = Vector3.zero;
            Body.angularVelocity = Vector3.zero;
            Body.position = position;
            Body.rotation = rotation;
            transform.SetPositionAndRotation(position, rotation);
            _prevVelocity = Vector3.zero;
            Simulation.ResetDynamics();
            for (int i = 0; i < 4; i++) _prevCompression[i] = 0f;
        }

        private void OnDrawGizmosSelected()
        {
            var def = definition != null ? definition : null;
            if (def == null) return;
            var sim = Simulation ?? new VehicleSimulation(def);
            Gizmos.color = Color.cyan;
            for (int i = 0; i < 4; i++)
            {
                Vector3 m = transform.TransformPoint(sim.MountPoint(i));
                var susp = sim.Suspension(i);
                Gizmos.DrawLine(m, m - transform.up * (susp.restLength + sim.Tire(i).radius));
                Gizmos.DrawWireSphere(m - transform.up * susp.restLength, sim.Tire(i).radius);
            }
        }
    }
}
