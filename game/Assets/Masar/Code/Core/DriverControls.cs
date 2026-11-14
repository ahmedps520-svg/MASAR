namespace Masar.Core
{
    /// <summary>Which kind of device produced the current driver input.</summary>
    public enum InputDeviceKind
    {
        None,
        Keyboard,
        Gamepad,
        Wheel,
        Touch,
        Bridge
    }

    /// <summary>Requested gear-selector position (automatic) or shift request (manual).</summary>
    public enum GearSelector
    {
        Park,
        Reverse,
        Neutral,
        Drive
    }

    /// <summary>
    /// Continuous driver inputs, already calibrated and normalized.
    /// Produced by the input layer once per frame and consumed by the vehicle at
    /// the fixed physics rate. Plain data: no device or engine references.
    /// </summary>
    public struct DriverControls
    {
        /// <summary>-1 = full left lock, +1 = full right lock.</summary>
        public float Steer;
        /// <summary>True if Steer comes from an absolute device (wheel), so no speed-sensitive assist is applied.</summary>
        public bool SteerIsAbsolute;
        /// <summary>0..1</summary>
        public float Throttle;
        /// <summary>0..1</summary>
        public float Brake;
        /// <summary>0..1, 1 = pedal fully pressed (clutch disengaged).</summary>
        public float Clutch;
        /// <summary>True if a physical clutch pedal is present. Otherwise the auto-clutch is used.</summary>
        public bool HasClutchPedal;
        /// <summary>0..1</summary>
        public float Handbrake;
        public InputDeviceKind Source;

        public static DriverControls Neutral => new DriverControls();
    }

    /// <summary>
    /// Discrete, edge-triggered actions for one frame (true only on the frame of the press).
    /// </summary>
    public struct DriverActions
    {
        public bool ShiftUp;
        public bool ShiftDown;
        public bool SelectDrive;
        public bool SelectReverse;
        public bool SelectNeutral;
        public bool SelectPark;
        public bool ToggleTransmissionMode;
        public bool ToggleHeadlights;
        public bool IndicatorLeft;
        public bool IndicatorRight;
        public bool Hazards;
        public bool Horn;           // level, not edge: true while held
        public bool Ignition;
        public bool NextCamera;
        public bool PreviousCamera;
        public bool ResetVehicle;
        public bool Pause;
        public bool TogglePerfOverlay;
    }

    /// <summary>Implemented by the input layer. Read by the vehicle and the camera.</summary>
    public interface IDriverInputSource
    {
        DriverControls Controls { get; }
        DriverActions Actions { get; }
        /// <summary>Free-camera / look input: x,y look delta (deg) and x,y,z move axes in -1..1.</summary>
        UnityEngine.Vector2 Look { get; }
        UnityEngine.Vector3 Move { get; }
    }
}
