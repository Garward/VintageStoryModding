using ProtoBuf;

namespace VRPG.Network;

[ProtoContract]
public sealed class OpenHubPacket
{
    [ProtoMember(1)]
    public LibraryEntryPacket[] Entries { get; set; } = System.Array.Empty<LibraryEntryPacket>();

    [ProtoMember(2)]
    public RpgResourcePacket? Resources { get; set; }

    [ProtoMember(3)]
    public HubStatPacket[] BaseStats { get; set; } = System.Array.Empty<HubStatPacket>();

    [ProtoMember(4)]
    public string[] Talents { get; set; } = System.Array.Empty<string>();

    [ProtoMember(6)]
    public HubClassPacket[] Classes { get; set; } = System.Array.Empty<HubClassPacket>();

    [ProtoMember(7)]
    public HubOptionsPacket Options { get; set; } = new HubOptionsPacket();

    [ProtoMember(8)]
    public string FeedbackMessage { get; set; } = "";

    [ProtoMember(9)]
    public bool FeedbackError { get; set; }

    [ProtoMember(10)]
    public string TalentTreeCode { get; set; } = "";

    [ProtoMember(11)]
    public string TalentTreeHash { get; set; } = "";

    [ProtoMember(12)]
    public bool CanEditTalentTree { get; set; }
}

[ProtoContract]
public sealed class HubStatPacket
{
    [ProtoMember(1)]
    public string Code { get; set; } = "";

    [ProtoMember(2)]
    public int Value { get; set; }
}

[ProtoContract]
public sealed class HubClassPacket
{
    [ProtoMember(1)]
    public string Code { get; set; } = "";

    [ProtoMember(2)]
    public string Name { get; set; } = "";

    [ProtoMember(3)]
    public string Description { get; set; } = "";

    [ProtoMember(4)]
    public HubSkillPacket[] Skills { get; set; } = System.Array.Empty<HubSkillPacket>();

    [ProtoMember(5)]
    public string Icon { get; set; } = "class";

    [ProtoMember(6)]
    public string Color { get; set; } = "#ff9f0d";

    [ProtoMember(7)]
    public string[] Tags { get; set; } = System.Array.Empty<string>();
}

[ProtoContract]
public sealed class HubSkillPacket
{
    [ProtoMember(1)]
    public string Code { get; set; } = "";

    [ProtoMember(2)]
    public string Name { get; set; } = "";

    [ProtoMember(3)]
    public string Description { get; set; } = "";

    [ProtoMember(4)]
    public int RequiredLevel { get; set; }

    [ProtoMember(5)]
    public string[] Tags { get; set; } = System.Array.Empty<string>();

    [ProtoMember(6)]
    public string ClassCode { get; set; } = "";

    [ProtoMember(7)]
    public string Icon { get; set; } = "skill";

    [ProtoMember(8)]
    public string Color { get; set; } = "#ffffff";

    [ProtoMember(9)]
    public int LearnedLevel { get; set; }

    [ProtoMember(10)]
    public int MaxLevel { get; set; }

    [ProtoMember(11)]
    public int EquippedSlot { get; set; }

    [ProtoMember(12)]
    public string Delivery { get; set; } = "";

    [ProtoMember(13)]
    public string DamageType { get; set; } = "";

    [ProtoMember(14)]
    public float Damage { get; set; }

    [ProtoMember(15)]
    public string ResourceType { get; set; } = "none";

    [ProtoMember(16)]
    public float ResourceCost { get; set; }

    [ProtoMember(17)]
    public float CooldownSeconds { get; set; }

    [ProtoMember(18)]
    public float Range { get; set; }

    [ProtoMember(19)]
    public float Radius { get; set; }

    [ProtoMember(20)]
    public string ProjectileImpactMode { get; set; } = "";

    [ProtoMember(21)]
    public string TimingMode { get; set; } = "instant";

    [ProtoMember(22)]
    public int HitCount { get; set; } = 1;

    [ProtoMember(23)]
    public float HitIntervalSeconds { get; set; }

    [ProtoMember(24)]
    public string ResourceCostMode { get; set; } = "cast";

    [ProtoMember(25)]
    public float MeleeArcDegrees { get; set; }

    [ProtoMember(26)]
    public float MeleeWidth { get; set; }
}
