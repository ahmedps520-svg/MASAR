using Masar.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace Masar.Input
{
    /// <summary>
    /// Samples every supported input device each frame, picks the most recently used
    /// one, applies calibration and exposes clean <see cref="DriverControls"/>.
    /// Nothing outside this assembly knows which device is in use.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class InputRouter : MonoBehaviour, IDriverInputSource
    {
        public InputCalibration keyboardCalibration = new InputCalibration();
        public InputCalibration gamepadCalibration = new InputCalibration { steerDeadzone = 0.08f, steerExponent = 1.4f };
        public InputCalibration wheelCalibration = new InputCalibration { steerDeadzone = 0f };
        public WheelMapping wheelMapping = new WheelMapping();

        public DriverControls Controls { get; private set; }
        public DriverActions Actions { get; private set; }
        public Vector2 Look { get; private set; }
        public Vector3 Move { get; private set; }

        /// <summary>When true, driving controls are released and movement keys/sticks drive the free camera.</summary>
        public bool FreeCameraCapture { get; set; }
        /// <summary>Set by the game while a menu is open, so driving input is ignored.</summary>
        public bool MenuOpen { get; set; }

        public InputDeviceKind ActiveKind { get; private set; } = InputDeviceKind.Keyboard;
        public Joystick ActiveWheel { get; private set; }

        private float _kbSteer;
        private float _lastKeyboard = -100f, _lastGamepad = -100f, _lastWheel = -100f;
        private float _wheelBaselineSteer = float.NaN;

        private void Update()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            var pad = Gamepad.current;
            ActiveWheel = FindWheel();

            DetectActivity(kb, pad, ActiveWheel);

            var actions = new DriverActions();
            Vector2 look = Vector2.zero;
            Vector3 move = Vector3.zero;

            // ---------------- keyboard + mouse (always read for actions)
            if (kb != null)
            {
                actions.ShiftUp |= kb.eKey.wasPressedThisFrame;
                actions.ShiftDown |= kb.qKey.wasPressedThisFrame;
                actions.ToggleTransmissionMode |= kb.mKey.wasPressedThisFrame;
                actions.ToggleHeadlights |= kb.lKey.wasPressedThisFrame;
                actions.Ignition |= kb.iKey.wasPressedThisFrame;
                actions.NextCamera |= kb.cKey.wasPressedThisFrame;
                actions.PreviousCamera |= kb.vKey.wasPressedThisFrame;
                actions.ResetVehicle |= kb.backspaceKey.wasPressedThisFrame;
                actions.Pause |= kb.escapeKey.wasPressedThisFrame;
                actions.TogglePerfOverlay |= kb.f3Key.wasPressedThisFrame;

                move.x += Axis(kb.rightArrowKey.isPressed, kb.leftArrowKey.isPressed);
                move.z += Axis(kb.upArrowKey.isPressed, kb.downArrowKey.isPressed);
                move.y += Axis(kb.pageUpKey.isPressed, kb.pageDownKey.isPressed);
                if (FreeCameraCapture)
                {
                    move.x += Axis(kb.dKey.isPressed, kb.aKey.isPressed);
                    move.z += Axis(kb.wKey.isPressed, kb.sKey.isPressed);
                }
            }
            if (mouse != null && mouse.rightButton.isPressed)
                look += mouse.delta.ReadValue() * 0.12f;

            // ---------------- gamepad
            if (pad != null)
            {
                actions.ShiftUp |= pad.rightShoulder.wasPressedThisFrame;
                actions.ShiftDown |= pad.leftShoulder.wasPressedThisFrame;
                actions.NextCamera |= pad.buttonNorth.wasPressedThisFrame;
                actions.ToggleHeadlights |= pad.buttonWest.wasPressedThisFrame;
                actions.ResetVehicle |= pad.selectButton.wasPressedThisFrame;
                actions.Pause |= pad.startButton.wasPressedThisFrame;
                actions.TogglePerfOverlay |= pad.dpad.up.wasPressedThisFrame;
                actions.ToggleTransmissionMode |= pad.dpad.down.wasPressedThisFrame;
                look += pad.rightStick.ReadValue() * (180f * Time.unscaledDeltaTime);
                if (FreeCameraCapture)
                {
                    var ls = pad.leftStick.ReadValue();
                    move += new Vector3(ls.x, pad.rightTrigger.ReadValue() - pad.leftTrigger.ReadValue(), ls.y);
                }
            }

            // ---------------- wheel buttons
            if (ActiveWheel != null)
            {
                actions.ShiftUp |= Pressed(ActiveWheel, wheelMapping.shiftUpButton);
                actions.ShiftDown |= Pressed(ActiveWheel, wheelMapping.shiftDownButton);
                actions.NextCamera |= Pressed(ActiveWheel, wheelMapping.nextCameraButton);
                actions.Pause |= Pressed(ActiveWheel, wheelMapping.pauseButton);
            }

            // ---------------- touch requests
            if (TouchInputState.RequestShiftUp) { actions.ShiftUp = true; TouchInputState.RequestShiftUp = false; }
            if (TouchInputState.RequestShiftDown) { actions.ShiftDown = true; TouchInputState.RequestShiftDown = false; }
            if (TouchInputState.RequestNextCamera) { actions.NextCamera = true; TouchInputState.RequestNextCamera = false; }
            if (TouchInputState.RequestPause) { actions.Pause = true; TouchInputState.RequestPause = false; }
            if (TouchInputState.RequestReset) { actions.ResetVehicle = true; TouchInputState.RequestReset = false; }
            if (TouchInputState.RequestHeadlights) { actions.ToggleHeadlights = true; TouchInputState.RequestHeadlights = false; }
            look += TouchInputState.LookDelta;
            TouchInputState.LookDelta = Vector2.zero;

            // ---------------- continuous driving controls from the active device
            var c = new DriverControls();
            switch (ActiveKind)
            {
                case InputDeviceKind.Gamepad when pad != null: c = ReadGamepad(pad); break;
                case InputDeviceKind.Wheel when ActiveWheel != null: c = ReadWheel(ActiveWheel); break;
                case InputDeviceKind.Touch: c = ReadTouch(); break;
                default: c = ReadKeyboard(kb); break;
            }

            if (MenuOpen || FreeCameraCapture)
            {
                c.Throttle = 0f;
                c.Steer = FreeCameraCapture ? c.Steer * 0f : c.Steer;
                c.Brake = MenuOpen ? 0f : c.Brake;
            }
            if (MenuOpen)
            {
                // Menus own the buttons while open. Only Pause passes through, so the menu can be closed.
                bool pause = actions.Pause;
                actions = new DriverActions { Pause = pause };
            }

            Controls = c;
            Actions = actions;
            Look = look;
            Move = Vector3.ClampMagnitude(move, 1.5f);
        }

        // ------------------------------------------------------------------ devices

        private DriverControls ReadKeyboard(Keyboard kb)
        {
            var c = new DriverControls { Source = InputDeviceKind.Keyboard };
            if (kb == null) return c;
            float target = Axis(kb.dKey.isPressed || kb.rightArrowKey.isPressed && !FreeCameraCapture,
                                kb.aKey.isPressed || kb.leftArrowKey.isPressed && !FreeCameraCapture);
            var cal = keyboardCalibration;
            float rate = Mathf.Abs(target) > 0.01f && Mathf.Sign(target) == Mathf.Sign(_kbSteer) || Mathf.Abs(_kbSteer) < 0.01f
                ? cal.digitalSteerRate : cal.digitalReturnRate;
            _kbSteer = Mathf.MoveTowards(_kbSteer, target, rate * Time.deltaTime);
            c.Steer = cal.ShapeSteer(_kbSteer);
            c.Throttle = kb.wKey.isPressed || (kb.upArrowKey.isPressed && !FreeCameraCapture) ? 1f : 0f;
            c.Brake = kb.sKey.isPressed || (kb.downArrowKey.isPressed && !FreeCameraCapture) ? 1f : 0f;
            c.Handbrake = kb.spaceKey.isPressed ? 1f : 0f;
            return c;
        }

        private DriverControls ReadGamepad(Gamepad pad)
        {
            var cal = gamepadCalibration;
            return new DriverControls
            {
                Source = InputDeviceKind.Gamepad,
                Steer = cal.ShapeSteer(pad.leftStick.ReadValue().x),
                Throttle = Deadzone(pad.rightTrigger.ReadValue(), cal.pedalDeadzone),
                Brake = Deadzone(pad.leftTrigger.ReadValue(), cal.pedalDeadzone),
                Handbrake = pad.buttonSouth.isPressed ? 1f : 0f,
            };
        }

        private DriverControls ReadWheel(Joystick wheel)
        {
            var cal = wheelCalibration;
            var m = wheelMapping;
            float steerRaw = ReadAxis(wheel, m.steerAxis);
            var c = new DriverControls
            {
                Source = InputDeviceKind.Wheel,
                SteerIsAbsolute = true,
                Steer = cal.ShapeSteer(steerRaw),
                Throttle = cal.ShapePedal(ReadAxis(wheel, m.throttleAxis), cal.throttleRawMin, cal.throttleRawMax, cal.invertThrottle),
                Brake = cal.ShapePedal(ReadAxis(wheel, m.brakeAxis), cal.brakeRawMin, cal.brakeRawMax, cal.invertBrake),
                HasClutchPedal = m.hasClutch && HasControl(wheel, m.clutchAxis),
                Handbrake = ReadButton(wheel, m.handbrakeButton) ? 1f : 0f,
            };
            if (c.HasClutchPedal)
                c.Clutch = cal.ShapePedal(ReadAxis(wheel, m.clutchAxis), cal.clutchRawMin, cal.clutchRawMax, cal.invertClutch);
            return c;
        }

        private static DriverControls ReadTouch()
        {
            return new DriverControls
            {
                Source = InputDeviceKind.Touch,
                Steer = Mathf.Clamp(TouchInputState.Steer, -1f, 1f),
                Throttle = Mathf.Clamp01(TouchInputState.Throttle),
                Brake = Mathf.Clamp01(TouchInputState.Brake),
                Handbrake = Mathf.Clamp01(TouchInputState.Handbrake),
            };
        }

        // ------------------------------------------------------------------ activity

        private void DetectActivity(Keyboard kb, Gamepad pad, Joystick wheel)
        {
            float now = Time.unscaledTime;
            if (kb != null && kb.anyKey.isPressed) _lastKeyboard = now;
            if (pad != null)
            {
                if (pad.leftStick.ReadValue().sqrMagnitude > 0.04f || pad.rightTrigger.ReadValue() > 0.1f ||
                    pad.leftTrigger.ReadValue() > 0.1f || pad.buttonSouth.isPressed)
                    _lastGamepad = now;
            }
            if (wheel != null)
            {
                float steer = ReadAxis(wheel, wheelMapping.steerAxis);
                if (float.IsNaN(_wheelBaselineSteer)) _wheelBaselineSteer = steer;
                if (Mathf.Abs(steer - _wheelBaselineSteer) > 0.03f) { _lastWheel = now; _wheelBaselineSteer = steer; }
            }
            else _wheelBaselineSteer = float.NaN;

            float best = _lastKeyboard;
            var kind = InputDeviceKind.Keyboard;
            if (_lastGamepad > best) { best = _lastGamepad; kind = InputDeviceKind.Gamepad; }
            if (_lastWheel > best) { best = _lastWheel; kind = InputDeviceKind.Wheel; }
            if (TouchInputState.LastActivityTime > best) { kind = InputDeviceKind.Touch; }
            ActiveKind = kind;
        }

        /// <summary>The first connected joystick-class device that isn't a gamepad (wheels on desktop show up this way).</summary>
        private static Joystick FindWheel()
        {
            var all = Joystick.all;
            for (int i = 0; i < all.Count; i++)
                if (all[i] != null && all[i].added) return all[i];
            return null;
        }

        // ------------------------------------------------------------------ helpers

        private static float Axis(bool pos, bool neg) => (pos ? 1f : 0f) - (neg ? 1f : 0f);
        private static float Deadzone(float v, float dz) => v < dz ? 0f : (v - dz) / (1f - dz);

        public static bool HasControl(InputDevice d, string path) =>
            !string.IsNullOrEmpty(path) && d.TryGetChildControl(path) != null;

        public static float ReadAxis(InputDevice d, string path)
        {
            if (string.IsNullOrEmpty(path)) return 0f;
            var ctrl = d.TryGetChildControl(path);
            if (ctrl is AxisControl a) return a.ReadValue();
            if (ctrl is ButtonControl b) return b.ReadValue();
            return 0f;
        }

        private static bool ReadButton(InputDevice d, string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            return d.TryGetChildControl(path) is ButtonControl b && b.isPressed;
        }

        private static bool Pressed(InputDevice d, string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            return d.TryGetChildControl(path) is ButtonControl b && b.wasPressedThisFrame;
        }
    }
}
