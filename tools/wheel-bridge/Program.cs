using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Windows.Gaming.Input;
using Windows.Gaming.Input.ForceFeedback;

namespace Masar.WheelBridge
{
    /// <summary>
    /// MASAR Wheel Bridge. Usage:
    ///   MasarWheelBridge.exe [ipad-address] [--simulate] [--gain 0.8] [--invert-force] [--test-ffb]
    /// The iPad's address is shown in the game (Input devices &amp; wheel test). It is remembered in
    /// %APPDATA%\MASAR\wheel-bridge.json, so later runs need no arguments.
    /// </summary>
    internal static class Program
    {
        sealed class Config
        {
            public string Ipad { get; set; } = "";
            public double Gain { get; set; } = 1.0;
            public bool InvertForce { get; set; }
        }

        static readonly string ConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MASAR", "wheel-bridge.json");
        static readonly Stopwatch Clock = Stopwatch.StartNew();
        static uint NowMs => (uint)Clock.ElapsedMilliseconds;

        [DllImport("winmm.dll")] static extern uint timeBeginPeriod(uint ms);
        [DllImport("winmm.dll")] static extern uint timeEndPeriod(uint ms);

        // Latest force command from the game (written by the receive thread)
        static Protocol.Force s_Force;
        static long s_ForceAtMs = -10_000;
        static uint s_ForcePackets, s_LastForcePackets;
        static long s_LastForceRateMs;
        static readonly object s_Lock = new object();

        static int Main(string[] args)
        {
            var cfg = LoadConfig();
            bool simulate = args.Contains("--simulate"), testFfb = args.Contains("--test-ffb");
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--gain" && i + 1 < args.Length && double.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var g)) cfg.Gain = Math.Clamp(g, 0, 2);
                if (args[i] == "--invert-force") cfg.InvertForce = true;
                if (!args[i].StartsWith("--") && IPAddress.TryParse(args[i], out _)) cfg.Ipad = args[i];
            }

            Console.WriteLine("MASAR Wheel Bridge");
            Console.WriteLine("------------------");
            if (!IPAddress.TryParse(cfg.Ipad, out var ipad))
            {
                Console.WriteLine("Type the iPad address shown in the game (Input devices & wheel test), e.g. 192.168.1.23:");
                Console.Write("> ");
                var line = Console.ReadLine()?.Trim() ?? "";
                if (!IPAddress.TryParse(line, out ipad)) { Console.WriteLine("Not an IP address."); return 1; }
                cfg.Ipad = line;
            }
            SaveConfig(cfg);

            IWheel wheel = simulate ? new SimulatedWheel() : FindWheel();
            if (wheel == null)
            {
                Console.WriteLine("No racing wheel found. Check that the G923 is plugged into this PC (and that Logitech G HUB sees it),");
                Console.WriteLine("then run the bridge again. To test the connection without a wheel: MasarWheelBridge.exe --simulate");
                return 2;
            }
            Console.WriteLine($"Wheel: {wheel.Name} ({wheel.Kind}), range {wheel.RangeDeg:F0}° lock-to-lock, force feedback: {(wheel.HasFfb ? "yes" : "no")}");
            Console.WriteLine($"Sending to {ipad}:{Protocol.GamePort} at 250 Hz. Force gain {cfg.Gain:F2}{(cfg.InvertForce ? " (inverted)" : "")}. Ctrl+C to stop.");
            if (testFfb) { TestFfb(wheel); return 0; }

            var target = new IPEndPoint(ipad, Protocol.GamePort);
            using var sock = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            sock.Bind(new IPEndPoint(IPAddress.Any, 0));
            // Windows reports "port unreachable" (game not running yet) as a connection reset on the next
            // receive. Those resets pile up and delay the force packets: switch them off (SIO_UDP_CONNRESET).
            try { sock.IOControl(unchecked((int)0x9800000C), new byte[] { 0, 0, 0, 0 }, null); } catch { }
            var stop = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
            var rx = new Thread(() => ReceiveLoop(sock, stop.Token)) { IsBackground = true, Name = "ffb-rx" };
            rx.Start();

            timeBeginPeriod(1);
            try { Run(wheel, sock, target, cfg, stop.Token); }
            finally
            {
                timeEndPeriod(1);
                wheel.StopForces();
                Console.WriteLine();
                Console.WriteLine("Stopped. Forces released.");
            }
            return 0;
        }

