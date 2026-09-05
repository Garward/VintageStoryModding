using ProtoBuf;

namespace VRPG.Network;

public static class MovementEffectProtocol
{
    public const int CurrentVersion = 1;
}

public enum MovementEffectMode : byte
{
    SetVelocity = 0,
    AddImpulse = 1,
    BlendVelocity = 2,
    Cancel = 3
}

[System.Flags]
public enum MovementEffectAxes : byte
{
    None = 0,
    X = 1,
    Y = 2,
    Z = 4,
    Horizontal = X | Z,
    All = X | Y | Z
}

[ProtoContract]
public sealed class MovementEffectPacket
{
    [ProtoMember(1)] public int ProtocolVersion { get; set; } = MovementEffectProtocol.CurrentVersion;
    [ProtoMember(2)] public long Sequence { get; set; }
    [ProtoMember(3)] public string EffectCode { get; set; } = "";
    [ProtoMember(4)] public byte Mode { get; set; }
    [ProtoMember(5)] public byte Axes { get; set; }
    [ProtoMember(6)] public double X { get; set; }
    [ProtoMember(7)] public double Y { get; set; }
    [ProtoMember(8)] public double Z { get; set; }
    [ProtoMember(9)] public int DurationMilliseconds { get; set; }
    [ProtoMember(10)] public float ResponsePerSecond { get; set; }
    [ProtoMember(11)] public int Priority { get; set; }
    [ProtoMember(12)] public int CollisionGraceMilliseconds { get; set; }
}
