using System.Threading;
using Masar.Input;
using UnityEditor;
using UnityEngine;

namespace Masar.EditorTools
{
    /// <summary>
    /// End-to-end check of the wheel bridge link on this PC: run
    ///   build\wheel-bridge\MasarWheelBridge.exe 127.0.0.1 --simulate
    /// then  -executeMethod Masar.EditorTools.WheelBridgeSelfTest.Run
    /// It receives the stream with the game's own NetworkWheel code, sends force commands back,
    /// and reports rate, round trip, packet loss and the values received.
    /// </summary>
    public static class WheelBridgeSelfTest
    {
        [MenuItem("MASAR/Review/Wheel Bridge Self-Test")]
        public static void Run()
        {
            NetworkWheel.Start();
            float minDeg = float.MaxValue, maxDeg = float.MinValue;
            int samples = 0;
            NetworkWheel.State last = default;
            for (int i = 0; i < 80; i++)   // 8 s
            {
                Thread.Sleep(100);
                NetworkWheel.SetForce(0.25f, 0f, 0.2f, 450f, true);
                if (NetworkWheel.TryGetState(out var s, out float age) && age < 0.5f)
                {
                    samples++;
                    last = s;
                    minDeg = Mathf.Min(minDeg, s.WheelDeg); maxDeg = Mathf.Max(maxDeg, s.WheelDeg);
                }
            }
            bool ok = samples > 50 && NetworkWheel.PacketRate > 150f;
            Debug.Log($"[MASAR] Wheel bridge self-test: {(ok ? "PASS" : "FAIL")}  connected samples {samples}/80, " +
                      $"rate {NetworkWheel.PacketRate:F0} Hz, round trip {NetworkWheel.RoundTripMs:F1} ms, lost {NetworkWheel.LostPackets}, " +
                      $"rim range seen {minDeg:F1}..{maxDeg:F1}°, last throttle {last.Throttle:F2} brake {last.Brake:F2}, " +
                      $"simulated {last.Simulated}, bridge {NetworkWheel.BridgeAddress}, local address {NetworkWheel.LocalAddress}");
            NetworkWheel.Stop();
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }
    }
}
