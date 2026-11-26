using Masar.Core;
using UnityEngine;

namespace Masar.Vehicles
{
    /// <summary>Per-wheel contact data gathered by the physics adapter (ray casts) each fixed step.</summary>
    public struct WheelContact
    {
        public bool Grounded;
        /// <summary>Suspension compression (m), 0 = fully extended.</summary>
        public float Compression;
        /// <summary>Compression rate (m/s), positive = compressing.</summary>
        public float CompressionVelocity;
        /// <summary>Contact-point velocity along the wheel's forward axis (m/s).</summary>
        public float Vx;
        /// <summary>Contact-point velocity along the wheel's side axis (m/s).</summary>
        public float Vy;
        /// <summary>Surface grip multiplier (1 = dry asphalt).</summary>
        public float SurfaceGrip;
    }

    /// <summary>Forces the adapter must apply for one wheel this fixed step.</summary>
    public struct WheelOutput
    {
        public float SuspensionForce; // N, along the chassis up axis
        public float Fx;              // N, along the wheel forward axis
        public float Fy;              // N, along the wheel side axis
    }

    /// <summary>Live state of one wheel. Readable by visuals, audio, telemetry and FFB.</summary>
    public struct WheelState
    {
        public float Omega;          // rad/s
        public float SpinAngle;      // degrees, for visuals
        public float SteerAngle;     // degrees (positive = right)
        public float Load;           // N
        public float SlipRatio;      // relaxed, -1..1 typical
        public float TanSlipAngle;   // relaxed tan(alpha)
        public float Compression;    // m
        public bool Grounded;
        public float DriveTorque;    // N·m
        public float BrakeTorque;    // N·m
    }

    /// <summary>Snapshot of the vehicle for HUD, cameras, audio and force feedback.</summary>
    public struct VehicleTelemetry
    {
        public float SpeedMs;
        public float ForwardSpeedMs;
        public float EngineRpm;
        public bool EngineRunning;
        public int Gear;
        public GearSelector Selector;
        public TransmissionKind TransmissionMode;
        public bool Shifting;
        public float Throttle;
        public float Brake;
        public float Clutch;
        public float SteerInput;
        public float SteeringWheelAngleDeg;
        /// <summary>Front-axle self-aligning torque (N·m). Positive pulls the wheel to the right.</summary>
        public float SelfAligningTorque;
        public Vector3 LocalAccelerationG;
        public int GroundedWheels;
        public bool AbsActive;
    }

    /// <summary>
    /// The vehicle-dynamics core. Pure C# (no MonoBehaviour, no rendering), so it can
    /// be unit-tested and profiled on its own. Wheels are indexed FL, FR, RL, RR.
    /// </summary>
    public sealed class VehicleSimulation
    {
        public const int FL = 0, FR = 1, RL = 2, RR = 3;

        public readonly VehicleDefinition Def;
        public readonly Drivetrain Drivetrain;
        public readonly WheelState[] Wheels = new WheelState[4];
        public VehicleTelemetry Telemetry;

        private readonly float[] _omega = new float[4];
        private readonly float[] _driveTorque = new float[4];
        private readonly float[] _slipRatio = new float[4];
        private readonly float[] _tanSlip = new float[4];
        private readonly float[] _sumFx = new float[4];
        private readonly float[] _sumFy = new float[4];
        private bool _absActive;

        public VehicleSimulation(VehicleDefinition def)
        {
            Def = def;
            Drivetrain = new Drivetrain(def);
        }

        public TireSpec Tire(int i) => i < 2 ? Def.frontTires : Def.rearTires;
        public AxleSuspensionSpec Suspension(int i) => i < 2 ? Def.frontSuspension : Def.rearSuspension;

        /// <summary>Wheel mount point in chassis space (top of the suspension travel).</summary>
        public Vector3 MountPoint(int i)
        {
            var b = Def.body;
            float x = (i == FL || i == RL ? -0.5f : 0.5f) * (i < 2 ? b.trackFront : b.trackRear);
            float z = b.axleCenterOffset + (i < 2 ? 0.5f : -0.5f) * b.wheelbase;
            return new Vector3(x, b.mountHeight, z);
        }

