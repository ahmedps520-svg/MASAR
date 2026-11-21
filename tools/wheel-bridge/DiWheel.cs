using System.Runtime.InteropServices;
using Vortice.DirectInput;

namespace Masar.WheelBridge
{
    /// <summary>
    /// The wheel through DirectInput: the path Logitech's G HUB driver supports for input and force
    /// feedback. Unlike Windows.Gaming.Input it keeps reading while the bridge window is in the
    /// background (cooperative level Background | Exclusive on a hidden window of our own).
    ///
    /// Axis layout of Logitech wheels in DirectInput (G29 / G923 / G920, "combined pedals" off in G HUB):
    /// X = wheel, Y = throttle, Rz = brake, Slider 0 = clutch; pedals read full scale when released.
    /// The layout can be overridden in wheel-bridge.json ("Axes": "X,Y,Rz,S0").
    /// </summary>
    sealed class DiWheel : IWheel
    {
        const int Infinite = -1, NoTrigger = -1;

        readonly IDirectInput8 _di;
        readonly IDirectInputDevice8 _dev;
        readonly IntPtr _hwnd;
        readonly string[] _axisNames;
        readonly float[] _pedalRest = new float[3];
        readonly bool _logitechPs;
        IDirectInputEffect _constant, _spring, _damper;
        double _lastConstant = double.NaN, _lastSpring = -1, _lastDamper = -1;
        bool _haveShifter, _acquired, _reporting, _pedalsKnown;
        int _forceSign = 1;
        WheelReading _last;

        public string Name { get; }
        public string Kind => "racing wheel";
        public float RangeDeg { get; }
        public bool HasClutch => true;
        public bool HasFfb => _constant != null;
        public ushort VendorId { get; }
        public ushort ProductId { get; }

        public static DiWheel TryCreate(float rangeDeg, string axes, out string why)
        {
            why = "";
            IDirectInput8 di;
            try { di = DInput.DirectInput8Create(); }
            catch (Exception e) { why = $"DirectInput unavailable: {e.Message}"; return null; }

            DeviceInstance best = null; int bestScore = -1;
            foreach (var d in di.GetDevices(DeviceClass.GameControl, DeviceEnumerationFlags.AttachedOnly))
            {
                ProductIds(d, out var vid, out var pid);
                int score = d.Type == DeviceType.Driving ? 4 : 0;
                if (vid == 0x046D) score += 2;
                if (d.ForceFeedbackDriverGuid != Guid.Empty) score += 1;
                if (score > bestScore) { best = d; bestScore = score; }
            }
            if (best == null || bestScore < 2) { why = "no wheel in DirectInput"; di.Dispose(); return null; }
            try { return new DiWheel(di, best, rangeDeg, axes); }
            catch (Exception e) { why = $"DirectInput could not open {best.ProductName}: {e.Message}"; di.Dispose(); return null; }
        }

        /// <summary>A Logitech PlayStation wheel still in its PlayStation compatibility mode (G923 PS: C267, G29: C294).</summary>
        public static bool PsCompatModePresent()
        {
            try
            {
                using var di = DInput.DirectInput8Create();
                foreach (var d in di.GetDevices(DeviceClass.GameControl, DeviceEnumerationFlags.AttachedOnly))
                {
                    ProductIds(d, out var vid, out var pid);
                    if (vid == 0x046D && (pid == 0xC267 || pid == 0xC294)) return true;
                }
            }
            catch { }
            return false;
        }

        public string Legend => _logitechPs
            ? "Buttons: paddles = gear up/down, Cross = camera, Square = handbrake, Circle = lights, Options = pause, D-pad up = performance overlay, D-pad down = auto/manual"
            : "Buttons: first two = gear up/down, then camera, handbrake, lights, pause";

        static void ProductIds(DeviceInstance d, out ushort vid, out ushort pid)
        {
            // DirectInput's product GUID carries the USB IDs in its first four bytes: PID high word, VID low word
            var b = d.ProductGuid.ToByteArray();
            vid = (ushort)(b[0] | b[1] << 8);
            pid = (ushort)(b[2] | b[3] << 8);
        }

