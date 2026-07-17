using VRPG.Data.Definitions;
using VRPG.Modules.Rpg.Scaling;
using Xunit;

namespace VRPG.Tests;

public sealed class ScalingMathTests
{
    [Fact]
    public void HealthUsesLevelTierAndRarityLayers()
    {
        ScalingDefinition scaling = CreateScaling();
        var rarity = new CreatureRarityScalingDefinition { HealthMultiplier = 2f };

        float multiplier = ScalingMath.CreatureHealthMultiplier(scaling, 11, rarity);

        Assert.Equal(6.6f, multiplier, 3);
    }

    [Fact]
    public void TierSelectionUsesLatestEligibleTier()
    {
        ScalingDefinition scaling = CreateScaling();

        Assert.Equal(1, ScalingMath.TierFor(scaling, 10).StartsAtLevel);
        Assert.Equal(11, ScalingMath.TierFor(scaling, 11).StartsAtLevel);
    }

    [Fact]
    public void WeaponRequirementUsesCompoundingGrowthWithoutAStalenessPenalty()
    {
        ScalingDefinition scaling = CreateScaling();
        scaling.WeaponLevels = new WeaponLevelScalingDefinition
        {
            BaseDamage = 10,
            DamagePerLevelPercent = 3.5f
        };

        float level80 = ScalingMath.WeaponLevelBaseDamage(scaling, 80);
        float level100 = ScalingMath.WeaponLevelBaseDamage(scaling, 100);

        Assert.Equal(1.99f, level100 / level80, 2);
        Assert.Equal(level80, ScalingMath.WeaponLevelBaseDamage(scaling, 80), 5);
    }

    [Fact]
    public void WeaponDamageAppliesAffixesBeforeBoundedRarityScalar()
    {
        float result = ScalingMath.ResolveWeaponDamage(
            levelBaseDamage: 100,
            flatWeaponDamage: 20,
            additionalWeaponDamagePercent: 50,
            moreWeaponDamagePercent: 25,
            rarityMultiplier: 1.15f);

        Assert.Equal(258.75f, result, 3);
    }

    [Fact]
    public void BuildPressureMeasuresCreatureHealthOutpacingCurrentWeaponGrowth()
    {
        ScalingDefinition scaling = CreateScaling();
        scaling.WeaponLevels = new WeaponLevelScalingDefinition { BaseDamage = 10, DamagePerLevelPercent = 3.5f };
        scaling.CreatureStats.HealthPerLevelPercent = 12;
        scaling.CreatureStats.Tiers = new[]
        {
            new CreatureTierScalingDefinition { StartsAtLevel = 1, HealthMultiplier = 1 },
            new CreatureTierScalingDefinition { StartsAtLevel = 91, HealthMultiplier = 32 }
        };
        var ordinary = new CreatureRarityScalingDefinition { HealthMultiplier = 1 };

        float pressure = ScalingMath.CreatureBuildPressureMultiplier(scaling, 100, ordinary);

        Assert.InRange(pressure, 13.6f, 13.8f);
    }

    private static ScalingDefinition CreateScaling()
    {
        return new ScalingDefinition
        {
            MaxPlayerLevel = 100,
            MaxCreatureLevel = 100,
            CreatureStats = new CreatureStatScalingDefinition
            {
                HealthPerLevelPercent = 12f,
                DamagePerLevelPercent = 8f,
                Tiers = new[]
                {
                    new CreatureTierScalingDefinition { StartsAtLevel = 1, HealthMultiplier = 1f, DamageMultiplier = 1f, ExperienceMultiplier = 1f },
                    new CreatureTierScalingDefinition { StartsAtLevel = 11, HealthMultiplier = 1.5f, DamageMultiplier = 1.25f, ExperienceMultiplier = 2f }
                }
            }
        };
    }
}