        /// <summary>
        /// Computes the front-wheel steering angles (with Ackermann) from the steering input.
        /// Call this before casting the wheels, because the contact frames depend on the steer angle.
        /// </summary>
        public void UpdateSteering(in DriverControls c, float speedMs)
        {
            var s = Def.steering;
            float input = Mathf.Clamp(c.Steer, -1f, 1f);
            if (!c.SteerIsAbsolute)
            {
                // Speed-sensitive limit for digital/gamepad steering only. Never applied to a real wheel.
                float limit = Mathf.Lerp(1f, s.highSpeedSteerLimit, Mathf.Clamp01(Mathf.Abs(speedMs) / 30f));
                input *= limit;
            }
            float delta = input * s.maxSteerAngle * Mathf.Deg2Rad;
            float inner = delta, outer = delta;
            if (Mathf.Abs(delta) > 1e-4f)
            {
                float L = Def.body.wheelbase;
                float T = Def.body.trackFront;
                float R = L / Mathf.Tan(Mathf.Abs(delta));
                float a = Mathf.Atan(L / Mathf.Max(0.1f, R - 0.5f * T));
                float o = Mathf.Atan(L / (R + 0.5f * T));
                inner = Mathf.Sign(delta) * Mathf.Lerp(Mathf.Abs(delta), a, s.ackermann);
                outer = Mathf.Sign(delta) * Mathf.Lerp(Mathf.Abs(delta), o, s.ackermann);
            }
            // Turning right (delta > 0): the right wheel is the inner wheel.
            float left = delta > 0f ? outer : inner;
            float right = delta > 0f ? inner : outer;
            Wheels[FL].SteerAngle = left * Mathf.Rad2Deg;
            Wheels[FR].SteerAngle = right * Mathf.Rad2Deg;
            Wheels[RL].SteerAngle = 0f;
            Wheels[RR].SteerAngle = 0f;
            Telemetry.SteerInput = input;
            Telemetry.SteeringWheelAngleDeg = input * s.steeringWheelLockDegrees;
        }

        /// <summary>Suspension force (N) from compression, including spring, damper, bump stop and anti-roll bar.</summary>
        private float SuspensionForce(int i, in WheelContact c, in WheelContact opposite)
        {
            if (!c.Grounded) return 0f;
            var s = Suspension(i);
            float spring = s.springRate * c.Compression;
            float damp = c.CompressionVelocity * (c.CompressionVelocity > 0f ? s.bumpDamping : s.reboundDamping);
            float bump = c.Compression > s.travel ? (c.Compression - s.travel) * s.springRate * 12f : 0f;
            float oppComp = opposite.Grounded ? opposite.Compression : 0f;
            float arb = (c.Compression - oppComp) * s.antiRollStiffness;
            return Mathf.Max(0f, spring + damp + bump + arb);
        }

