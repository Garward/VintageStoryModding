using ProtoBuf;

namespace VRPG.Network;

[ProtoContract]
public sealed class SkillChannelStatePacket
{
    [ProtoMember(1)]
    public bool Active { get; set; }

    [ProtoMember(2)]
    public int Slot { get; set; }

    [ProtoMember(3)]
    public string SkillCode { get; set; } = "";

    [ProtoMember(4)]
    public string SkillName { get; set; } = "";

    [ProtoMember(5)]
    public string Color { get; set; } = "#ff9f0d";

    [ProtoMember(6)]
    public float MaxDurationSeconds { get; set; }
}