        static void Run(IWheel wheel, Socket sock, IPEndPoint target, Config cfg, CancellationToken stop)
        {
            const double Period = 1000.0 / 250.0;
            var buf = new byte[Protocol.StateSize];
            uint seq = 0, sent = 0, lastSent = 0;
            double next = Clock.Elapsed.TotalMilliseconds;
            long lastStatus = 0;
            float lastRtt = float.NaN;
            uint lastEchoGame = 0, lastForceSeq = 0;

            while (!stop.IsCancellationRequested)
            {
                var r = wheel.Read();
                Protocol.Force f; long forceAt;
                lock (s_Lock) { f = s_Force; forceAt = s_ForceAtMs; }
                long now = Clock.ElapsedMilliseconds;
                bool live = now - forceAt < 300;
                if (live && f.Seq != lastForceSeq)   // measure only on fresh packets
                {
                    lastForceSeq = f.Seq;
                    lastEchoGame = f.GameTimeMs;
                    if (f.EchoBridgeTimeMs != 0) lastRtt = NowMs - f.EchoBridgeTimeMs - (uint)(now - forceAt);
                }

                var s = new Protocol.State
                {
                    Seq = ++seq, BridgeTimeMs = NowMs, EchoGameTimeMs = lastEchoGame,
                    WheelDeg = r.WheelDeg, WheelRangeDeg = wheel.RangeDeg,
                    Throttle = r.Throttle, Brake = r.Brake, Clutch = r.Clutch,
                    Buttons = r.Buttons, ShifterGear = r.ShifterGear,
                    Flags = (byte)((wheel.HasClutch ? Protocol.FlagHasClutch : 0) | (wheel.HasFfb ? Protocol.FlagFfb : 0) |
                                   (wheel.Kind == "racing wheel" ? Protocol.FlagRacingWheel : 0) | (wheel is SimulatedWheel ? Protocol.FlagSimulated : 0)),
                };
                Protocol.Write(s, buf);
                try { sock.SendTo(buf, target); sent++; } catch (SocketException) { }

                // Forces: the game's aligning torque + a soft lock at the car's steering lock.
                // Without fresh commands (game paused, Wi-Fi drop) everything is released.
                if (wheel.HasFfb)
                {
                    if (live && f.Enabled)
                    {
                        double c = f.Constant;
                        if (f.SteeringLockDeg > 30f)
                        {
                            double over = Math.Abs(r.WheelDeg) - f.SteeringLockDeg;
                            if (over > 0) c -= Math.Sign(r.WheelDeg) * Math.Min(1.0, over / 12.0);
                        }
                        c *= cfg.Gain;
                        if (cfg.InvertForce) c = -c;
                        wheel.SetForces(Math.Clamp(c, -1, 1), f.Spring * cfg.Gain, f.Damper * cfg.Gain);
                    }
                    else wheel.SetForces(0, 0, 0.05);
                }

                if (now - lastStatus >= 250)
                {
                    float rate = (sent - lastSent) * 1000f / Math.Max(1, now - lastStatus);
                    lastSent = sent; lastStatus = now;
                    uint fp; lock (s_Lock) fp = s_ForcePackets;
                    float frate = (fp - s_LastForcePackets) * 1000f / Math.Max(1, now - s_LastForceRateMs);
                    s_LastForcePackets = fp; s_LastForceRateMs = now;
                    string game = live ? $"game connected, RTT {lastRtt:F0} ms, forces {frate:F0}/s q{sock.Available}, FFB {(f.Enabled ? $"{f.Constant:+0.00;-0.00}" : "off")}" : "waiting for the game...";
                    Console.Write($"\rwheel {r.WheelDeg,7:F1}°  thr {r.Throttle:F2}  brk {r.Brake:F2}  clu {r.Clutch:F2}  btn {r.Buttons:X8}  {rate,4:F0} Hz  |  {game}        ");
                }

                next += Period;
                double wait = next - Clock.Elapsed.TotalMilliseconds;
                if (wait > 1.5) Thread.Sleep((int)(wait - 1));
                while (Clock.Elapsed.TotalMilliseconds < next) Thread.SpinWait(50);
                if (Clock.Elapsed.TotalMilliseconds - next > 50) next = Clock.Elapsed.TotalMilliseconds;   // fell behind: resync
            }
        }

