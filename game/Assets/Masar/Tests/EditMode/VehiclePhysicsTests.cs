using Masar.Core;
using Masar.Vehicles;
using NUnit.Framework;
using UnityEngine;

namespace Masar.Tests
{
    /// <summary>
    /// Physics regression tests on the pure-C# vehicle model (no PhysX, no rendering).
    /// A straight-line point-mass harness moves the chassis and feeds wheel contacts back,
    /// so the tire, drivetrain and brake behavior can be checked against plausible real-world ranges.
    /// The measured values are printed to the test log and copied into PERFORMANCE_REPORT.md.
    /// </summary>
    public class VehiclePhysicsTests
    {
        private const float Dt = 1f / 120f;
        private const int Substeps = 4;

        /// <summary>Simple longitudinal harness: static axle loads, flat dry road, aero drag.</summary>
        private sealed class Harness
        {
            public readonly VehicleDefinition Def;
            public readonly VehicleSimulation Sim;
            public float V;          // m/s
            public float X;          // m
            private readonly WheelContact[] _c = new WheelContact[4];
            private readonly WheelOutput[] _o = new WheelOutput[4];

            public Harness()
            {
                Def = VehicleDefinition.CreateDefault();
                Def.body.massKg = 1480f;
                Sim = new VehicleSimulation(Def);
            }

            public void Step(DriverControls ctl)
            {
                var b = Def.body;
                float g = 9.81f * b.massKg;
                float frontShare = 0.5f + b.centerOfMass.z / b.wheelbase;   // static weight distribution
                for (int i = 0; i < 4; i++)
                {
                    float load = (i < 2 ? frontShare : 1f - frontShare) * g * 0.5f;
                    var susp = Sim.Suspension(i);
                    _c[i] = new WheelContact
                    {
                        Grounded = true,
                        Compression = load / susp.springRate,
                        CompressionVelocity = 0f,
                        Vx = V,
                        Vy = 0f,
                        SurfaceGrip = 1f
                    };
                }
                Sim.UpdateSteering(ctl, V);
                Sim.Step(Dt, Substeps, ctl, V, _c, _o);
                float fx = 0f;
                for (int i = 0; i < 4; i++) fx += _o[i].Fx;
                float drag = 0.5f * 1.2f * b.dragCoefficient * b.frontalArea * V * Mathf.Abs(V);
                V += (fx - drag) / b.massKg * Dt;
                X += V * Dt;
            }
        }

        [Test]
        public void Tire_ZeroSlip_GivesZeroForce()
        {
            var t = new TireSpec();
            TireModel.ComputeForces(t, 4000f, 1f, 0f, 0f, out float fx, out float fy, out _);
            Assert.That(Mathf.Abs(fx), Is.LessThan(1e-3f));
            Assert.That(Mathf.Abs(fy), Is.LessThan(1e-3f));
        }

        [Test]
        public void Tire_CombinedForce_NeverExceedsFrictionLimit()
        {
            var t = new TireSpec();
            for (float k = -1f; k <= 1f; k += 0.05f)
                for (float a = -1f; a <= 1f; a += 0.05f)
                {
                    TireModel.ComputeForces(t, 4000f, 1f, k, a, out float fx, out float fy, out float mu);
                    float mag = Mathf.Sqrt(fx * fx + fy * fy);
                    Assert.That(mag, Is.LessThanOrEqualTo(mu * 4000f * 1.0001f), $"slip {k}, tanAlpha {a}");
                }
        }

        [Test]
        public void Engine_Idles_StablyInPark()
        {
            var h = new Harness();
            var ctl = new DriverControls();
            for (int i = 0; i < 120 * 10; i++) h.Step(ctl);
            float rpm = h.Sim.Drivetrain.EngineRpm;
            Debug.Log($"[MASAR test] idle rpm after 10 s in Park: {rpm:F0}");
            Assert.That(float.IsNaN(rpm), Is.False);
            Assert.That(rpm, Is.InRange(h.Def.engine.idleRpm - 200f, h.Def.engine.idleRpm + 250f));
            Assert.That(Mathf.Abs(h.V), Is.LessThan(0.05f), "car must not move in Park");
        }

        [Test]
        public void FullThrottle_ZeroTo100_IsPlausibleForA1500kgSedan()
        {
            var h = new Harness();
            h.Sim.Drivetrain.SetSelector(GearSelector.Drive, 0f);
            var ctl = new DriverControls { Throttle = 1f };
            float t = 0f, t100 = -1f, maxSpeed = 0f;
            while (t < 40f)
            {
                h.Step(ctl);
                t += Dt;
                Assert.That(float.IsNaN(h.V) || float.IsInfinity(h.V), Is.False, $"NaN at t={t:F2}");
                if (t100 < 0f && h.V >= 100f / 3.6f) t100 = t;
                maxSpeed = Mathf.Max(maxSpeed, h.V);
            }
            Debug.Log($"[MASAR test] 0-100 km/h: {t100:F2} s | speed after 40 s: {h.V * 3.6f:F0} km/h | gear {h.Sim.Drivetrain.Gear} | rpm {h.Sim.Drivetrain.EngineRpm:F0}");
            // Reference: 2.5 L NA sedans of this class run 0-100 km/h in about 8-10 s
            Assert.That(t100, Is.InRange(6.5f, 13f));
            Assert.That(h.Sim.Drivetrain.Gear, Is.GreaterThanOrEqualTo(4), "automatic should have upshifted");
        }

        [Test]
        public void Braking_From100_StopsInPlausibleDistance()
        {
            var h = new Harness();
            h.Sim.Drivetrain.SetSelector(GearSelector.Neutral, 0f);
            h.V = 100f / 3.6f;
            // Spin the wheels up to road speed so the test starts rolling, not skidding
            for (int i = 0; i < 4; i++) h.Sim.Wheels[i].Omega = h.V / h.Sim.Tire(i).radius;
            var ctl = new DriverControls { Brake = 1f };
            float t = 0f;
            float x0 = h.X;
            while (h.V > 0.1f && t < 15f) { h.Step(ctl); t += Dt; }
            float dist = h.X - x0;
            Debug.Log($"[MASAR test] 100-0 km/h braking: {dist:F1} m in {t:F2} s (ABS {(h.Def.brakes.abs ? "on" : "off")})");
            // Modern sedans on dry asphalt: about 35-45 m. Allow a band for the simple model.
            Assert.That(dist, Is.InRange(30f, 55f));
        }

        [Test]
        public void Coasting_AtHighSpeed_StaysStable()
        {
            var h = new Harness();
            h.Sim.Drivetrain.SetSelector(GearSelector.Neutral, 0f);
            h.V = 45f;
            for (int i = 0; i < 4; i++) h.Sim.Wheels[i].Omega = h.V / h.Sim.Tire(i).radius;
            var ctl = new DriverControls();
            for (int i = 0; i < 120 * 5; i++)
            {
                h.Step(ctl);
                for (int w = 0; w < 4; w++)
                {
                    float rollSpeed = h.Sim.Wheels[w].Omega * h.Sim.Tire(w).radius;
                    Assert.That(Mathf.Abs(rollSpeed - h.V), Is.LessThan(1.0f), $"wheel {w} slip drifted at step {i}");
                }
            }
            Assert.That(h.V, Is.InRange(35f, 45f), "drag + rolling resistance should slow the car a little, not much");
        }
    }
}
