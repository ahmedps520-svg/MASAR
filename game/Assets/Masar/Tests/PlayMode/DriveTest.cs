using System.Collections;
using Masar.Core;
using Masar.Vehicles;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Masar.Tests
{
    /// <summary>
    /// End-to-end drive test in the real TestBlock scene, with PhysX, colliders, kerbs and road
    /// meshes. A scripted driver accelerates, brakes and turns. This checks that the whole
    /// vehicle stack works in the engine, not just the isolated model.
    /// </summary>
    public class DriveTest
    {
        private sealed class ScriptedDriver : IDriverInputSource
        {
            public DriverControls Controls { get; set; }
            public DriverActions Actions { get; set; }
            public Vector2 Look => Vector2.zero;
            public Vector3 Move => Vector3.zero;
        }

        private static float Yaw(Transform t) => Mathf.Atan2(t.forward.x, t.forward.z) * Mathf.Rad2Deg;

        [UnityTest]
        public IEnumerator Car_Accelerates_Brakes_And_Turns_InTestBlock()
        {
            SceneManager.LoadScene("TestBlock");
            yield return null;
            yield return null;
            var car = Object.FindFirstObjectByType<VehicleController>();
            Assert.That(car, Is.Not.Null, "TestBlock must contain the vehicle");
            var driver = new ScriptedDriver();
            car.Input = driver;   // replaces the live input router for this test

            // 1. Settle on the suspension
            float t = 0f;
            while (t < 1.5f) { t += Time.deltaTime; yield return null; }
            Assert.That(car.transform.up.y, Is.GreaterThan(0.95f), "car should rest upright");
            Assert.That(car.Telemetry.GroundedWheels, Is.EqualTo(4), "all four wheels on the road");

            // 2. Select Drive (one frame), then full throttle for 8 s
            driver.Actions = new DriverActions { SelectDrive = true };
            yield return null;
            driver.Actions = default;
            driver.Controls = new DriverControls { Throttle = 1f, Source = InputDeviceKind.Keyboard };
            Vector3 start = car.transform.position;
            float t60 = -1f, minUp = 1f;
            int frames = 0; float frameTime = 0f;
            t = 0f;
            while (t < 8f)
            {
                t += Time.deltaTime; frames++; frameTime += Time.unscaledDeltaTime;
                if (t60 < 0f && car.Telemetry.ForwardSpeedMs * 3.6f >= 60f) t60 = t;
                minUp = Mathf.Min(minUp, car.transform.up.y);
                yield return null;
            }
            float vAfter8 = car.Telemetry.ForwardSpeedMs * 3.6f;
            float driftZ = Mathf.Abs(car.transform.position.z - start.z);
            Debug.Log($"[MASAR drive test] 0-60 km/h: {t60:F2} s | speed after 8 s: {vAfter8:F0} km/h | gear {car.Telemetry.Gear} | " +
                      $"rpm {car.Telemetry.EngineRpm:F0} | lateral drift {driftZ:F2} m | editor avg frame {frameTime / frames * 1000f:F1} ms");
            Assert.That(vAfter8, Is.GreaterThan(60f), "should exceed 60 km/h within 8 s at full throttle");
            Assert.That(driftZ, Is.LessThan(2.5f), "should drive straight with neutral steering");

            // 3. Full braking to a stop
            driver.Controls = new DriverControls { Brake = 1f };
            Vector3 brakeStart = car.transform.position;
            float vBrake = car.Telemetry.ForwardSpeedMs * 3.6f;
            t = 0f;
            while (car.Telemetry.SpeedMs > 0.3f && t < 10f) { t += Time.deltaTime; minUp = Mathf.Min(minUp, car.transform.up.y); yield return null; }
            float brakeDist = Vector3.Distance(brakeStart, car.transform.position);
            Debug.Log($"[MASAR drive test] braking from {vBrake:F0} km/h: {brakeDist:F1} m in {t:F2} s");
            Assert.That(car.Telemetry.SpeedMs, Is.LessThan(0.5f), "car should stop");
            float ideal = (vBrake / 3.6f) * (vBrake / 3.6f) / (2f * 9.81f);   // perfect mu=1 stop
            Assert.That(brakeDist, Is.InRange(ideal * 0.85f, ideal * 1.8f), $"braking distance plausible (mu=1 ideal {ideal:F1} m)");

            // 4. Low-speed turn in the open parking lot
            car.Teleport(new Vector3(-63f, 0.55f, -63f), Quaternion.Euler(0f, 90f, 0f));
            driver.Controls = new DriverControls();
            t = 0f;
            while (t < 1f) { t += Time.deltaTime; yield return null; }
            float yaw0 = Yaw(car.transform);
            driver.Controls = new DriverControls { Throttle = 0.3f, Steer = -0.6f };
            t = 0f;
            while (t < 3.5f) { t += Time.deltaTime; minUp = Mathf.Min(minUp, car.transform.up.y); yield return null; }
            float dYaw = Mathf.DeltaAngle(yaw0, Yaw(car.transform));
            Debug.Log($"[MASAR drive test] left turn: heading change {dYaw:F0} deg at {car.Telemetry.SpeedMs * 3.6f:F0} km/h | min upright {minUp:F3}");
            Assert.That(dYaw, Is.LessThan(-30f), "steering left should turn the car left (negative yaw)");
            Assert.That(minUp, Is.GreaterThan(0.85f), "car must stay upright through the whole test");
        }
    }
}
