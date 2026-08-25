using System;
using System.Collections.Generic;
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
    public SkillChargesDefinition Charges { get; set; } = new SkillChargesDefinition();
    public SkillMeleeDefinition Melee { get; set; } = new SkillMeleeDefinition();
    public SkillTimingDefinition Timing { get; set; } = new SkillTimingDefinition();
    public SkillProjectileDefinition Projectile { get; set; } = new SkillProjectileDefinition();
    public SkillTargetedDropDefinition TargetedDrop { get; set; } = new SkillTargetedDropDefinition();
    public SkillParticleDefinition Particles { get; set; } = new SkillParticleDefinition();
    public SkillImpactVisualDefinition ImpactVisual { get; set; } = new SkillImpactVisualDefinition();
    public SkillGroundAreaDefinition GroundArea { get; set; } = new SkillGroundAreaDefinition();
    public SkillOnHitEffectDefinition[] OnHitEffects { get; set; } = Array.Empty<SkillOnHitEffectDefinition>();

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
    public bool RepeatWhileHeld { get; set; }
    public float HoldRepeatDelaySeconds { get; set; } = 0.35f;
    public float HoldRepeatIntervalSeconds { get; set; } = 0.45f;
}

/// <summary>
/// Optional stored activations. CooldownSeconds is the time required to restore
/// one charge; recovery is sequential and begins when the first charge is spent.
/// </summary>
public sealed class SkillChargesDefinition
{
    public int Maximum { get; set; } = 1;
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
    public float CreatureCollisionRadius { get; set; } = 0.2f;
    public bool Ballistic { get; set; }
    public float MinimumFlightSeconds { get; set; } = 0.45f;
    public string RotationMode { get; set; } = "flight";
    public string[] ModelVariants { get; set; } = Array.Empty<string>();
}

public sealed class SkillTargetedDropDefinition
{
    public float Height { get; set; } = 8f;
    public float FallSpeed { get; set; } = 1.5f;
    public float Gravity { get; set; } = 18f;
    public float LifetimeSeconds { get; set; } = 10f;
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

/// <summary>
/// Optional client-side layers composed when a skill emits an impact or burst.
/// Quantities are independently budgeted and require no gameplay entity.
/// </summary>
public sealed class SkillImpactVisualDefinition
{
    public bool Enabled { get; set; }
    public string Preset { get; set; } = "";
    public SkillFxLayerDefinition[] Layers { get; set; } = Array.Empty<SkillFxLayerDefinition>();
    public Dictionary<string, SkillFxLayerOverrideDefinition> Overrides { get; set; } = new Dictionary<string, SkillFxLayerOverrideDefinition>(StringComparer.OrdinalIgnoreCase);
    public float ParticleDurationScale { get; set; } = 1f;
    public float ExpansionSpeedScale { get; set; } = 1f;
    public bool Shockwave { get; set; }
    public float ShockwaveDurationSeconds { get; set; } = 0.32f;
    public float CameraShake { get; set; }
    public float CameraShakeRange { get; set; } = 16f;
    public string[] Sounds { get; set; } = Array.Empty<string>();
    public float SoundRange { get; set; } = 24f;
    public float SoundVolume { get; set; } = 1f;
}

public sealed class SkillFxLayerDefinition
{
    public string Role { get; set; } = "custom";
    public string Model { get; set; } = "quad";
    public string Color { get; set; } = "$skill";
    public float Quantity { get; set; }
    public float SizeMin { get; set; } = 0.1f;
    public float SizeMax { get; set; } = 0.25f;
    public float LifetimeSeconds { get; set; } = 0.5f;
    public float Gravity { get; set; }
    public float Coverage { get; set; } = 0.65f;
    public float OriginCoverage { get; set; } = 0.065f;
    public int Glow { get; set; }
    public float DelaySeconds { get; set; }
    public bool TerrainCollision { get; set; }
    public bool Informative { get; set; }
    public SkillFxEvolveDefinition? OpacityEvolve { get; set; }
    public SkillFxEvolveDefinition? SizeEvolve { get; set; }
}

/// <summary>Nullable fields preserve field-wise preset inheritance.</summary>
public sealed class SkillFxLayerOverrideDefinition
{
    public string? Model { get; set; }
    public string? Color { get; set; }
    public float? Quantity { get; set; }
    public float? SizeMin { get; set; }
    public float? SizeMax { get; set; }
    public float? LifetimeSeconds { get; set; }
    public float? Gravity { get; set; }
    public float? Coverage { get; set; }
    public float? OriginCoverage { get; set; }
    public int? Glow { get; set; }
    public float? DelaySeconds { get; set; }
    public bool? TerrainCollision { get; set; }
    public bool? Informative { get; set; }
    public SkillFxEvolveDefinition? OpacityEvolve { get; set; }
    public SkillFxEvolveDefinition? SizeEvolve { get; set; }
}

public sealed class SkillFxEvolveDefinition
{
    public string Fn { get; set; } = "linear";
    public float Rate { get; set; }
}

public sealed class SkillGroundAreaDefinition
{
    public bool Enabled { get; set; }
    public float DurationSeconds { get; set; }
    public float Radius { get; set; }
}

/// <summary>
/// Data-authored status mutation performed after a skill successfully damages
/// a target. Operations are interpreted by SkillStatusEffectService.
/// </summary>
public sealed class SkillOnHitEffectDefinition
{
    public string StatusCode { get; set; } = "";
    public string Operation { get; set; } = "apply";
    public int Stacks { get; set; } = 1;
    public float PrimaryMagnitude { get; set; }
    public float SecondaryMagnitude { get; set; }
    public float DurationSeconds { get; set; }
    public float MaximumMagnitude { get; set; } = 100f;
    public float TriggerThreshold { get; set; }
    public string TriggerEvent { get; set; } = "";
    public string ResultStatusCode { get; set; } = "";
    public float ResultDurationSeconds { get; set; }
}
