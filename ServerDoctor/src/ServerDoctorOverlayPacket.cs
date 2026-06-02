using System.Collections.Generic;
using ProtoBuf;

namespace ServerDoctor;

[ProtoContract]
public sealed class ServerDoctorOverlayPacket
{
    [ProtoMember(1)]
    public bool Enabled;

    [ProtoMember(2)]
    public List<ServerDoctorOverlayEntry> Entries = new List<ServerDoctorOverlayEntry>();

    [ProtoMember(3)]
    public long CreatedUnixMs;

    [ProtoMember(4)]
    public float ObservedTickRate;

    [ProtoMember(5)]
    public float TargetTickRate;

    [ProtoMember(6)]
    public float AverageActiveMilliseconds;

    [ProtoMember(7)]
    public float AverageFrameMilliseconds;

    [ProtoMember(8)]
    public float MaxActiveMilliseconds;
}

[ProtoContract]
public sealed class ServerDoctorOverlayEntry
{
    [ProtoMember(1)]
    public int X;

    [ProtoMember(2)]
    public int Y;

    [ProtoMember(3)]
    public int Z;

    [ProtoMember(4)]
    public float MillisecondsPerTick;

    [ProtoMember(5)]
    public float PercentOfActiveTick;

    [ProtoMember(6)]
    public int Calls;

    [ProtoMember(7)]
    public string Label;

    [ProtoMember(8)]
    public bool HasCoordinates;
}

[ProtoContract]
public sealed class ServerDoctorControlPacket
{
    [ProtoMember(1)]
    public string Action;
}

[ProtoContract]
public sealed class ServerDoctorControlResponsePacket
{
    [ProtoMember(1)]
    public bool Allowed;

    [ProtoMember(2)]
    public string Message;

    [ProtoMember(3)]
    public bool Enabled;

    [ProtoMember(4)]
    public bool OpenDialog;
}
