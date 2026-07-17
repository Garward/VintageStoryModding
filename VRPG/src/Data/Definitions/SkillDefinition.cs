using System;
using VRPG.Data;

namespace VRPG.Data.Definitions;

public sealed class SkillDefinition : IVrpgDataRecord
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string ClassCode { get; set; } = "";
    public string Icon { get; set; } = "skill";
    public int RequiredLevel { get; set; } = 1;
    public int MaxLevel { get; set; } = 10;
    public string Delivery { get; set; } = "raycast_aoe";
    public float CooldownSeconds { get; set; } = 1f;
    public float Range { get; set; } = 12f;
    public float Radius { get; set; } = 2f;
    public int MaxTargets { get; set; }
    public string Model { get; set; } = "";
    public string Color { get; set; } = "#ffffff";
    public string[] Tags { get; set; } = Array.Empty<string>();
    public SkillDamageDefinition Damage { get; set; } = new SkillDamageDefinition();
    public SkillResourceCostDefinition Resource { get; set; } = new SkillResourceCostDefinition();
    public SkillMeleeDefinition Melee { get; set; } = new SkillMeleeDefinition();
    public SkillTimingDefinition Timing { get; set; } = new SkillTimingDefinition();
    public SkillProjectileDefinition Projectile { get; set; } = new SkillProjectileDefinition();
    public SkillParticleDefinition Particles { get; set; } = new SkillParticleDefinition();

    public float DamageAtLevel(int level)
    {
        int clamped = Math.Clamp(level, 1, Math.Max(1, MaxLevel));
        return Math.Max(0f, Damage.Base + Damage.PerLevel * (clamped - 1));
    }

    public float WeaponDamagePercentAtLevel(int level)
    {
        int clamped = Math.Clamp(level, 1, Math.Max(1, MaxLevel));
        float rankGrowth = 1f + Math.Max(0f, Damage.WeaponDamagePerLevelPercent) / 100f * (clamped - 1);
        return Math.Max(0f, Damage.WeaponDamagePercent * rankGrowth);
    }

    public float ResourceCostAtLevel(int level)
    {
        int clamped = Math.Clamp(level, 1, Math.Max(1, MaxLevel));
        return Math.Max(0f, Resource.Base + Resource.PerLevel * (clamped - 1));
    }
}

public sealed class SkillDamageDefinition
{
    public string Type { get; set; } = "vrpg:physical";
    public float Base { get; set; } = 1f;
    public float PerLevel { get; set; }
    public float WeaponDamagePercent { get; set; } = 100f;
    public float WeaponDamagePerLevelPercent { get; set; } = 4f;
    public int Tier { get; set; }
    public bool IgnoreInvFrames { get; set; }
}

public sealed class SkillResourceCostDefinition
{
    public string Type { get; set; } = "none";
    public float Base { get; set; }
    public float PerLevel { get; set; }
    public string CostMode { get; set; } = "cast";
}

public sealed class SkillMeleeDefinition
{
    public float ArcDegrees { get; set; } = 70f;
    public float Width { get; set; } = 1.2f;
    public float VerticalTolerance { get; set; } = 2.25f;
}

public sealed class SkillTimingDefinition
{
    public string Mode { get; set; } = "instant";
    public int HitCount { get; set; } = 1;
    public float HitIntervalSeconds { get; set; } = 0.2f;
    public float MaxDurationSeconds { get; set; } = 4f;
}

public sealed class SkillProjectileDefinition
{
    public string ImpactMode { get; set; } = "entity";
    public float Speed { get; set; } = 0.65f;
    public float LifetimeSeconds { get; set; } = 5f;
    public float VerticalOffset { get; set; } = -0.5f;
    public float HorizontalOffset { get; set; } = 0.22f;
    public float ForwardOffset { get; set; } = 0.5f;
    public float AimConvergenceDistance { get; set; } = 12f;
}

public sealed class SkillParticleDefinition
{
    public string Model { get; set; } = "quad";
    public float BurstQuantity { get; set; } = 24f;
    public float TrailQuantity { get; set; } = 1f;
    public float LifetimeSeconds { get; set; } = 0.6f;
    public float TrailLifetimeSeconds { get; set; } = 0.2f;
    public float Gravity { get; set; }
    public float Scale { get; set; } = 0.35f;
    public float Velocity { get; set; } = 0.8f;
    public float OriginVerticalOffset { get; set; } = -0.48f;
    public float OriginHorizontalOffset { get; set; } = 0.18f;
    public float OriginForwardOffset { get; set; } = 0.35f;
}
