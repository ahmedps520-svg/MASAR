using Masar.Vehicles;
using NUnit.Framework;
using UnityEngine;

namespace Masar.Tests
{
    /// <summary>
    /// Checks of the synthesised car sound (M3-2): the engine's pitch must follow the firing
    /// frequency (rpm x cylinders / 120), and tyre squeal must only sound while the tyres slide.
    /// </summary>
    public class VehicleAudioTests
    {
        const int Sr = 24000;

        static float[] Render(VehicleSoundSynth s, float seconds)
        {
            var buf = new float[Mathf.RoundToInt(seconds * Sr)];
            s.Render(buf, 1);
            return buf;
        }

        static float Rms(float[] x) { double a = 0; foreach (var v in x) a += v * v; return Mathf.Sqrt((float)(a / x.Length)); }

        /// <summary>Fundamental from the normalised autocorrelation: the first strong peak.</summary>
        static float Pitch(float[] x)
        {
            int minLag = Sr / 1500, maxLag = Sr / 40;
            double r0 = 0; for (int i = 0; i < x.Length - maxLag; i++) r0 += x[i] * x[i];
            var r = new float[maxLag + 2];
            for (int lag = minLag; lag <= maxLag + 1; lag++)
            {
                double a = 0; for (int i = 0; i < x.Length - maxLag - 1; i++) a += x[i] * x[i + lag];
                r[lag] = (float)(a / r0);
            }
            for (int lag = minLag + 1; lag <= maxLag; lag++)
                if (r[lag] > 0.6f && r[lag] >= r[lag - 1] && r[lag] >= r[lag + 1])
                {
                    // parabolic interpolation around the peak
                    float a = r[lag - 1], b = r[lag], c = r[lag + 1];
                    float d = (a - c) / (2f * (a - 2f * b + c));
                    return Sr / (lag + (float.IsNaN(d) ? 0f : d));
                }
            return -1f;
        }

        [TestCase(4, 3000f)]
        [TestCase(4, 6000f)]
        [TestCase(12, 1200f)]
        [TestCase(12, 3000f)]
        public void EnginePitch_FollowsFiringFrequency(int cylinders, float rpm)
        {
            var s = new VehicleSoundSynth(Sr) { Cylinders = cylinders, Rpm = rpm, Load = 0.6f, Throttle = 0.6f, Roughness = 0f };
            Render(s, 0.4f);   // let the smoothed parameters settle
            float f = Pitch(Render(s, 0.5f));
            float expected = rpm * cylinders / 120f;
            Debug.Log($"[MASAR test] engine {cylinders} cyl at {rpm:F0} rpm: pitch {f:F1} Hz, firing frequency {expected:F1} Hz");
            Assert.That(f, Is.InRange(expected * 0.95f, expected * 1.05f));
        }

        [Test]
        public void TyreSqueal_OnlyWhileSliding()
        {
            var s = new VehicleSoundSynth(Sr) { EngineRunning = false, Rpm = 0f, SpeedMs = 0f, TyreSlip = 0f };
            Render(s, 0.5f);
            float quiet = Rms(Render(s, 0.5f));
            s.TyreSlip = 0.8f;
            Render(s, 0.2f);
            float squeal = Rms(Render(s, 0.5f));
            Debug.Log($"[MASAR test] tyre sound: rms {quiet:F5} gripping, {squeal:F4} sliding");
            Assert.That(quiet, Is.LessThan(0.001f));
            Assert.That(squeal, Is.GreaterThan(0.02f));
        }

        [Test]
        public void Output_StaysInRange_AtRedlineFullLoad()
        {
            var s = new VehicleSoundSynth(Sr) { Cylinders = 12, Rpm = 8700f, Load = 1f, Throttle = 1f, Turbo = true, SpeedMs = 80f, TyreSlip = 1f, Volume = 1f };
            var x = Render(s, 1f);
            float peak = 0f; foreach (var v in x) { Assert.That(float.IsNaN(v), Is.False); peak = Mathf.Max(peak, Mathf.Abs(v)); }
            Debug.Log($"[MASAR test] redline full load: peak {peak:F3}, rms {Rms(x):F3}");
            Assert.That(peak, Is.LessThanOrEqualTo(1f));
            Assert.That(Rms(x), Is.GreaterThan(0.05f));
        }
    }
}
