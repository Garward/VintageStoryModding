using ProtoBuf;

namespace VRPG.Network;

[ProtoContract]
public sealed class RpgResourcePacket
{
    [ProtoMember(1)]
    public float Health { get; set; }

    [ProtoMember(2)]
    public float MaxHealth { get; set; }

    [ProtoMember(3)]
    public float Mana { get; set; }

    [ProtoMember(4)]
    public float MaxMana { get; set; }

    [ProtoMember(7)]
    public float MagicShield { get; set; }

    [ProtoMember(8)]
    public float MaxMagicShield { get; set; }

    [ProtoMember(9)]
    public float Blood { get; set; }

    [ProtoMember(10)]
    public float MaxBlood { get; set; }

    [ProtoMember(11)]
    public bool BloodUnlocked { get; set; }

    [ProtoMember(12)]
    public long Experience { get; set; }

    [ProtoMember(13)]
    public long ExperienceToNextLevel { get; set; }

    [ProtoMember(14)]
    public int Level { get; set; }

    [ProtoMember(15)]
    public bool HudEnabled { get; set; }

    [ProtoMember(16)]
    public bool HideVanillaStatbar { get; set; }

    [ProtoMember(17)]
    public float HealthRegenPerSecond { get; set; }

    [ProtoMember(18)]
    public float ManaRegenPerSecond { get; set; }

    [ProtoMember(20)]
    public float MagicShieldRegenPerSecond { get; set; }

    [ProtoMember(21)]
    public float BloodRegenPerSecond { get; set; }

    [ProtoMember(22)]
    public int UnspentStatPoints { get; set; }

    [ProtoMember(23)]
    public int UnspentTalentPoints { get; set; }

    [ProtoMember(24)]
    public float CombatLockRemainingSeconds { get; set; }

    [ProtoMember(25)]
    public string PrimaryAttribute { get; set; } = "";

    [ProtoMember(26)]
    public string StartingAttributeAffinity { get; set; } = "";

    [ProtoMember(27)]
    public bool EvasiveStepActive { get; set; }

    [ProtoMember(28)]
    public float EvasiveStepCooldownRemainingSeconds { get; set; }

    [ProtoMember(29)]
    public int RespecPoints { get; set; }
}
