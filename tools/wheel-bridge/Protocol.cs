using System.Buffers.Binary;

namespace Masar.WheelBridge
{
    /// <summary>
    /// Wire format shared with the game (game/Assets/Masar/Code/Input/WheelBridgeProtocol.cs).
    /// Little-endian UDP datagrams. Keep both files identical in layout.
    ///
    /// Bridge -> iPad, port 7780, 48 bytes, sent at 250 Hz:
    ///   0  "MWB1"          4  uint seq           8  uint bridgeTimeMs
    ///   12 float wheelDeg (+ = right)            16 float wheelRangeDeg (lock-to-lock)
    ///   20 float throttle 0..1   24 float brake 0..1   28 float clutch 0..1
    ///   32 uint buttons (Windows.Gaming.Input RacingWheelButtons layout)
    ///   36 sbyte shifterGear (-1 R, 0 N, 1..7, 127 = no shifter)   37 byte flags
    ///   38 ushort reserved       40 uint echoGameTimeMs   44 uint reserved
    ///
    /// iPad -> bridge (to the bridge's source port), 36 bytes, about 100 Hz:
    ///   0  "MWF1"   4 uint seq   8 uint gameTimeMs
    ///   12 float constant force -1..1 (+ pushes the rim right)
    ///   16 float spring 0..1    20 float damper 0..1
    ///   24 float steeringLockDeg (the car's center-to-lock rim angle; soft lock beyond it)
    ///   28 uint echoBridgeTimeMs   32 byte flags (bit0 forces enabled)   33..35 reserved
    /// </summary>
    public static class Protocol
    {
        public const int GamePort = 7780;
        public const int StateSize = 48, ForceSize = 36;
        public const byte FlagHasClutch = 1, FlagFfb = 2, FlagRacingWheel = 4, FlagSimulated = 8;
        public const sbyte NoShifter = 127;

        public struct State
        {
            public uint Seq, BridgeTimeMs, Buttons, EchoGameTimeMs;
            public float WheelDeg, WheelRangeDeg, Throttle, Brake, Clutch;
            public sbyte ShifterGear;
            public byte Flags;
        }

        public struct Force
        {
            public uint Seq, GameTimeMs, EchoBridgeTimeMs;
            public float Constant, Spring, Damper, SteeringLockDeg;
            public bool Enabled;
        }

        public static void Write(in State s, Span<byte> b)
        {
            b[0] = (byte)'M'; b[1] = (byte)'W'; b[2] = (byte)'B'; b[3] = (byte)'1';
            BinaryPrimitives.WriteUInt32LittleEndian(b[4..], s.Seq);
            BinaryPrimitives.WriteUInt32LittleEndian(b[8..], s.BridgeTimeMs);
            BinaryPrimitives.WriteSingleLittleEndian(b[12..], s.WheelDeg);
            BinaryPrimitives.WriteSingleLittleEndian(b[16..], s.WheelRangeDeg);
            BinaryPrimitives.WriteSingleLittleEndian(b[20..], s.Throttle);
            BinaryPrimitives.WriteSingleLittleEndian(b[24..], s.Brake);
            BinaryPrimitives.WriteSingleLittleEndian(b[28..], s.Clutch);
            BinaryPrimitives.WriteUInt32LittleEndian(b[32..], s.Buttons);
            b[36] = (byte)s.ShifterGear;
            b[37] = s.Flags;
            b[38] = 0; b[39] = 0;
            BinaryPrimitives.WriteUInt32LittleEndian(b[40..], s.EchoGameTimeMs);
            BinaryPrimitives.WriteUInt32LittleEndian(b[44..], 0);
        }

        public static bool TryRead(ReadOnlySpan<byte> b, out Force f)
        {
            f = default;
            if (b.Length < ForceSize || b[0] != 'M' || b[1] != 'W' || b[2] != 'F' || b[3] != '1') return false;
            f.Seq = BinaryPrimitives.ReadUInt32LittleEndian(b[4..]);
            f.GameTimeMs = BinaryPrimitives.ReadUInt32LittleEndian(b[8..]);
            f.Constant = Clamp(BinaryPrimitives.ReadSingleLittleEndian(b[12..]), -1f, 1f);
            f.Spring = Clamp(BinaryPrimitives.ReadSingleLittleEndian(b[16..]), 0f, 1f);
            f.Damper = Clamp(BinaryPrimitives.ReadSingleLittleEndian(b[20..]), 0f, 1f);
            f.SteeringLockDeg = BinaryPrimitives.ReadSingleLittleEndian(b[24..]);
            f.EchoBridgeTimeMs = BinaryPrimitives.ReadUInt32LittleEndian(b[28..]);
            f.Enabled = (b[32] & 1) != 0;
            return true;
        }

        static float Clamp(float v, float lo, float hi) => float.IsNaN(v) ? 0f : Math.Clamp(v, lo, hi);
    }
}
