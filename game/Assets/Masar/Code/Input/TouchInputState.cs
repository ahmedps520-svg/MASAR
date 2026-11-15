using UnityEngine;

namespace Masar.Input
{
    /// <summary>
    /// Shared state written by the on-screen touch controls (UI layer) and read by
    /// <see cref="InputRouter"/>. Keeps the input layer independent of UI code.
    /// </summary>
    public static class TouchInputState
    {
        public static float Steer;
        public static float Throttle;
        public static float Brake;
        public static float Handbrake;
        public static Vector2 LookDelta;

        // Edge-triggered requests (consumed once by the router)
        public static bool RequestShiftUp;
        public static bool RequestShiftDown;
        public static bool RequestNextCamera;
        public static bool RequestPause;
        public static bool RequestReset;
        public static bool RequestHeadlights;
        public static bool RequestSelectPark, RequestSelectReverse, RequestSelectNeutral, RequestSelectDrive;

        /// <summary>Time.unscaledTime of the last touch interaction.</summary>
        public static float LastActivityTime = -100f;

        public static void MarkActive() => LastActivityTime = Time.unscaledTime;

        public static void ClearContinuous()
        {
            Steer = Throttle = Brake = Handbrake = 0f;
            LookDelta = Vector2.zero;
        }
    }
}
