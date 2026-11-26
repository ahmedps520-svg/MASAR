using UnityEngine;

namespace Masar.Vehicles
{
    /// <summary>
    /// Engine, clutch / launch coupling, gearbox and differential, integrated at the
    /// physics sub-step rate. Pure C#: it reads the driven wheels' angular velocities
    /// and returns the drive torque for each wheel.
    /// </summary>
    public sealed class Drivetrain
    {
        public const float RadPerSecToRpm = 60f / (2f * Mathf.PI);

        private readonly VehicleDefinition _def;

        public float EngineOmega { get; private set; }     // rad/s
        public float EngineRpm => EngineOmega * RadPerSecToRpm;
        public bool EngineRunning { get; private set; } = true;
        /// <summary>0 = neutral, 1..N forward gears, -1 = reverse.</summary>
        public int Gear { get; private set; } = 1;
        public float EngineTorque { get; private set; }
        public float ClutchTorque { get; private set; }
        public float ClutchEngagement { get; private set; }
        public bool IsShifting => _shiftTimer > 0f;
        public TransmissionKind Mode { get; set; }
        public Masar.Core.GearSelector Selector { get; private set; } = Masar.Core.GearSelector.Drive;
        public float AppliedThrottle { get; private set; }
        private float _idleTrim;

        private float _shiftTimer;
        private int _pendingGear;
        private float _shiftCooldown;

        public int ForwardGearCount => _def.transmission.forwardRatios.Length;

        private readonly float _drivenWheelInertia;

        public Drivetrain(VehicleDefinition def)
        {
            _def = def;
            switch (def.drivetrain.layout)
            {
                case DrivetrainLayout.FrontWheelDrive: _drivenWheelInertia = 2f * def.frontTires.inertia; break;
                case DrivetrainLayout.RearWheelDrive: _drivenWheelInertia = 2f * def.rearTires.inertia; break;
                default: _drivenWheelInertia = 2f * (def.frontTires.inertia + def.rearTires.inertia); break;
            }
            Mode = def.transmission.kind;
            EngineOmega = def.engine.idleRpm / RadPerSecToRpm;
            Gear = 0;
            Selector = Masar.Core.GearSelector.Park;
        }

        public float CurrentRatio()
        {
            var t = _def.transmission;
            if (Gear == 0) return 0f;
            if (Gear < 0) return -t.reverseRatio * t.finalDrive;
            int idx = Mathf.Clamp(Gear - 1, 0, t.forwardRatios.Length - 1);
            return t.forwardRatios[idx] * t.finalDrive;
        }

        // ------------------------------------------------------------------ inputs

        public void SetSelector(Masar.Core.GearSelector selector, float forwardSpeed)
        {
            // Refuse obviously damaging selections at speed (as real gearboxes and their interlocks do).
            if ((selector == Masar.Core.GearSelector.Reverse && forwardSpeed > 2f) ||
                (selector == Masar.Core.GearSelector.Drive && forwardSpeed < -2f) ||
                (selector == Masar.Core.GearSelector.Park && Mathf.Abs(forwardSpeed) > 1f))
                return;
            Selector = selector;
            switch (selector)
            {
                case Masar.Core.GearSelector.Park:
                case Masar.Core.GearSelector.Neutral: RequestGear(0); break;
                case Masar.Core.GearSelector.Reverse: RequestGear(-1); break;
                case Masar.Core.GearSelector.Drive: if (Gear <= 0) RequestGear(1); break;
            }
        }

        /// <summary>The gear we are in, or the one we are shifting into.</summary>
        public int TargetGear => _shiftTimer > 0f ? _pendingGear : Gear;

        public void ShiftUp()
        {
            if (Mode != TransmissionKind.Manual) return;
            int g = TargetGear;
            if (g >= ForwardGearCount) return;
            RequestGear(g + 1);
            Selector = SelectorFor(g + 1);
        }

        public void ShiftDown()
        {
            if (Mode != TransmissionKind.Manual) return;
            int g = TargetGear;
            if (g <= -1) return;
            RequestGear(g - 1);
            Selector = SelectorFor(g - 1);
        }

