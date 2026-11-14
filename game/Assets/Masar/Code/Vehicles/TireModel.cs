using UnityEngine;

namespace Masar.Vehicles
{
    /// <summary>
    /// Tire force model: simplified Pacejka "magic formula" curves, load sensitivity,
    /// relaxation-length slip dynamics and a friction ellipse for combined slip.
    /// Pure functions. No Unity physics calls.
    /// </summary>
    public static class TireModel
    {
        /// <summary>Normalized magic-formula curve (output roughly in -1..1).</summary>
        public static float MagicFormula(float x, float b, float c, float e)
        {
            float bx = b * x;
            return Mathf.Sin(c * Mathf.Atan(bx - e * (bx - Mathf.Atan(bx))));
        }

        /// <summary>Effective friction coefficient including load sensitivity and surface grip.</summary>
        public static float EffectiveFriction(TireSpec t, float load, float surfaceGrip)
        {
            float loadRatio = load / Mathf.Max(1f, t.nominalLoad);
            float mu = t.peakFriction * (1f - t.loadSensitivity * (loadRatio - 1f));
            return Mathf.Max(0.1f, mu) * surfaceGrip;
        }

        /// <summary>
        /// Advances the relaxed slip states by dt and returns them.
        /// slipRatioState relaxes toward (wheel slip velocity / |vx|).
        /// slipAngleState is the relaxed tan(slip angle).
        /// Uses implicit integration of the decay term, so it stays stable at any speed.
        /// </summary>
        public static void RelaxSlip(TireSpec t, float vx, float vy, float slipVelocity, float dt,
                                     ref float slipRatioState, ref float slipAngleState)
        {
            float absVx = Mathf.Abs(vx);
            // A small speed floor keeps the states from integrating forever at standstill,
            // so there is no creep. Real tires behave this way too.
            const float vFloor = 0.6f;
            float decayL = (absVx + vFloor) / Mathf.Max(0.05f, t.longRelaxation);
            float decayY = (absVx + vFloor) / Mathf.Max(0.05f, t.latRelaxation);

            slipRatioState = (slipRatioState + dt * slipVelocity / Mathf.Max(0.05f, t.longRelaxation))
                             / (1f + dt * decayL);
            slipAngleState = (slipAngleState + dt * (-vy) / Mathf.Max(0.05f, t.latRelaxation))
                             / (1f + dt * decayY);

            slipRatioState = Mathf.Clamp(slipRatioState, -1.5f, 1.5f);
            slipAngleState = Mathf.Clamp(slipAngleState, -1.5f, 1.5f);
        }

        /// <summary>
        /// Combined-slip tire forces in the tire frame.
        /// fx: positive = forward (traction), fy: positive = toward the tire's +side axis.
        /// </summary>
        public static void ComputeForces(TireSpec t, float load, float surfaceGrip,
                                         float slipRatio, float tanSlipAngle,
                                         out float fx, out float fy, out float mu)
        {
            mu = EffectiveFriction(t, load, surfaceGrip);
            float fmax = mu * load;

            float slipAngle = Mathf.Atan(tanSlipAngle);
            float fx0 = fmax * MagicFormula(slipRatio, t.longB, t.longC, t.longE);
            float fy0 = fmax * MagicFormula(slipAngle, t.latB, t.latC, t.latE);

            // Friction ellipse: scale the combined force so it never exceeds the available grip.
            float mag = Mathf.Sqrt(fx0 * fx0 + fy0 * fy0);
            if (mag > fmax && mag > 1e-3f)
            {
                float s = fmax / mag;
                fx0 *= s;
                fy0 *= s;
            }
            fx = fx0;
            fy = fy0;
        }

        /// <summary>
        /// Self-aligning torque estimate (N·m) for force feedback. The pneumatic trail
        /// shrinks as the tire approaches and passes peak slip, so the steering
        /// goes light at the limit, as it does in a real car.
        /// </summary>
        public static float SelfAligningTorque(TireSpec t, float fy, float tanSlipAngle)
        {
            float alpha = Mathf.Abs(Mathf.Atan(tanSlipAngle));
            float peakAlpha = 1.0f / Mathf.Max(1f, t.latB) * 2.2f;
            float trail = t.pneumaticTrail * Mathf.Clamp01(1f - alpha / Mathf.Max(0.01f, peakAlpha * 1.6f));
            return -fy * trail;
        }
    }
}
