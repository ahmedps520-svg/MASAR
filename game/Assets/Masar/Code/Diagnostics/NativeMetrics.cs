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

        public static bool Available => true;
        public static int ThermalState => masar_thermal_state();
        public static long AvailableMemoryBytes => masar_available_memory();
        public static long FootprintBytes => masar_phys_footprint();
        public static int LowPowerMode => masar_low_power_mode();
#else
        public static bool Available => false;
        public static int ThermalState => -1;
        public static long AvailableMemoryBytes => -1;
        public static long FootprintBytes => -1;
        public static int LowPowerMode => -1;
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
