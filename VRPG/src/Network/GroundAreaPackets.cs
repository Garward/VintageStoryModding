using ProtoBuf;

namespace VRPG.Network;

public enum GroundAreaShape : byte
{
    Disc = 0,
    Ring = 1
}

public enum GroundAreaState : byte
{
    Armed = 0,
    Triggered = 1,
    Active = 2,
    Expiring = 3
}

[ProtoContract]
public sealed class GroundAreaUpsertPacket
{
    [ProtoMember(1)]
    public long Id { get; set; }

    [ProtoMember(2)]
    public string OwnerUid { get; set; } = "";

    [ProtoMember(3)]
    public string StyleCode { get; set; } = "";

    [ProtoMember(4)]
    public byte Shape { get; set; }

    [ProtoMember(5)]
    public double X { get; set; }

    [ProtoMember(6)]
    public double Y { get; set; }

    [ProtoMember(7)]
    public double Z { get; set; }

    [ProtoMember(8)]
    public long FollowEntityId { get; set; }

    [ProtoMember(9)]
    public float Radius { get; set; }

    [ProtoMember(10)]
    public byte State { get; set; }

    [ProtoMember(11)]
    public int RemainingMs { get; set; }
}

[ProtoContract]
public sealed class GroundAreaRemovePacket
{
    [ProtoMember(1)]
    public long Id { get; set; }
}