        DiWheel(IDirectInput8 di, DeviceInstance inst, float rangeDeg, string axes)
        {
            _di = di;
            ProductIds(inst, out var vid, out var pid);
            VendorId = vid; ProductId = pid;
            _logitechPs = vid == 0x046D && (pid == 0xC266 || pid == 0xC24F || pid == 0xC268);   // G923 PS, G29, Pro PS
            Name = $"{inst.ProductName.Trim()} (VID {vid:X4} PID {pid:X4}, DirectInput)";
            RangeDeg = rangeDeg > 0 ? rangeDeg : 900f;
            _axisNames = (string.IsNullOrWhiteSpace(axes) ? "X,Y,Rz,S0" : axes).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

            _hwnd = HiddenWindow.Create();
            _dev = di.CreateDevice(inst.InstanceGuid);
            _dev.SetCooperativeLevel(_hwnd, CooperativeLevel.Background | CooperativeLevel.Exclusive);
            _dev.SetDataFormat<RawJoystickState>();
            try { _dev.Properties.AutoCenter = false; } catch { }   // the game supplies all centering forces
            Acquire();

            // Logitech: released pedals read full scale, and a positive DirectInput force turns the rim LEFT
            // (measured on the G923 with --test-ffb, 2026-11-21). Other wheels: learnt from the first real report.
            if (vid == 0x046D) { _pedalsKnown = true; for (int i = 0; i < 3; i++) _pedalRest[i] = 1f; _forceSign = -1; }

            if (inst.ForceFeedbackDriverGuid != Guid.Empty)
            {
                try { CreateEffects(); }
                catch (Exception e) { Console.WriteLine($"Force feedback unavailable: {e.Message}"); _constant = _spring = _damper = null; }
            }
        }

        void Acquire()
        {
            try { _dev.Acquire(); _acquired = true; } catch { _acquired = false; }
        }

        JoystickState ReadState()
        {
            if (!_acquired) Acquire();
            if (!_acquired) return null;
            try { _dev.Poll(); return _dev.GetCurrentJoystickState(); }
            catch { _acquired = false; return null; }
        }

        static float Norm(int raw) => Math.Clamp(raw / 65535f, 0f, 1f);

        float Axis(JoystickState s, int index)
        {
            if (index >= _axisNames.Length) return 0f;
            return _axisNames[index].ToUpperInvariant() switch
            {
                "X" => Norm(s.X), "Y" => Norm(s.Y), "Z" => Norm(s.Z),
                "RX" => Norm(s.RotationX), "RY" => Norm(s.RotationY), "RZ" => Norm(s.RotationZ),
                "S0" => s.Sliders.Length > 0 ? Norm(s.Sliders[0]) : 0f,
                "S1" => s.Sliders.Length > 1 ? Norm(s.Sliders[1]) : 0f,
                _ => 0f,
            };
        }

        float Pedal(JoystickState s, int i)
        {
            float v = Axis(s, i + 1);
            return _pedalRest[i] > 0.5f ? 1f - v : v;
        }

        public WheelReading Read()
        {
            var s = ReadState();
            if (s == null)
            {
                // Device lost (unplugged, or G HUB re-enumerating it): hold the wheel angle, release the pedals
                _last.Throttle = _last.Brake = _last.Clutch = 0; _last.Buttons = 0;
                return _last;
            }
            // Until the wheel sends its first report, DirectInput returns the mid-scale default on every axis:
            // report a centered wheel with released pedals rather than half-pressed ones
            if (!_reporting)
            {
                _reporting = s.X != 32767 || s.Y != 32767 || s.RotationZ != 32767 || s.Sliders[0] != 32767 ||
                             s.PointOfViewControllers[0] != -1 || s.Buttons.Any(x => x);
                if (!_reporting) { _last = new WheelReading { ShifterGear = Protocol.NoShifter }; return _last; }
                if (!_pedalsKnown)
                {
                    for (int i = 0; i < 3; i++) _pedalRest[i] = Axis(s, i + 1) > 0.5f ? 1f : 0f;
                    _pedalsKnown = true;
                }
            }
            var r = new WheelReading
            {
                WheelDeg = (Axis(s, 0) - 0.5f) * RangeDeg,
                Throttle = Pedal(s, 0), Brake = Pedal(s, 1), Clutch = Pedal(s, 2),
                Buttons = MapButtons(s, out var gear),
                ShifterGear = gear,
            };
            _last = r;
            return r;
        }

