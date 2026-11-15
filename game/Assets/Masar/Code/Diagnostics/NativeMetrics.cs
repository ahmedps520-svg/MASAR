using System.Runtime.InteropServices;

namespace Masar.Diagnostics
{
    /// <summary>
    /// OS metrics that Unity does not expose. iOS uses the MasarNative plugin
    /// (Assets/Masar/Plugins/iOS/MasarNative.mm). Every other platform reports
    /// "unavailable" (-1). We never estimate.
    /// </summary>
    public static class NativeMetrics
    {
#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern int masar_thermal_state();
        [DllImport("__Internal")] private static extern long masar_available_memory();
        [DllImport("__Internal")] private static extern long masar_phys_footprint();
        [DllImport("__Internal")] private static extern int masar_low_power_mode();
        [DllImport("__Internal")] private static extern string masar_controllers_json();

        public static bool Available => true;
        public static int ThermalState => masar_thermal_state();
        public static long AvailableMemoryBytes => masar_available_memory();
        public static long FootprintBytes => masar_phys_footprint();
        public static int LowPowerMode => masar_low_power_mode();
        /// <summary>
        /// JSON from the GameController framework: every controller iPadOS exposes (vendor, category,
        /// element names), whether the GCRacingWheel class exists, and keyboard/mouse presence.
        /// This is the on-device G923 test.
        /// </summary>
        public static string ControllersJson => masar_controllers_json();
#else
        public static bool Available => false;
        public static int ThermalState => -1;
        public static long AvailableMemoryBytes => -1;
        public static long FootprintBytes => -1;
        public static int LowPowerMode => -1;
        public static string ControllersJson => null;
#endif

        public static string ThermalStateName(int s)
        {
            switch (s)
            {
                case 0: return "nominal";
                case 1: return "fair";
                case 2: return "SERIOUS";
                case 3: return "CRITICAL";
                default: return "n/a";
            }
        }
    }
}