        static void ReceiveLoop(Socket sock, CancellationToken stop)
        {
            var buf = new byte[256];
            EndPoint from = new IPEndPoint(IPAddress.Any, 0);
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    int n = sock.ReceiveFrom(buf, ref from);
                    if (Protocol.TryRead(buf.AsSpan(0, n), out var f))
                        lock (s_Lock) { s_Force = f; s_ForceAtMs = Clock.ElapsedMilliseconds; s_ForcePackets++; }
                }
                catch (SocketException) { if (stop.IsCancellationRequested) return; }
                catch (ObjectDisposedException) { return; }
            }
        }

        static void TestFfb(IWheel wheel)
        {
            if (!wheel.HasFfb) { Console.WriteLine("This wheel reports no force feedback motor."); return; }
            Console.WriteLine("Force test: the rim should turn RIGHT for 1 s, then LEFT for 1 s. If it's the other way round, run with --invert-force.");
            wheel.SetForces(0.35, 0, 0); Thread.Sleep(1000);
            wheel.SetForces(-0.35, 0, 0); Thread.Sleep(1000);
            wheel.StopForces();
            Console.WriteLine("Done.");
        }

        // ------------------------------------------------------------------ wheel discovery

        static IWheel FindWheel()
        {
            // Controllers are enumerated asynchronously after start-up: give Windows a moment
            for (int i = 0; i < 30; i++)
            {
                if (RacingWheel.RacingWheels.Count > 0) return new WgiRacingWheel(RacingWheel.RacingWheels[0]);
                Thread.Sleep(100);
            }
            foreach (var raw in RawGameController.RawGameControllers)
                if (raw.AxisCount >= 3) return new RawWheel(raw);
            return null;
        }

        static Config LoadConfig()
        {
            try { if (File.Exists(ConfigPath)) return JsonSerializer.Deserialize<Config>(File.ReadAllText(ConfigPath)) ?? new Config(); }
            catch { }
            return new Config();
        }

        static void SaveConfig(Config c)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
                File.WriteAllText(ConfigPath, JsonSerializer.Serialize(c, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }
    }

    // ---------------------------------------------------------------------- wheel sources

    struct WheelReading
    {
        public float WheelDeg, Throttle, Brake, Clutch;
        public uint Buttons;
        public sbyte ShifterGear;
    }

    interface IWheel
    {
        string Name { get; }
        string Kind { get; }
        float RangeDeg { get; }
        bool HasClutch { get; }
        bool HasFfb { get; }
        WheelReading Read();
        void SetForces(double constant, double spring, double damper);
        void StopForces();
    }

    /// <summary>Force feedback through Windows.Gaming.Input: one constant force, one spring, one damper.</summary>
    sealed class Ffb
    {
        readonly ForceFeedbackMotor _motor;
        readonly ConstantForceEffect _constant = new ConstantForceEffect();
        readonly ConditionForceEffect _spring = new ConditionForceEffect(ConditionForceEffectKind.Spring);
        readonly ConditionForceEffect _damper = new ConditionForceEffect(ConditionForceEffectKind.Damper);
        bool _ready;
        double _lastSpring = -1, _lastDamper = -1;
        long _lastConstantMs = -1000;
        double _lastConstant = double.NaN;
        static readonly Stopwatch Clock = Stopwatch.StartNew();

        public Ffb(ForceFeedbackMotor motor)
        {
            _motor = motor;
            try
            {
                _constant.SetParameters(Vector3.Zero, TimeSpan.FromMilliseconds(200));
                _spring.SetParameters(Vector3.UnitX, 0, 0, 1, 1, 0, 0);
                _damper.SetParameters(Vector3.UnitX, 0, 0, 1, 1, 0, 0);
                bool ok = _motor.LoadEffectAsync(_constant).AsTask().Result == ForceFeedbackLoadEffectResult.Succeeded;
                ok &= _motor.LoadEffectAsync(_spring).AsTask().Result == ForceFeedbackLoadEffectResult.Succeeded;
                ok &= _motor.LoadEffectAsync(_damper).AsTask().Result == ForceFeedbackLoadEffectResult.Succeeded;
                if (ok) { _constant.Start(); _spring.Start(); _damper.Start(); _ready = true; }
                else Console.WriteLine("Force feedback: the wheel refused the effects (is another game using it?).");
            }
            catch (Exception e) { Console.WriteLine($"Force feedback unavailable: {e.Message}"); }
        }

        public bool Ready => _ready;

        public void Set(double constant, double spring, double damper)
        {
            if (!_ready) return;
            long now = Clock.ElapsedMilliseconds;
            // The constant force carries a short duration and is refreshed: if the bridge stalls, it fades out by itself
            if (Math.Abs(constant - _lastConstant) > 0.004 || now - _lastConstantMs > 50)
            {
                _constant.SetParameters(new Vector3((float)constant, 0, 0), TimeSpan.FromMilliseconds(200));
                _lastConstant = constant; _lastConstantMs = now;
            }
            if (Math.Abs(spring - _lastSpring) > 0.01)
            {
                float k = (float)Math.Clamp(spring, 0, 1);
                _spring.SetParameters(Vector3.UnitX, k, k, 1, 1, 0, 0);
                _lastSpring = spring;
            }
            if (Math.Abs(damper - _lastDamper) > 0.01)
            {
                float k = (float)Math.Clamp(damper, 0, 1);
                _damper.SetParameters(Vector3.UnitX, k, k, 1, 1, 0, 0);
                _lastDamper = damper;
            }
        }

        public void Stop()
        {
            if (!_ready) return;
            try { _motor.StopAllEffects(); } catch { }
        }
    }

    sealed class WgiRacingWheel : IWheel
    {
        readonly RacingWheel _w;
        readonly Ffb _ffb;
        public WgiRacingWheel(RacingWheel w)
        {
            _w = w;
            var raw = RawGameController.FromGameController(w);
            Name = raw?.DisplayName ?? "Racing wheel";
            RangeDeg = (float)(w.MaxWheelAngle > 0 ? w.MaxWheelAngle * 2 : 900);
            HasClutch = w.HasClutch;
            if (w.WheelMotor != null) _ffb = new Ffb(w.WheelMotor);
        }
        public string Name { get; }
        public string Kind => "racing wheel";
        public float RangeDeg { get; }
        public bool HasClutch { get; }
        public bool HasFfb => _ffb != null && _ffb.Ready;

        public WheelReading Read()
        {
            var r = _w.GetCurrentReading();
            return new WheelReading
            {
                WheelDeg = (float)(r.Wheel * RangeDeg * 0.5),
                Throttle = (float)r.Throttle, Brake = (float)r.Brake, Clutch = (float)r.Clutch,
                Buttons = (uint)r.Buttons,
                ShifterGear = _w.HasPatternShifter ? (sbyte)Math.Clamp(r.PatternShifterGear, -1, 7) : Protocol.NoShifter,
            };
        }
        public void SetForces(double c, double s, double d) => _ffb?.Set(c, s, d);
        public void StopForces() => _ffb?.Stop();
    }

    /// <summary>
    /// Fallback for wheels Windows doesn't classify as racing wheels (for example a PlayStation-mode
    /// wheel): axis 0 is the steering, the next axes are pedals. Pedal axes resting at 1.0 are inverted
    /// automatically. Buttons go into the Button1.. bits of the RacingWheelButtons layout.
    /// </summary>
    sealed class RawWheel : IWheel
    {
        readonly RawGameController _c;
        readonly bool[] _buttons;
        readonly GameControllerSwitchPosition[] _switches;
        readonly double[] _axes, _rest;
        readonly Ffb _ffb;
        public RawWheel(RawGameController c)
        {
            _c = c;
            _buttons = new bool[c.ButtonCount];
            _switches = new GameControllerSwitchPosition[c.SwitchCount];
            _axes = new double[c.AxisCount];
            c.GetCurrentReading(_buttons, _switches, _axes);
            _rest = (double[])_axes.Clone();
            Name = $"{c.DisplayName} (VID {c.HardwareVendorId:X4} PID {c.HardwareProductId:X4})";
            HasClutch = c.AxisCount >= 4;
            if (c.ForceFeedbackMotors.Count > 0) _ffb = new Ffb(c.ForceFeedbackMotors[0]);
        }
        public string Name { get; }
        public string Kind => "generic controller";
        public float RangeDeg => 900f;
        public bool HasClutch { get; }
        public bool HasFfb => _ffb != null && _ffb.Ready;

        float Pedal(int i) => i < _axes.Length ? (float)(_rest[i] > 0.5 ? 1 - _axes[i] : _axes[i]) : 0f;

        public WheelReading Read()
        {
            _c.GetCurrentReading(_buttons, _switches, _axes);
            uint bits = 0;
            for (int i = 0; i < _buttons.Length && i < 16; i++) if (_buttons[i]) bits |= 64u << i;
            return new WheelReading
            {
                WheelDeg = (float)((_axes[0] - 0.5) * 2 * RangeDeg * 0.5),
                Throttle = Pedal(1), Brake = Pedal(2), Clutch = Pedal(3),
                Buttons = bits, ShifterGear = Protocol.NoShifter,
            };
        }
        public void SetForces(double c, double s, double d) => _ffb?.Set(c, s, d);
        public void StopForces() => _ffb?.Stop();
    }

    /// <summary>--simulate: a slow steering sweep and pedal cycle, to test the network path without a wheel.</summary>
    sealed class SimulatedWheel : IWheel
    {
        readonly Stopwatch _t = Stopwatch.StartNew();
        public string Name => "Simulated wheel";
        public string Kind => "simulated";
        public float RangeDeg => 900f;
        public bool HasClutch => true;
        public bool HasFfb => false;
        public WheelReading Read()
        {
            double t = _t.Elapsed.TotalSeconds;
            return new WheelReading
            {
                WheelDeg = (float)(90 * Math.Sin(t * 0.8)),
                Throttle = (float)Math.Max(0, Math.Sin(t * 0.3)),
                Brake = (float)Math.Max(0, -Math.Sin(t * 0.3)),
                Clutch = 0, Buttons = 0, ShifterGear = Protocol.NoShifter,
            };
        }
        public void SetForces(double c, double s, double d) { }
        public void StopForces() { }
    }
}