        /// <summary>
        /// Converts the wheel's buttons into the Windows.Gaming.Input RacingWheelButtons layout the game reads:
        /// PreviousGear 1, NextGear 2, DPad up/down/left/right 4/8/16/32, Button1.. = 64 &lt;&lt; (n-1).
        /// </summary>
        uint MapButtons(JoystickState s, out sbyte gear)
        {
            var b = s.Buttons;
            bool B(int i) => i < b.Length && b[i];
            uint bits = 0;
            gear = Protocol.NoShifter;

            int pov = s.PointOfViewControllers.Length > 0 ? s.PointOfViewControllers[0] : -1;
            if (pov >= 0 && pov < 36000)
            {
                if (pov >= 31500 || pov <= 4500) bits |= Protocol.BtnDPadUp;
                if (pov >= 4500 && pov <= 13500) bits |= Protocol.BtnDPadRight;
                if (pov >= 13500 && pov <= 22500) bits |= Protocol.BtnDPadDown;
                if (pov >= 22500 && pov <= 31500) bits |= Protocol.BtnDPadLeft;
            }

            if (_logitechPs)
            {
                // G923 / G29 PlayStation layout: 0 Cross, 1 Square, 2 Circle, 3 Triangle, 4 right paddle,
                // 5 left paddle, 6 R2, 7 L2, 8 Share, 9 Options, 10 R3, 11 L3, 12-17 shifter 1-6,
                // 18 shifter reverse, 19 plus, 20 minus, 21 dial right, 22 dial left, 23 enter, 24 PS
                if (B(4)) bits |= Protocol.BtnNextGear;
                if (B(5)) bits |= Protocol.BtnPreviousGear;
                int[] order = { 0, 1, 2, 9, 3, 6, 7, 8, 10, 11, 19, 20, 21, 22, 23, 24 };   // -> Button1..Button16
                for (int n = 0; n < order.Length; n++) if (B(order[n])) bits |= Protocol.Button(n + 1);
                for (int g = 0; g < 7; g++)
                    if (B(12 + g)) { _haveShifter = true; gear = (sbyte)(g == 6 ? -1 : g + 1); }
                if (_haveShifter && gear == Protocol.NoShifter) gear = 0;
            }
            else
            {
                // Unknown wheel: buttons in order, first two as paddles
                if (B(0)) bits |= Protocol.BtnNextGear;
                if (B(1)) bits |= Protocol.BtnPreviousGear;
                for (int i = 2; i < b.Length && i < 18; i++) if (b[i]) bits |= Protocol.Button(i - 1);
            }
            return bits;
        }

        // ------------------------------------------------------------------ force feedback

        EffectParameters Params(TypeSpecificParameters specific)
        {
            var p = new EffectParameters
            {
                Flags = EffectFlags.Cartesian | EffectFlags.ObjectOffsets,
                Duration = Infinite,
                SamplePeriod = 0,
                Gain = 10000,
                TriggerButton = NoTrigger,
                TriggerRepeatInterval = 0,
                StartDelay = 0,
                Parameters = specific,
            };
            p.SetAxes(new[] { 0 /* DIJOFS_X */ }, new[] { 1 });
            return p;
        }

        static ConditionSet Condition(double k)
        {
            int c = (int)Math.Round(Math.Clamp(k, 0, 1) * 10000);
            return new ConditionSet
            {
                Conditions = new[] { new Vortice.DirectInput.Condition { Offset = 0, PositiveCoefficient = c, NegativeCoefficient = c, PositiveSaturation = 10000, NegativeSaturation = 10000, DeadBand = 0 } },
            };
        }

        void CreateEffects()
        {
            _constant = _dev.CreateEffect(EffectGuid.ConstantForce, Params(new ConstantForce { Magnitude = 0 }));
            _spring = _dev.CreateEffect(EffectGuid.Spring, Params(Condition(0)));
            _damper = _dev.CreateEffect(EffectGuid.Damper, Params(Condition(0)));
            _constant.Start(1, EffectPlayFlags.None);
            _spring.Start(1, EffectPlayFlags.None);
            _damper.Start(1, EffectPlayFlags.None);
        }

