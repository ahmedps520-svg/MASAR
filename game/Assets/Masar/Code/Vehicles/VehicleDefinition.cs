using System;
using UnityEngine;

namespace Masar.Vehicles
{
    public enum DrivetrainLayout { FrontWheelDrive, RearWheelDrive, AllWheelDrive }
    public enum TransmissionKind { Automatic, Manual }
    public enum DifferentialKind { Open, LimitedSlip, Locked }

    [Serializable]
    public class EngineSpec
    {
        [Tooltip("RPM sample points for the full-throttle torque curve.")]
        public float[] torqueCurveRpm = { 800, 1500, 2500, 3500, 4500, 5500, 6200, 6800 };
        [Tooltip("Full-throttle torque (N·m) at each RPM sample.")]
        public float[] torqueCurveNm = { 160, 200, 235, 250, 248, 232, 210, 180 };
        public float idleRpm = 750f;
        public float redlineRpm = 6500f;
        public float revLimiterRpm = 6700f;
        public float stallRpm = 450f;
        [Tooltip("Engine + flywheel rotational inertia (kg·m²).")]
        public float inertia = 0.18f;
        [Tooltip("Closed-throttle friction/pumping torque at redline (N·m). Scales with RPM.")]
        public float engineBrakeTorqueAtRedline = 55f;

        public float TorqueAt(float rpm)
        {
            var r = torqueCurveRpm;
            var t = torqueCurveNm;
            if (r == null || t == null || r.Length == 0 || r.Length != t.Length) return 0f;
            if (rpm <= r[0]) return t[0];
            for (int i = 1; i < r.Length; i++)
            {
                if (rpm <= r[i])
                {
                    float k = (rpm - r[i - 1]) / (r[i] - r[i - 1]);
                    return Mathf.Lerp(t[i - 1], t[i], k);
                }
            }
            return t[t.Length - 1];
        }
    }

    [Serializable]
    public class TransmissionSpec
    {
        public TransmissionKind kind = TransmissionKind.Automatic;
        [Tooltip("Forward gear ratios, 1st first.")]
        public float[] forwardRatios = { 3.30f, 2.10f, 1.45f, 1.10f, 0.87f, 0.69f };
        public float reverseRatio = 3.25f;
        public float finalDrive = 3.85f;
        [Range(0.5f, 1f)] public float efficiency = 0.90f;
        [Tooltip("Seconds of zero drive torque while a gear change happens.")]
        public float shiftTime = 0.22f;
        [Tooltip("Automatic: upshift RPM at light throttle and at full throttle.")]
        public float upshiftRpmLight = 2300f;
        public float upshiftRpmFull = 6000f;
        public float downshiftRpm = 1350f;
        [Tooltip("Max torque (N·m) the clutch / lock-up can transmit when fully engaged.")]
        public float clutchMaxTorque = 420f;
        [Tooltip("Clutch coupling stiffness (N·m per rad/s of slip).")]
        public float clutchStiffness = 18f;
        [Tooltip("Automatic: engine RPM at which the launch coupling is fully engaged at full throttle.")]
        public float launchFullEngageRpm = 2400f;
    }

    [Serializable]
    public class DrivetrainSpec
    {
        public DrivetrainLayout layout = DrivetrainLayout.FrontWheelDrive;
        [Range(0f, 1f)] public float awdFrontShare = 0.4f;
        public DifferentialKind differential = DifferentialKind.Open;
        [Tooltip("Limited-slip: locking torque per rad/s of wheel-speed difference.")]
        public float lsdStiffness = 40f;
        [Tooltip("Limited-slip: max locking torque as a fraction of input torque.")]
        [Range(0f, 1f)] public float lsdMaxLockFraction = 0.35f;
    }

    [Serializable]
    public class AxleSuspensionSpec
    {
        [Tooltip("Spring rate at the wheel (N/m).")]
        public float springRate = 32000f;
        [Tooltip("Damping in compression (N·s/m).")]
        public float bumpDamping = 2600f;
        [Tooltip("Damping in extension (N·s/m).")]
        public float reboundDamping = 3400f;
        [Tooltip("Suspension length from mount to wheel center at full extension (m).")]
        public float restLength = 0.36f;
        [Tooltip("Usable compression before the bump stop (m).")]
        public float travel = 0.20f;
        [Tooltip("Anti-roll bar stiffness (N/m of left/right compression difference).")]
        public float antiRollStiffness = 9000f;
    }

