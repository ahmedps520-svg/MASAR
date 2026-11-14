using System.Collections.Generic;

namespace Masar.Core
{
    /// <summary>
    /// Counts live simulation entities, so diagnostics can report real numbers
    /// instead of guesses. Entities register in OnEnable and unregister in OnDisable.
    /// </summary>
    public static class SimRegistry
    {
        private static readonly HashSet<object> s_Vehicles = new HashSet<object>();
        private static readonly HashSet<object> s_Pedestrians = new HashSet<object>();

        public static int VehicleCount => s_Vehicles.Count;
        public static int PedestrianCount => s_Pedestrians.Count;

        public static void RegisterVehicle(object v) => s_Vehicles.Add(v);
        public static void UnregisterVehicle(object v) => s_Vehicles.Remove(v);
        public static void RegisterPedestrian(object p) => s_Pedestrians.Add(p);
        public static void UnregisterPedestrian(object p) => s_Pedestrians.Remove(p);
    }
}