        public void SetForces(double constant, double spring, double damper)
        {
            if (_constant == null || !_acquired) return;
            constant *= _forceSign;
            try
            {
                if (double.IsNaN(_lastConstant) || Math.Abs(constant - _lastConstant) > 0.002)
                {
                    _constant.SetParameters(Params(new ConstantForce { Magnitude = (int)Math.Round(Math.Clamp(constant, -1, 1) * 10000) }), EffectParameterFlags.TypeSpecificParameters);
                    _lastConstant = constant;
                }
                if (Math.Abs(spring - _lastSpring) > 0.01)
                {
                    _spring.SetParameters(Params(Condition(spring)), EffectParameterFlags.TypeSpecificParameters);
                    _lastSpring = spring;
                }
                if (Math.Abs(damper - _lastDamper) > 0.01)
                {
                    _damper.SetParameters(Params(Condition(damper)), EffectParameterFlags.TypeSpecificParameters);
                    _lastDamper = damper;
                }
            }
            catch
            {
                // Lost the device (it re-acquires on the next read); effects are re-sent once values change
                _lastConstant = double.NaN; _lastSpring = _lastDamper = -1;
            }
        }

        public void StopForces()
        {
            try
            {
                _constant?.Stop(); _spring?.Stop(); _damper?.Stop();
                _dev?.SendForceFeedbackCommand(ForceFeedbackCommand.StopAll);
                _dev?.Unacquire();
            }
            catch { }
        }

        /// <summary>For --probe: every DirectInput game controller and its live state.</summary>
        public static void Probe(int seconds, Func<long> nowMs)
        {
            using var di = DInput.DirectInput8Create();
            var devs = di.GetDevices(DeviceClass.GameControl, DeviceEnumerationFlags.AttachedOnly).ToList();
            Console.WriteLine($"DirectInput game controllers: {devs.Count}");
            var hwnd = HiddenWindow.Create();
            var open = new List<(string label, IDirectInputDevice8 dev)>();
            for (int i = 0; i < devs.Count; i++)
            {
                var d = devs[i];
                ProductIds(d, out var vid, out var pid);
                Console.WriteLine($"  DI[{i}] '{d.ProductName.Trim()}' type {d.Type}/{d.Subtype} VID {vid:X4} PID {pid:X4} ffb {(d.ForceFeedbackDriverGuid != Guid.Empty)}");
                try
                {
                    var dev = di.CreateDevice(d.InstanceGuid);
                    dev.SetCooperativeLevel(hwnd, CooperativeLevel.Background | CooperativeLevel.NonExclusive);
                    dev.SetDataFormat<RawJoystickState>();
                    dev.Acquire();
                    var caps = dev.Capabilities;
                    Console.WriteLine($"         axes {caps.AxeCount} buttons {caps.ButtonCount} povs {caps.PovCount}");
                    open.Add(($"DI[{i}]", dev));
                }
                catch (Exception e) { Console.WriteLine($"         could not open: {e.Message}"); }
            }
            var prev = new string[open.Count];
            long end = nowMs() + seconds * 1000L;
            while (nowMs() < end)
            {
                for (int i = 0; i < open.Count; i++)
                {
                    JoystickState s;
                    try { open[i].dev.Poll(); s = open[i].dev.GetCurrentJoystickState(); } catch { continue; }
                    var pressed = string.Join(",", Enumerable.Range(0, s.Buttons.Length).Where(k => s.Buttons[k]));
                    var line = $"X {s.X,5} Y {s.Y,5} Z {s.Z,5} Rx {s.RotationX,5} Ry {s.RotationY,5} Rz {s.RotationZ,5} S0 {s.Sliders[0],5} S1 {s.Sliders[1],5} pov {s.PointOfViewControllers[0],5} buttons [{pressed}]";
                    var key = $"{s.X / 600} {s.Y / 600} {s.Z / 600} {s.RotationX / 600} {s.RotationY / 600} {s.RotationZ / 600} {s.Sliders[0] / 600} {s.Sliders[1] / 600} {s.PointOfViewControllers[0]} {pressed}";
                    if (key == prev[i]) continue;
                    prev[i] = key;
                    Console.WriteLine($"{nowMs(),7} {open[i].label} {line}");
                }
                Thread.Sleep(20);
            }
            foreach (var o in open) { try { o.dev.Unacquire(); o.dev.Dispose(); } catch { } }
        }
    }

    /// <summary>DirectInput's exclusive/background mode needs a top-level window of our own; it is never shown.</summary>
    static class HiddenWindow
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr CreateWindowExW(int exStyle, string cls, string name, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
        static IntPtr s_Hwnd;
        public static IntPtr Create()
        {
            if (s_Hwnd == IntPtr.Zero) s_Hwnd = CreateWindowExW(0, "STATIC", "MASAR Wheel Bridge", 0x80000000u /* WS_POPUP */, 0, 0, 1, 1, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            return s_Hwnd;
        }
    }
}
