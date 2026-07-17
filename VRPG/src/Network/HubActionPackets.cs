using ProtoBuf;

namespace VRPG.Network;

[ProtoContract]
public sealed class SkillEquipRequestPacket
{
    [ProtoMember(1)]
    public int Slot { get; set; }

    [ProtoMember(2)]
    public string SkillCode { get; set; } = "";
}

[ProtoContract]
public sealed class TalentPlanRequestPacket
{
    [ProtoMember(1)]
    public string[] Allocate { get; set; } = System.Array.Empty<string>();

    [ProtoMember(2)]
    public string[] Refund { get; set; } = System.Array.Empty<string>();
}
