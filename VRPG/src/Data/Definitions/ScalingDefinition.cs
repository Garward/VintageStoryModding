using VRPG.Data;

namespace VRPG.Data.Definitions;

public sealed class ScalingDefinition : IVrpgDataRecord
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public int MaxPlayerLevel { get; set; } = 100;
    public int MaxCreatureLevel { get; set; } = 100;
    public PlayerLevelScalingDefinition PlayerLevels { get; set; } = new PlayerLevelScalingDefinition();
    public CreatureLevelScalingDefinition CreatureLevels { get; set; } = new CreatureLevelScalingDefinition();
    public CreatureStatScalingDefinition CreatureStats { get; set; } = new CreatureStatScalingDefinition();
    public CreatureRarityScalingDefinition[] CreatureRarities { get; set; } = System.Array.Empty<CreatureRarityScalingDefinition>();
    public WeaponLevelScalingDefinition WeaponLevels { get; set; } = new WeaponLevelScalingDefinition();
}

public sealed class PlayerLevelScalingDefinition
{
    public long BaseExperienceToNextLevel { get; set; } = 1000;
    public float ExperienceGrowth { get; set; } = 1.13f;
    public long ExperienceLinearPerLevel { get; set; } = 75;
}

public sealed class CreatureLevelScalingDefinition
{
    public bool OpenWorldNearbyPlayerLevelCap { get; set; } = true;
    public int SurfaceAllowedOverlevel { get; set; } = 3;
    public int UndergroundAllowedOverlevel { get; set; } = 5;
    public int TemporalAllowedOverlevel { get; set; } = 8;
    public int RiftAllowedOverlevel { get; set; } = 100;
    public int NearbyPlayerRadius { get; set; } = 96;
    public int SpawnDistanceBlocksPerLevel { get; set; } = 650;
    public int DepthBlocksPerLevel { get; set; } = 10;
    public int UndergroundStartsBelowY { get; set; } = 96;
    public int RiftBaseLevel { get; set; } = 10;
    public bool RiftUsesNearbyPlayerCap { get; set; } = false;
}

public sealed class CreatureStatScalingDefinition
{
    public float HealthPerLevelPercent { get; set; } = 12f;
    public float DamagePerLevelPercent { get; set; } = 8f;
    public float ArmorPerLevel { get; set; } = 0.2f;
    public float StatusChancePerLevelPercent { get; set; } = 0.25f;
    public CreatureTierScalingDefinition[] Tiers { get; set; } = System.Array.Empty<CreatureTierScalingDefinition>();
}

public sealed class CreatureTierScalingDefinition
{
    public int StartsAtLevel { get; set; }
    public float HealthMultiplier { get; set; } = 1f;
    public float DamageMultiplier { get; set; } = 1f;
    public float ExperienceMultiplier { get; set; } = 1f;
}

public sealed class CreatureRarityScalingDefinition
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public int MinLevel { get; set; } = 1;
    public int Weight { get; set; } = 100;
    public int AffixSlots { get; set; }
    public float HealthMultiplier { get; set; } = 1f;
    public float DamageMultiplier { get; set; } = 1f;
    public float ExperienceMultiplier { get; set; } = 1f;
}

public sealed class WeaponLevelScalingDefinition
{
    public float BaseDamage { get; set; } = 10f;
    public float DamagePerLevelPercent { get; set; } = 3.5f;
    public float CommonRarityMultiplier { get; set; } = 1f;
}
