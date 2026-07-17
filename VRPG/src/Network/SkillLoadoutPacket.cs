using ProtoBuf;

namespace VRPG.Network;

[ProtoContract]
public sealed class SkillLoadoutPacket
{
    [ProtoMember(1)]
    public SkillLoadoutSlotPacket[] Slots { get; set; } = System.Array.Empty<SkillLoadoutSlotPacket>();
}

[ProtoContract]
public sealed class SkillLoadoutSlotPacket
{
    [ProtoMember(1)]
    public int Slot { get; set; }

    [ProtoMember(2)]
    public string Code { get; set; } = "";

    [ProtoMember(3)]
    public string Name { get; set; } = "";

    [ProtoMember(4)]
    public string Icon { get; set; } = "skill";

    [ProtoMember(5)]
    public string Color { get; set; } = "#ff9f0d";

    [ProtoMember(6)]
    public int LearnedLevel { get; set; }

    [ProtoMember(7)]
    public float CooldownSeconds { get; set; }

    [ProtoMember(8)]
    public float CooldownRemainingSeconds { get; set; }

    [ProtoMember(9)]
    public string ResourceType { get; set; } = "none";

    [ProtoMember(10)]
    public float ResourceCost { get; set; }

    [ProtoMember(11)]
    public string TimingMode { get; set; } = "instant";

    [ProtoMember(12)]
    public string ResourceCostMode { get; set; } = "cast";

    [ProtoMember(13)]
    public float HitIntervalSeconds { get; set; }
}