        private static Masar.Core.GearSelector SelectorFor(int gear) =>
            gear > 0 ? Masar.Core.GearSelector.Drive :
            gear < 0 ? Masar.Core.GearSelector.Reverse : Masar.Core.GearSelector.Neutral;

        public void StartEngine()
        {
            if (!EngineRunning)
            {
                EngineRunning = true;
                EngineOmega = _def.engine.idleRpm / RadPerSecToRpm;
            }
        }

        private void RequestGear(int g)
        {
            if (g == Gear && _shiftTimer <= 0f) return;
            _pendingGear = g;
            _shiftTimer = _def.transmission.shiftTime;
        }

        // ------------------------------------------------------------------ step

        /// <summary>
        /// Advances the drivetrain by dt and returns the driveshaft torque split per wheel.
        /// wheelOmega: angular velocity of all 4 wheels (FL, FR, RL, RR).
        /// driveTorque: output, torque applied to each wheel.
        /// </summary>
        public void Step(float dt, float throttle, float clutchPedal, bool hasClutchPedal,
                         float forwardSpeed, float[] wheelOmega, float[] driveTorque)
        {
            var e = _def.engine;
            var t = _def.transmission;

            for (int i = 0; i < 4; i++) driveTorque[i] = 0f;

            // --- gear change in progress: drive is interrupted
            if (_shiftTimer > 0f)
            {
                _shiftTimer -= dt;
                if (_shiftTimer <= 0f) Gear = _pendingGear;
            }
            if (_shiftCooldown > 0f) _shiftCooldown -= dt;

            // --- automatic shift schedule
            if (Mode == TransmissionKind.Automatic && Selector == Masar.Core.GearSelector.Drive && !IsShifting && Gear > 0 && _shiftCooldown <= 0f)
            {
                float upRpm = Mathf.Lerp(t.upshiftRpmLight, t.upshiftRpmFull, throttle * throttle);
                float wheelRpm = DrivenWheelOmega(wheelOmega) * CurrentRatio() * RadPerSecToRpm;
                if (wheelRpm > upRpm && Gear < ForwardGearCount) { RequestGear(Gear + 1); _shiftCooldown = 0.8f; }
                else if (wheelRpm < t.downshiftRpm && Gear > 1) { RequestGear(Gear - 1); _shiftCooldown = 0.5f; }
                // Kick-down: heavy throttle at low RPM
                else if (throttle > 0.9f && wheelRpm < upRpm * 0.45f && Gear > 1) { RequestGear(Gear - 1); _shiftCooldown = 0.8f; }
            }

            // --- engine torque
            float rpm = EngineOmega * RadPerSecToRpm;
            float thr = EngineRunning ? throttle : 0f;
            // Idle governor: hold idle by opening the throttle a little. Proportional + integral, so
            // it settles on the idle speed instead of drooping below it (KNOWN_ISSUES T8). The
            // integral only works near idle and only learns while the driver is off the throttle.
            if (EngineRunning && rpm < e.idleRpm + 300f)
            {
                if (throttle < 0.05f) _idleTrim = Mathf.Clamp(_idleTrim + (e.idleRpm - rpm) * 0.0008f * dt, 0f, 0.3f);
                float p = Mathf.Clamp01((e.idleRpm - rpm) / 250f) * 0.35f;
                thr = Mathf.Max(thr, Mathf.Clamp01(p + _idleTrim * Mathf.InverseLerp(e.idleRpm + 300f, e.idleRpm, rpm)));
            }
            // Rev limiter: hard fuel cut
            if (rpm > e.revLimiterRpm) thr = 0f;
            AppliedThrottle = thr;

            float full = e.TorqueAt(rpm);
            float friction = e.engineBrakeTorqueAtRedline * Mathf.Clamp01(rpm / e.redlineRpm) + 8f;
            EngineTorque = EngineRunning ? thr * full - (1f - thr) * friction : -friction * 1.5f;

            // --- clutch / launch coupling
            float ratio = CurrentRatio();
            bool inGear = ratio != 0f && !IsShifting;
            float engagement;
            if (!inGear) engagement = 0f;
            else if (Mode == TransmissionKind.Manual && hasClutchPedal) engagement = 1f - Mathf.Clamp01(clutchPedal);
            else
            {
                // Automatic / auto-clutch: engagement rises with engine RPM above idle (torque-converter-like),
                // and is always full once the car is moving at road speed in gear.
                float launch = Mathf.InverseLerp(e.idleRpm + 50f, t.launchFullEngageRpm, rpm);
                float rolling = Mathf.InverseLerp(2f, 5f, Mathf.Abs(forwardSpeed));
                engagement = Mathf.Max(launch, rolling);
                // Creep: a small coupling at idle in D/R (automatic transmissions creep)
                if (Mode == TransmissionKind.Automatic) engagement = Mathf.Max(engagement, 0.12f);
            }
            ClutchEngagement = engagement;

            float shaftOmega = DrivenWheelOmega(wheelOmega) * ratio;          // gearbox input speed
            float slip = EngineOmega - shaftOmega;
            float capacity = t.clutchMaxTorque * engagement;

            // Implicit (backward-Euler) viscous coupling between the engine and the driven wheels.
            // The effective inertia of the slip mode combines the engine with the wheels reflected
            // through the gear ratio. Solving implicitly keeps this stiff coupling stable at
            // every gear ratio. An explicit spring would blow up in 1st gear.
            float invIeff = 1f / Mathf.Max(0.02f, e.inertia) + ratio * ratio / Mathf.Max(0.1f, _drivenWheelInertia);
            float c = t.clutchStiffness * 10f;
            float slipPred = slip + dt * EngineTorque / Mathf.Max(0.02f, e.inertia);
            float clutchT = c * slipPred / (1f + c * dt * invIeff);
            clutchT = Mathf.Clamp(clutchT, -capacity, capacity);
            ClutchTorque = clutchT;

            // --- integrate engine
            EngineOmega += (EngineTorque - clutchT) / Mathf.Max(0.02f, e.inertia) * dt;
            if (EngineOmega < 0f) EngineOmega = 0f;

            // Stall (manual with clutch pedal only; automatic/auto-clutch can't stall)
            if (EngineRunning && Mode == TransmissionKind.Manual && hasClutchPedal && EngineOmega * RadPerSecToRpm < e.stallRpm && engagement > 0.5f)
                EngineRunning = false;

            // --- distribute torque through the differential(s)
            if (ratio == 0f) return;
            float shaftTorque = clutchT * ratio * t.efficiency;
            SplitTorque(shaftTorque, wheelOmega, driveTorque);
        }

