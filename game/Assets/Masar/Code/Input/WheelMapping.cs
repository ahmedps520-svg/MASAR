using System;

namespace Masar.Input
{
    /// <summary>
    /// Which HID controls on a generic joystick-class device map to wheel functions.
    /// Paths are Input System control paths relative to the device, for example
    /// "stick/x", "z", "rz" or "slider".
    ///
    /// IMPORTANT: The default pedal axes below are UNVERIFIED for the Logitech G923,
    /// because the wheel was not connected during Phase 0. The Wheel Test panel lists every
    /// axis live, so the real mapping can be confirmed and saved.
    /// </summary>
    [Serializable]
    public class WheelMapping
    {
        public string steerAxis = "stick/x";
        public string throttleAxis = "z";
        public string brakeAxis = "rz";
        public string clutchAxis = "slider";
        public string shiftUpButton = "button5";
        public string shiftDownButton = "button6";
        public string handbrakeButton = "button1";
        public string nextCameraButton = "button4";
        public string pauseButton = "button10";
        public bool hasClutch = true;
    }
}