    [Serializable]
    public class TireSpec
    {
        public float radius = 0.325f;
        public float width = 0.215f;
        [Tooltip("Wheel + tire + brake rotor rotational inertia (kg·m²).")]
        public float inertia = 1.1f;
        [Tooltip("Peak friction coefficient on dry asphalt.")]
        public float peakFriction = 1.0f;
        [Tooltip("Friction drop per unit of extra load ratio (load sensitivity).")]
        public float loadSensitivity = 0.12f;
        [Tooltip("Nominal load (N) for load sensitivity.")]
        public float nominalLoad = 4000f;
        // Simplified Pacejka shape coefficients
        public float longB = 11f, longC = 1.65f, longE = 0.3f;
        public float latB = 14f, latC = 1.35f, latE = -0.2f;
        [Tooltip("Longitudinal relaxation length (m).")]
        public float longRelaxation = 0.35f;
        [Tooltip("Lateral relaxation length (m).")]
        public float latRelaxation = 0.55f;
        public float rollingResistance = 0.012f;
        [Tooltip("Pneumatic trail at small slip angles (m). Used for self-aligning torque.")]
        public float pneumaticTrail = 0.035f;
    }

    [Serializable]
    public class SteeringSpec
    {
        [Tooltip("Road-wheel angle at full lock (degrees).")]
        public float maxSteerAngle = 34f;
        [Tooltip("Steering-wheel rotation from center to full lock (degrees), for visuals and wheel mapping.")]
        public float steeringWheelLockDegrees = 495f;
        [Range(0f, 1f)] public float ackermann = 0.8f;
        [Tooltip("Assist for digital/gamepad input only: steering limit at 30 m/s as fraction of full lock.")]
        [Range(0.1f, 1f)] public float highSpeedSteerLimit = 0.35f;
    }

    [Serializable]
    public class BrakeSpec
    {
        [Tooltip("Max brake torque per front wheel (N·m).")]
        public float frontTorque = 2300f;
        [Tooltip("Max brake torque per rear wheel (N·m).")]
        public float rearTorque = 1300f;
        [Tooltip("Handbrake torque per rear wheel (N·m).")]
        public float handbrakeTorque = 1800f;
        public bool abs = true;
    }

    [Serializable]
    public class BodySpec
    {
        public float massKg = 1480f;
        [Tooltip("Center of mass relative to the chassis origin (m).")]
        public Vector3 centerOfMass = new Vector3(0f, 0.45f, 0.10f);
        [Tooltip("Multiplier on the inertia tensor Unity computes from the colliders.")]
        public Vector3 inertiaScale = new Vector3(1f, 1.1f, 1f);
        public float wheelbase = 2.82f;
        public float trackFront = 1.58f;
        public float trackRear = 1.57f;
        [Tooltip("Height of the suspension mount points above the chassis origin (m).")]
        public float mountHeight = 0.62f;
        [Tooltip("Longitudinal offset of the axle midpoint from the chassis origin (m).")]
        public float axleCenterOffset = 0f;
        public float dragCoefficient = 0.30f;
        public float frontalArea = 2.25f;
    }

    /// <summary>
    /// Complete physical description of one vehicle model. Adding a new car means
    /// adding a new definition asset (or JSON file). It never means new physics code.
    /// </summary>
    [CreateAssetMenu(menuName = "MASAR/Vehicle Definition", fileName = "VehicleDefinition")]
    public class VehicleDefinition : ScriptableObject
    {
        public string displayName = "Test Sedan";
        [TextArea] public string notes = "Generic mid-size FWD sedan. Phase 0 physics test vehicle (not a real model).";
        public BodySpec body = new BodySpec();
        public EngineSpec engine = new EngineSpec();
        public TransmissionSpec transmission = new TransmissionSpec();
        public DrivetrainSpec drivetrain = new DrivetrainSpec();
        public AxleSuspensionSpec frontSuspension = new AxleSuspensionSpec();
        public AxleSuspensionSpec rearSuspension = new AxleSuspensionSpec { springRate = 29000f, antiRollStiffness = 6000f };
        public TireSpec frontTires = new TireSpec();
        public TireSpec rearTires = new TireSpec();
        public SteeringSpec steering = new SteeringSpec();
        public BrakeSpec brakes = new BrakeSpec();
        public float fuelCapacityLitres = 60f;

        /// <summary>Creates an in-memory definition with the default values (used by tests and as a fallback).</summary>
        public static VehicleDefinition CreateDefault()
        {
            var d = CreateInstance<VehicleDefinition>();
            d.name = "DefaultSedan";
            return d;
        }
    }
}