        private float DrivenWheelOmega(float[] w)
        {
            switch (_def.drivetrain.layout)
            {
                case DrivetrainLayout.FrontWheelDrive: return 0.5f * (w[0] + w[1]);
                case DrivetrainLayout.RearWheelDrive: return 0.5f * (w[2] + w[3]);
                default:
                    float f = _def.drivetrain.awdFrontShare;
                    return f * 0.5f * (w[0] + w[1]) + (1f - f) * 0.5f * (w[2] + w[3]);
            }
        }

        private void SplitTorque(float torque, float[] w, float[] outT)
        {
            var d = _def.drivetrain;
            switch (d.layout)
            {
                case DrivetrainLayout.FrontWheelDrive: Axle(torque, w[0], w[1], out outT[0], out outT[1]); break;
                case DrivetrainLayout.RearWheelDrive: Axle(torque, w[2], w[3], out outT[2], out outT[3]); break;
                default:
                    Axle(torque * d.awdFrontShare, w[0], w[1], out outT[0], out outT[1]);
                    Axle(torque * (1f - d.awdFrontShare), w[2], w[3], out outT[2], out outT[3]);
                    break;
            }
        }

        private void Axle(float torque, float wl, float wr, out float tl, out float tr)
        {
            var d = _def.drivetrain;
            tl = tr = 0.5f * torque;
            if (d.differential == DifferentialKind.Open) return;
            float diff = wl - wr;
            float lockMax = d.differential == DifferentialKind.Locked ? 1e5f : Mathf.Abs(torque) * d.lsdMaxLockFraction;
            float stiffness = d.differential == DifferentialKind.Locked ? 400f : d.lsdStiffness;
            float lockT = Mathf.Clamp(diff * stiffness, -lockMax, lockMax);
            tl -= lockT * 0.5f;
            tr += lockT * 0.5f;
        }
    }
}
