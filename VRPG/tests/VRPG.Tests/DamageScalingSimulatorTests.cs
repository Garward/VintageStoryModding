using System.Linq;
using VRPG.Data.Definitions;
using VRPG.Modules.Rpg.Balance;
using Xunit;

namespace VRPG.Tests;

public sealed class DamageScalingSimulatorTests
{
    [Fact]
    public void SweepIncludesEveryLevelAndRarity()
    {
        ScalingDefinition scaling = CreateScaling();
        SkillDefinition skill = CreateSkill();
        var scenario = new DamageScalingScenario
        {
            MinCreatureLevel = 1,
            MaxCreatureLevel = 2,
            SkillRank = 1,
            BaseCreatureHealth = 20
        };

        var rows = new DamageScalingSimulator().Run(scaling, skill, scenario);

        Assert.Equal(4, rows.Count);
        Assert.Contains(rows, row => row.CreatureLevel == 1 && row.RarityCode == "rare" && !row.RarityEligible);
        Assert.Contains(rows, row => row.CreatureLevel == 2 && row.RarityCode == "rare" && row.RarityEligible);
    }

    [Fact]
    public void ExpectedDamageUsesConfiguredCriticalLayers()
    {
        ScalingDefinition scaling = CreateScaling();
        SkillDefinition skill = CreateSkill();
        var scenario = new DamageScalingScenario
        {
            MinCreatureLevel = 1,
            MaxCreatureLevel = 1,
            SkillRank = 1,
            BaseCreatureHealth = 20,
            FlatCriticalChancePercent = 1,
            AdditionalCriticalChancePercent = 100,
            CriticalDamagePercent = 150
        };

        DamageScalingRow row = new DamageScalingSimulator().Run(scaling, skill, scenario).First(candidate => candidate.RarityCode == "ordinary");

        Assert.Equal(12, row.FinalCriticalChancePercent, 3);
        Assert.Equal(10.6, row.ExpectedHit, 3);
        Assert.Equal(2, row.WholeHitsToKill);
    }

    [Fact]
    public void MatchedRankReachesMaximumAtMaximumCreatureLevel()
    {
        ScalingDefinition scaling = CreateScaling();
        SkillDefinition skill = CreateSkill();
        var scenario = new DamageScalingScenario
        {
            MinCreatureLevel = 100,
            MaxCreatureLevel = 100,
            MatchSkillRankToCreatureLevel = true
        };

        DamageScalingRow row = new DamageScalingSimulator().Run(scaling, skill, scenario).First();

        Assert.Equal(10, row.SkillRank);
    }

    [Fact]
    public void WeaponLagComparesAnUnchangedOlderWeaponRatherThanApplyingAPenalty()
    {
        ScalingDefinition scaling = CreateScaling();
        scaling.WeaponLevels = new WeaponLevelScalingDefinition { BaseDamage = 10, DamagePerLevelPercent = 3.5f };
        SkillDefinition skill = CreateSkill();
        var currentScenario = new DamageScalingScenario { MinCreatureLevel = 100, MaxCreatureLevel = 100 };
        var olderScenario = new DamageScalingScenario { MinCreatureLevel = 100, MaxCreatureLevel = 100, WeaponLevelLag = 20 };
        var fixedOlderScenario = new DamageScalingScenario { MinCreatureLevel = 80, MaxCreatureLevel = 80 };

        DamageScalingRow current = new DamageScalingSimulator().Run(scaling, skill, currentScenario).First();
        DamageScalingRow olderAtLevel100 = new DamageScalingSimulator().Run(scaling, skill, olderScenario).First();
        DamageScalingRow sameWeaponAtLevel80 = new DamageScalingSimulator().Run(scaling, skill, fixedOlderScenario).First();

        Assert.Equal(80, olderAtLevel100.WeaponRequiredLevel);
        Assert.Equal(sameWeaponAtLevel80.FinalWeaponDamage, olderAtLevel100.FinalWeaponDamage, 3);
        Assert.Equal(0.503, olderAtLevel100.FinalWeaponDamage / current.FinalWeaponDamage, 3);
    }

    [Fact]
    public void SkillRanksScaleWeaponEffectiveness()
    {
        SkillDefinition skill = CreateSkill();
        skill.Damage.WeaponDamagePercent = 85;
        skill.Damage.WeaponDamagePerLevelPercent = 4;

        Assert.Equal(85, skill.WeaponDamagePercentAtLevel(1), 3);
        Assert.Equal(115.6, skill.WeaponDamagePercentAtLevel(10), 3);
    }

    [Fact]
    public void SequenceDamageCountsIndependentHitsPerActivation()
    {
        ScalingDefinition scaling = CreateScaling();
        SkillDefinition skill = CreateSkill();
        skill.CooldownSeconds = 2;
        skill.Timing = new SkillTimingDefinition
        {
            Mode = "sequence",
            HitCount = 3,
            HitIntervalSeconds = 0.2f
        };
        var scenario = new DamageScalingScenario
        {
            MinCreatureLevel = 1,
            MaxCreatureLevel = 1,
            BaseCriticalChancePercent = 0,
            CastsPerSecond = 1
        };

        DamageScalingRow row = new DamageScalingSimulator().Run(scaling, skill, scenario).First();

        Assert.Equal(3, row.HitsPerActivation);
        Assert.Equal(row.ExpectedHit * 3, row.ExpectedDamagePerActivation, 6);
        Assert.Equal(3, row.HitsPerSecond, 6);
        Assert.Equal(row.ExpectedHit * 3, row.ExpectedDps, 6);
    }

