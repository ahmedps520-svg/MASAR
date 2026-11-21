using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Masar.Input
{
    /// <summary>
    /// Receives a racing wheel streamed by the MASAR Wheel Bridge (tools/wheel-bridge) over the
    /// local network, and sends force feedback back to it. This is how a Logitech G923 reaches
    /// the iPad: iPadOS exposes no public API for racing wheels (KNOWN_ISSUES L1), so the wheel
    /// plugs into a Windows PC and the bridge forwards it.
    ///
    /// Wire format: see tools/wheel-bridge/Protocol.cs (both sides must match).
    /// Threading: one background thread receives the 250 Hz wheel stream and sends forces at 100 Hz.
    /// The main thread only reads the latest snapshot.
    /// </summary>
    public static class NetworkWheel
    {
        public const int Port = 7780;
        public const byte FlagHasClutch = 1, FlagFfb = 2, FlagRacingWheel = 4, FlagSimulated = 8;
        public const sbyte NoShifter = 127;

        // RacingWheelButtons bit layout (Windows.Gaming.Input), forwarded as-is by the bridge
        public const uint BtnPreviousGear = 1, BtnNextGear = 2, BtnDPadUp = 4, BtnDPadDown = 8, BtnDPadLeft = 16, BtnDPadRight = 32;
        public static uint Button(int n) => 64u << (n - 1);   // Button1..Button16

        public struct State
        {
            public float WheelDeg, WheelRangeDeg, Throttle, Brake, Clutch;
            public uint Buttons;
            public sbyte ShifterGear;
            public byte Flags;
            public bool HasClutch => (Flags & FlagHasClutch) != 0;
            public bool HasFfb => (Flags & FlagFfb) != 0;
            public bool Simulated => (Flags & FlagSimulated) != 0;
        }

        private static Thread s_Thread;
        private static volatile bool s_Run;
        private static Socket s_Socket;
        private static readonly object s_Lock = new object();
        private static readonly Stopwatch s_Clock = Stopwatch.StartNew();
        private static State s_State;
        private static long s_StateAtTicks = -1;
        private static uint s_Packets, s_LastSeq, s_LastBridgeTime, s_Lost;
        private static EndPoint s_Bridge;
        private static float s_RttMs = float.NaN;
        private static float s_Rate;
        // force command (main thread -> network thread)
        private static float s_Constant, s_Spring, s_Damper, s_LockDeg;
        private static bool s_ForceEnabled;

        public static string LocalAddress { get; private set; } = "";
        public static string Error { get; private set; }
        public static bool Running => s_Run;

        public static void Start()
        {
            if (s_Run) return;
            try
            {
                LocalAddress = FindLocalAddress();
                s_Socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp) { ReceiveTimeout = 10 };
                s_Socket.Bind(new IPEndPoint(IPAddress.Any, Port));
                // Windows only: stop "port unreachable" from surfacing as connection resets (SIO_UDP_CONNRESET)
                try { s_Socket.IOControl(unchecked((int)0x9800000C), new byte[] { 0, 0, 0, 0 }, null); } catch { }
                PokeLocalNetwork();
                s_Run = true;
                s_Thread = new Thread(Loop) { IsBackground = true, Name = "MASAR wheel bridge" };
                s_Thread.Start();
                Error = null;
            }
            catch (Exception e)
            {
                Error = e.Message;
                UnityEngine.Debug.LogWarning($"[MASAR] Wheel bridge receiver could not start: {e.Message}");
                try { s_Socket?.Close(); } catch { }
                s_Socket = null;
            }
        }

        public static void Stop()
        {
            s_Run = false;
            try { s_Socket?.Close(); } catch { }
            s_Socket = null;
        }

        /// <summary>Latest wheel snapshot and its age in seconds. False if nothing was ever received.</summary>
        public static bool TryGetState(out State state, out float ageSeconds)
        {
            lock (s_Lock)
            {
                state = s_State;
                if (s_StateAtTicks < 0) { ageSeconds = float.PositiveInfinity; return false; }
                ageSeconds = (float)((s_Clock.ElapsedTicks - s_StateAtTicks) / (double)Stopwatch.Frequency);
                return true;
            }
        }

        public static bool Connected => TryGetState(out _, out float age) && age < 0.5f;
        public static float RoundTripMs { get { lock (s_Lock) return s_RttMs; } }
        public static float PacketRate { get { lock (s_Lock) return s_Rate; } }
        public static uint LostPackets { get { lock (s_Lock) return s_Lost; } }
        public static string BridgeAddress { get { lock (s_Lock) return s_Bridge?.ToString() ?? ""; } }

        /// <summary>
        /// Force feedback for the bridge. constant: -1..1, positive pushes the rim right.
        /// lockDeg: the car's center-to-lock steering-wheel angle (the bridge adds a soft stop beyond it).
        /// </summary>
        public static void SetForce(float constant, float spring, float damper, float lockDeg, bool enabled)
        {
            lock (s_Lock)
            {
                s_Constant = constant; s_Spring = spring; s_Damper = damper; s_LockDeg = lockDeg; s_ForceEnabled = enabled;
            }
        }

        private static uint NowMs => (uint)s_Clock.ElapsedMilliseconds;

        private static void Loop()
        {
            var buf = new byte[512];
            var outBuf = new byte[36];
            EndPoint from = new IPEndPoint(IPAddress.Any, 0);
            long lastForceMs = 0, rateWindowStart = s_Clock.ElapsedMilliseconds;
            uint rateCount = 0, outSeq = 0;
            while (s_Run)
            {
                var sock = s_Socket;
                if (sock == null) break;
                try
                {
                    int n = sock.ReceiveFrom(buf, ref from);
                    if (n >= 48 && buf[0] == 'M' && buf[1] == 'W' && buf[2] == 'B' && buf[3] == '1')
                    {
                        uint seq = BitConverter.ToUInt32(buf, 4);
                        var st = new State
                        {
                            WheelDeg = BitConverter.ToSingle(buf, 12),
                            WheelRangeDeg = BitConverter.ToSingle(buf, 16),
                            Throttle = Clamp01(BitConverter.ToSingle(buf, 20)),
                            Brake = Clamp01(BitConverter.ToSingle(buf, 24)),
                            Clutch = Clamp01(BitConverter.ToSingle(buf, 28)),
                            Buttons = BitConverter.ToUInt32(buf, 32),
                            ShifterGear = (sbyte)buf[36],
                            Flags = buf[37],
                        };
                        uint echo = BitConverter.ToUInt32(buf, 40);
                        lock (s_Lock)
                        {
                            if (s_Packets > 0 && seq > s_LastSeq + 1 && seq - s_LastSeq < 10000) s_Lost += seq - s_LastSeq - 1;
                            if (s_Packets == 0 || seq > s_LastSeq || s_LastSeq - seq > 10000)   // drop reordered packets
                            {
                                s_State = st;
                                s_StateAtTicks = s_Clock.ElapsedTicks;
                                s_LastSeq = seq;
                            }
                            s_LastBridgeTime = BitConverter.ToUInt32(buf, 8);
                            s_Bridge = from;
                            s_Packets++;
                            if (echo != 0) s_RttMs = NowMs - echo;
                        }
                        rateCount++;
                    }
                }
                catch (SocketException) { /* timeout: fall through to the force send */ }
                catch (ObjectDisposedException) { break; }

                long now = s_Clock.ElapsedMilliseconds;
                if (now - rateWindowStart >= 1000)
                {
                    lock (s_Lock) s_Rate = rateCount * 1000f / (now - rateWindowStart);
                    rateCount = 0; rateWindowStart = now;
                }
                if (now - lastForceMs >= 10)
                {
                    lastForceMs = now;
                    EndPoint bridge; float c, s, d, l; bool en; uint echoBridge;
                    lock (s_Lock) { bridge = s_Bridge; c = s_Constant; s = s_Spring; d = s_Damper; l = s_LockDeg; en = s_ForceEnabled; echoBridge = s_LastBridgeTime; }
                    if (bridge != null && Connected)
                    {
                        WriteForce(outBuf, ++outSeq, NowMs, c, s, d, l, echoBridge, en);
                        try { sock.SendTo(outBuf, bridge); } catch (SocketException) { } catch (ObjectDisposedException) { break; }
                    }
                }
            }
        }

        private static void WriteForce(byte[] b, uint seq, uint time, float c, float s, float d, float lockDeg, uint echo, bool enabled)
        {
            b[0] = (byte)'M'; b[1] = (byte)'W'; b[2] = (byte)'F'; b[3] = (byte)'1';
            Put(b, 4, seq); Put(b, 8, time);
            Put(b, 12, c); Put(b, 16, s); Put(b, 20, d); Put(b, 24, lockDeg);
            Put(b, 28, echo);
            b[32] = (byte)(enabled ? 1 : 0); b[33] = 0; b[34] = 0; b[35] = 0;
        }

        private static void Put(byte[] b, int o, uint v) { b[o] = (byte)v; b[o + 1] = (byte)(v >> 8); b[o + 2] = (byte)(v >> 16); b[o + 3] = (byte)(v >> 24); }
        private static void Put(byte[] b, int o, float f) => Put(b, o, (uint)BitConverter.SingleToInt32Bits(f));
        private static float Clamp01(float v) => float.IsNaN(v) ? 0f : v < 0f ? 0f : v > 1f ? 1f : v;

        /// <summary>The address other devices on the Wi-Fi reach this device at (no packet is sent).</summary>
        private static string FindLocalAddress()
        {
            try
            {
                using (var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
                {
                    s.Connect(new IPEndPoint(IPAddress.Parse("192.0.2.1"), 9));   // TEST-NET address: routing lookup only
                    return ((IPEndPoint)s.LocalEndPoint).Address.ToString();
                }
            }
            catch { return ""; }
        }

        /// <summary>
        /// iPadOS asks for Local Network permission the first time an app talks to the LAN, and
        /// drops local traffic until it is granted. One harmless datagram to the router brings the
        /// prompt up at launch rather than leaving the wheel silently blocked.
        /// </summary>
        private static void PokeLocalNetwork()
        {
            try
            {
                if (!IPAddress.TryParse(LocalAddress, out var ip)) return;
                var b = ip.GetAddressBytes();
                b[3] = 1;
                s_Socket.SendTo(new byte[] { 0 }, new IPEndPoint(new IPAddress(b), 9));
            }
            catch { }
        }
    }
}