        /// <summary>
        /// Advances the simulation by one fixed step. Wheel spin, drivetrain and tire slip
        /// are sub-stepped for stability. The chassis forces returned are averaged over the sub-steps.
        /// </summary>
        public void Step(float dt, int substeps, in DriverControls c, float forwardSpeed,
                         WheelContact[] contacts, WheelOutput[] output)
        {
            substeps = Mathf.Max(1, substeps);
            float h = dt / substeps;

            // Suspension loads are constant across sub-steps (the chassis only moves once per fixed step).
            float[] load = _load;
            for (int i = 0; i < 4; i++)
            {
                int opp = i ^ 1;
                load[i] = SuspensionForce(i, contacts[i], contacts[opp]);
                output[i].SuspensionForce = load[i];
                _sumFx[i] = 0f;
                _sumFy[i] = 0f;
            }

            var br = Def.brakes;
            _absActive = false;

            // Static friction at (near) standstill. The slip model gives a force that grows with
            // slip speed, so on a slope a braked car would creep slowly downhill. Below 0.35 m/s a
            // contact that is held (wheel locked by the brakes, or no sideways motion) is instead
            // pinned by a stiff spring-damper anchored where it stopped, up to the friction limit.
            // Above the limit it lets go and the slip model takes over again.
            for (int i = 0; i < 4; i++)
            {
                var ct = contacts[i];
                _stickX[i] = _stickY[i] = false;
                if (!ct.Grounded || load[i] < 1f || Mathf.Abs(ct.Vx) > StickSpeed || Mathf.Abs(ct.Vy) > StickSpeed)
                {
                    _anchorX[i] = _anchorY[i] = 0f;
                    continue;
                }
                float brakeT = c.Brake * (i < 2 ? br.frontTorque : br.rearTorque) + (i >= 2 ? c.Handbrake * br.handbrakeTorque : 0f);
                bool held = Wheels[i].Omega == 0f && brakeT > 50f;
                float limit = TireModel.EffectiveFriction(Tire(i), load[i], ct.SurfaceGrip) * load[i];
                float k = load[i] * StickStiffness, d = load[i] * StickDamping;
                if (held)
                {
                    _anchorX[i] += ct.Vx * dt;
                    float f = -(k * _anchorX[i] + d * ct.Vx);
                    // The brake can only hold the wheel up to its torque
                    float cap = Mathf.Min(limit, brakeT / Tire(i).radius);
                    if (Mathf.Abs(f) <= cap) { _fStickX[i] = f; _stickX[i] = true; }
                    else _anchorX[i] = 0f;
                }
                else _anchorX[i] = 0f;
                if (Mathf.Abs(ct.Vx) < StickSpeed)
                {
                    _anchorY[i] += ct.Vy * dt;
                    float f = -(k * _anchorY[i] + d * ct.Vy);
                    if (Mathf.Abs(f) <= limit) { _fStickY[i] = f; _stickY[i] = true; }
                    else _anchorY[i] = 0f;
                }
            }

            for (int step = 0; step < substeps; step++)
            {
                for (int i = 0; i < 4; i++) _omega[i] = Wheels[i].Omega;
                Drivetrain.Step(h, c.Throttle, c.Clutch, c.HasClutchPedal, forwardSpeed, _omega, _driveTorque);

                for (int i = 0; i < 4; i++)
                {
                    var tire = Tire(i);
                    ref var w = ref Wheels[i];
                    var ct = contacts[i];

                    float fx = 0f, fy = 0f;
                    if (ct.Grounded && load[i] > 1f)
                    {
                        TireModel.ComputeForces(tire, load[i], ct.SurfaceGrip, _slipRatio[i], _tanSlip[i], out fx, out fy, out _);
                        if (_stickX[i]) fx = _fStickX[i];
                        if (_stickY[i]) fy = _fStickY[i];
                    }

                    // Brakes (with a simple ABS release when the wheel is locking).
                    float brakeT = c.Brake * (i < 2 ? br.frontTorque : br.rearTorque);
                    if (i >= 2) brakeT += c.Handbrake * br.handbrakeTorque;
                    if (br.abs && c.Brake > 0.05f && c.Handbrake < 0.05f && Mathf.Abs(ct.Vx) > 2f && _slipRatio[i] < -0.12f)
                    {
                        brakeT *= 0.25f;
                        _absActive = true;
                    }

                    // Rolling resistance as a torque opposing the wheel spin
                    float rollT = ct.Grounded ? tire.rollingResistance * load[i] * tire.radius : 0f;

                    // Integrate wheel spin. Brake and rolling torque can stop the wheel, never reverse it.
                    float netT = _driveTorque[i] - fx * tire.radius;
                    float omega = w.Omega + netT / tire.inertia * h;
                    float stopT = (brakeT + rollT) / tire.inertia * h;
                    if (Mathf.Abs(omega) <= stopT) omega = 0f;
                    else omega -= Mathf.Sign(omega) * stopT;
                    w.Omega = omega;
                    w.SpinAngle = Mathf.Repeat(w.SpinAngle + omega * Mathf.Rad2Deg * h, 360f);

                    // Relax slip states toward the new contact kinematics
                    if (ct.Grounded)
                    {
                        float slipVel = omega * tire.radius - ct.Vx;
                        TireModel.RelaxSlip(tire, ct.Vx, ct.Vy, slipVel, h, ref _slipRatio[i], ref _tanSlip[i]);
                    }
                    else
                    {
                        _slipRatio[i] = Mathf.MoveTowards(_slipRatio[i], 0f, h * 5f);
                        _tanSlip[i] = Mathf.MoveTowards(_tanSlip[i], 0f, h * 5f);
                    }

                    _sumFx[i] += fx;
                    _sumFy[i] += fy;
                    w.DriveTorque = _driveTorque[i];
                    w.BrakeTorque = brakeT;
                }
            }

            float inv = 1f / substeps;
            float sat = 0f;
            int grounded = 0;
            for (int i = 0; i < 4; i++)
            {
                ref var w = ref Wheels[i];
                output[i].Fx = _sumFx[i] * inv;
                output[i].Fy = _sumFy[i] * inv;
                w.Load = load[i];
                w.SlipRatio = _slipRatio[i];
                w.TanSlipAngle = _tanSlip[i];
                w.Compression = contacts[i].Compression;
                w.Grounded = contacts[i].Grounded;
                if (w.Grounded) grounded++;
                if (i < 2) sat += TireModel.SelfAligningTorque(Tire(i), output[i].Fy, _tanSlip[i]);
            }

            ref var t = ref Telemetry;
            t.EngineRpm = Drivetrain.EngineRpm;
            t.EngineRunning = Drivetrain.EngineRunning;
            t.Gear = Drivetrain.Gear;
            t.Selector = Drivetrain.Selector;
            t.TransmissionMode = Drivetrain.Mode;
            t.Shifting = Drivetrain.IsShifting;
            t.Throttle = Drivetrain.AppliedThrottle;
            t.Brake = c.Brake;
            t.Clutch = c.Clutch;
            t.SelfAligningTorque = sat;
            t.GroundedWheels = grounded;
            t.AbsActive = _absActive;
        }

        /// <summary>Clears all dynamic state (used when the vehicle is reset or teleported).</summary>
        public void ResetDynamics()
        {
            for (int i = 0; i < 4; i++)
            {
                Wheels[i].Omega = 0f;
                _slipRatio[i] = 0f;
                _tanSlip[i] = 0f;
                _anchorX[i] = _anchorY[i] = 0f;
            }
        }

        private readonly float[] _load = new float[4];

        // Static friction state (see Step)
        private const float StickSpeed = 0.35f;        // m/s
        private const float StickStiffness = 30f;      // N per m of creep, per N of load
        private const float StickDamping = 3f;         // N per m/s, per N of load
        private readonly float[] _anchorX = new float[4], _anchorY = new float[4];
        private readonly float[] _fStickX = new float[4], _fStickY = new float[4];
        private readonly bool[] _stickX = new bool[4], _stickY = new bool[4];
    }
}