    [Fact]
    public void ChannelDamageUsesFullHoldAndCooldownDutyCycle()
    {
        ScalingDefinition scaling = CreateScaling();
        SkillDefinition skill = CreateSkill();
        skill.CooldownSeconds = 3;
        skill.Timing = new SkillTimingDefinition
        {
            Mode = "channel",
            HitIntervalSeconds = 0.25f,
            MaxDurationSeconds = 4f
        };
        var scenario = new DamageScalingScenario
        {
            MinCreatureLevel = 1,
            MaxCreatureLevel = 1,
            BaseCriticalChancePercent = 0
        };

        DamageScalingRow row = new DamageScalingSimulator().Run(scaling, skill, scenario).First();

        Assert.Equal(16, row.HitsPerActivation);
        Assert.Equal(1d / 7d, row.CastsPerSecond, 6);
        Assert.Equal(16d / 7d, row.HitsPerSecond, 6);
        Assert.Equal(row.ExpectedHit * 16d / 7d, row.ExpectedDps, 6);
    }

    [Fact]
    public void UninvestedTopEndProfileLosesTheRepresentativeDamageRace()
    {
        ScalingDefinition scaling = CreateScaling();
        scaling.CreatureStats.HealthPerLevelPercent = 12;
        scaling.CreatureStats.DamagePerLevelPercent = 8;
        scaling.CreatureStats.Tiers = new[]
        {
            new CreatureTierScalingDefinition
            {
                StartsAtLevel = 1,
                HealthMultiplier = 1,
                DamageMultiplier = 1,
                ExperienceMultiplier = 1
            },
            new CreatureTierScalingDefinition
            {
                StartsAtLevel = 91,
                HealthMultiplier = 32,
                DamageMultiplier = 5.7f,
                ExperienceMultiplier = 8.1f
            }
        };
        SkillDefinition basicAttack = CreateSkill();
        basicAttack.MaxLevel = 1;
        basicAttack.CooldownSeconds = 1;
        basicAttack.Damage.WeaponDamagePercent = 100;
        basicAttack.Damage.WeaponDamagePerLevelPercent = 0;
        var scenario = new DamageScalingScenario
        {
            MinCreatureLevel = 100,
            MaxCreatureLevel = 100,
            PlayerHealth = 100,
            BaseCreatureHitDamage = 2,
            CreatureAttacksPerSecond = 2.0 / 3.0
        };

        DamageScalingRow row = new DamageScalingSimulator().Run(scaling, basicAttack, scenario)
            .First(candidate => candidate.RarityCode == "ordinary");

        Assert.False(row.WinsDamageRace);
        Assert.True(row.ExpectedTimeToKillSeconds > row.PlayerSurvivalSeconds);
        Assert.InRange(row.CreatureBuildPressureMultiplier, 13.6, 13.8);
    }

    [Fact]
    public void EncounterHealthAndEffectivenessOverridesModelBossProfiles()
    {
        ScalingDefinition scaling = CreateScaling();
        SkillDefinition skill = CreateSkill();
        var scenario = new DamageScalingScenario
        {
            MinCreatureLevel = 1,
            MaxCreatureLevel = 1,
            BaseCreatureHealth = 250,
            EncounterHealthMultiplier = 20,
            SkillWeaponDamagePercentOverride = 500,
            AdditionalCriticalChancePercent = 1900,
            CriticalDamagePercent = 500,
            CastsPerSecond = 1
        };

        DamageScalingRow row = new DamageScalingSimulator().Run(scaling, skill, scenario)
            .First(candidate => candidate.RarityCode == "ordinary");

        Assert.Equal(5000, row.TargetHealth, 3);
        Assert.Equal(500, row.SkillWeaponDamagePercent, 3);
        Assert.Equal(20, row.EncounterBuildPressureMultiplier, 3);
        Assert.Equal(100, row.FinalCriticalChancePercent, 3);
        Assert.Equal(250, row.ExpectedDps, 3);
        Assert.Equal(20, row.ExpectedTimeToKillSeconds, 3);
    }

    private static ScalingDefinition CreateScaling()
    {
        return new ScalingDefinition
        {
            MaxCreatureLevel = 100,
            MaxPlayerLevel = 100,
            WeaponLevels = new WeaponLevelScalingDefinition { BaseDamage = 10, DamagePerLevelPercent = 3.5f },
            CreatureStats = new CreatureStatScalingDefinition
            {
                HealthPerLevelPercent = 10,
                DamagePerLevelPercent = 5,
                Tiers = new[]
                {
                    new CreatureTierScalingDefinition { StartsAtLevel = 1, HealthMultiplier = 1, DamageMultiplier = 1, ExperienceMultiplier = 1 }
                }
            },
            CreatureRarities = new[]
            {
                new CreatureRarityScalingDefinition { Code = "ordinary", Name = "Ordinary", MinLevel = 1, HealthMultiplier = 1, DamageMultiplier = 1, ExperienceMultiplier = 1 },
                new CreatureRarityScalingDefinition { Code = "rare", Name = "Rare", MinLevel = 2, HealthMultiplier = 2, DamageMultiplier = 1.5f, ExperienceMultiplier = 2 }
            }
        };
    }

    private static SkillDefinition CreateSkill()
    {
        return new SkillDefinition
        {
            Code = "vrpg:test",
            Name = "Test",
            MaxLevel = 10,
            CooldownSeconds = 2,
            Damage = new SkillDamageDefinition
            {
                Base = 10,
                PerLevel = 10,
                WeaponDamagePercent = 100,
                WeaponDamagePerLevelPercent = 4
            }
        };
    }
}
