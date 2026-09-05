using ProtoBuf;

namespace VRPG.Network;

[ProtoContract]
public sealed class OnboardingStatePacket
{
    [ProtoMember(1)]
    public string Stage { get; set; } = "appearance";

    [ProtoMember(2)]
    public StarterChoicePacket[] Starters { get; set; } = System.Array.Empty<StarterChoicePacket>();

    [ProtoMember(3)]
    public bool CanConfirmStarter { get; set; }

    [ProtoMember(4)]
    public string StatusMessage { get; set; } = "";
}

[ProtoContract]
public sealed class StarterChoicePacket
{
    [ProtoMember(1)] public string Code { get; set; } = "";
    [ProtoMember(2)] public string Name { get; set; } = "";
    [ProtoMember(3)] public string Description { get; set; } = "";
    [ProtoMember(4)] public string CombatPattern { get; set; } = "";
    [ProtoMember(5)] public string DefensiveApproach { get; set; } = "";
    [ProtoMember(6)] public string PrimaryAttribute { get; set; } = "";
    [ProtoMember(7)] public string WeaponName { get; set; } = "";
    [ProtoMember(8)] public string[] ArmorNames { get; set; } = System.Array.Empty<string>();
    [ProtoMember(9)] public string SkillName { get; set; } = "";
    [ProtoMember(10)] public string SkillDescription { get; set; } = "";
    [ProtoMember(11)] public string Color { get; set; } = "#ff9f0d";
}
