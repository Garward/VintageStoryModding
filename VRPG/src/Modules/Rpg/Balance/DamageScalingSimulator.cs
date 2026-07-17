using System;
using System.Collections.Generic;
using VRPG.Data.Definitions;
using VRPG.Modules.Rpg.Scaling;

namespace VRPG.Modules.Rpg.Balance;

/// <summary>Deterministically compares a damage profile with every configured creature level and rarity.</summary>
public sealed class DamageScalingSimulator
{
    public IReadOnlyList<DamageScalingRow> Run(ScalingDefinition scaling, SkillDefinition skill, DamageScalingScenario scenario)
    {
        int minLevel = Math.Clamp(scenario.MinCreatureLevel, 1, Math.Max(1, scaling.MaxCreatureLevel));
        int maxLevel = Math.Clamp(scenario.MaxCreatureLevel, minLevel, Math.Max(1, scaling.MaxCreatureLevel));
        CreatureRarityScalingDefinition[] rarities = scaling.CreatureRarities.Length > 0
            ? scaling.CreatureRarities
            : new[] { new CreatureRarityScalingDefinition { Code = "ordinary", Name = "Ordinary" } };
        var rows = new List<DamageScalingRow>((maxLevel - minLevel + 1) * rarities.Length);

        for (int level = minLevel; level <= maxLevel; level++)
        {
            int skillRank = ResolveSkillRank(skill, scenario, level, scaling.MaxCreatureLevel);
            int weaponRequiredLevel = scenario.WeaponRequiredLevelOverride
                ?? Math.Max(1, level - Math.Max(0, scenario.WeaponLevelLag));
            double weaponLevelBaseDamage = ScalingMath.WeaponLevelBaseDamage(
                scaling,
                weaponRequiredLevel,
                (float)scenario.WeaponBaseDamageOverride);
            double finalWeaponDamage = ScalingMath.ResolveWeaponDamage(
                (float)weaponLevelBaseDamage,
                (float)scenario.FlatWeaponDamage,
                (float)scenario.AdditionalWeaponDamagePercent,
                (float)scenario.MoreWeaponDamagePercent,
                (float)scenario.WeaponRarityMultiplier);
            double skillWeaponDamagePercent = scenario.SkillWeaponDamagePercentOverride
                ?? skill.WeaponDamagePercentAtLevel(skillRank);
            double baseHitDamage = scenario.HitDamageOverride
                ?? (scenario.UseWeaponDamage
                    ? finalWeaponDamage * skillWeaponDamagePercent / 100.0
                    : skill.DamageAtLevel(skillRank));
            double nonCriticalHit = ResolveNonCriticalHit(baseHitDamage, scenario);
            double criticalChance = ResolveCriticalChance(scenario);
            double expectedHit = ResolveExpectedHit(nonCriticalHit, criticalChance, scenario.CriticalDamagePercent);
            int hitsPerActivation = ResolveHitsPerActivation(skill);
            double castsPerSecond = scenario.CastsPerSecond > 0
                ? scenario.CastsPerSecond
                : ResolveDefaultActivationsPerSecond(skill);
            double hitsPerSecond = hitsPerActivation * castsPerSecond;
            double expectedDamagePerActivation = expectedHit * hitsPerActivation;
            double expectedDps = expectedHit * hitsPerSecond;

            for (int rarityIndex = 0; rarityIndex < rarities.Length; rarityIndex++)
            {
                CreatureRarityScalingDefinition rarity = rarities[rarityIndex];
                bool eligible = level >= Math.Max(1, rarity.MinLevel);
                if (!eligible && !scenario.IncludeIneligibleRarities)
                {
                    continue;
                }

                double healthMultiplier = ScalingMath.CreatureHealthMultiplier(scaling, level, rarity);
                double targetHealth = Math.Max(
                    1.0,
                    scenario.BaseCreatureHealth
                    * healthMultiplier
                    * Math.Max(0.01, scenario.EncounterHealthMultiplier));
                double expectedHits = expectedHit > 0 ? targetHealth / expectedHit : double.PositiveInfinity;
                int wholeHits = expectedHit > 0 ? Math.Max(1, (int)Math.Ceiling(expectedHits)) : int.MaxValue;
                double timeToKill = expectedDps > 0 ? targetHealth / expectedDps : double.PositiveInfinity;
                double incomingHit = Math.Max(0.0, scenario.BaseCreatureHitDamage)
                    * ScalingMath.CreatureDamageMultiplier(scaling, level, rarity)
                    * Math.Max(0.0, 1.0 - scenario.PlayerDamageReductionPercent / 100.0);
                double incomingDps = incomingHit * Math.Max(0.0, scenario.CreatureAttacksPerSecond);
                double survivalTime = incomingDps > 0
                    ? Math.Max(0.0, scenario.PlayerHealth) / incomingDps
                    : double.PositiveInfinity;

                rows.Add(new DamageScalingRow
                {
                    Scenario = scenario.Code,
                    SkillCode = skill.Code,
                    SkillName = skill.Name,
                    SkillRank = skillRank,
                    CreatureLevel = level,
                    RarityCode = rarity.Code,
                    RarityName = rarity.Name,
                    RarityEligible = eligible,
                    AffixSlots = Math.Max(0, rarity.AffixSlots),
                    BaseCreatureHealth = scenario.BaseCreatureHealth,
                    EncounterHealthMultiplier = scenario.EncounterHealthMultiplier,
                    HealthMultiplier = healthMultiplier,
                    TargetHealth = targetHealth,
                    CreatureDamageMultiplier = ScalingMath.CreatureDamageMultiplier(scaling, level, rarity),
                    CreatureExperience = ScalingMath.CreatureExperience(scaling, level, rarity),
                    CreatureBuildPressureMultiplier = ScalingMath.CreatureBuildPressureMultiplier(scaling, level, rarity),
                    EncounterBuildPressureMultiplier = ScalingMath.CreatureBuildPressureMultiplier(scaling, level, rarity)
                        * Math.Max(0.01, scenario.EncounterHealthMultiplier),
                    WeaponRequiredLevel = weaponRequiredLevel,
                    WeaponLevelLag = Math.Max(0, level - weaponRequiredLevel),
                    WeaponRarityCode = scenario.WeaponRarityCode,
                    WeaponRarityName = scenario.WeaponRarityName,
                    WeaponRarityMultiplier = scenario.WeaponRarityMultiplier,
                    WeaponLevelBaseDamage = weaponLevelBaseDamage,
                    FinalWeaponDamage = finalWeaponDamage,
                    SkillWeaponDamagePercent = skillWeaponDamagePercent,
                    NonCriticalHit = nonCriticalHit,
                    FinalCriticalChancePercent = criticalChance,
                    CriticalDamagePercent = scenario.CriticalDamagePercent,
                    ExpectedHit = expectedHit,
                    HitsPerActivation = hitsPerActivation,
                    ExpectedDamagePerActivation = expectedDamagePerActivation,
                    CastsPerSecond = castsPerSecond,
                    HitsPerSecond = hitsPerSecond,
                    ExpectedDps = expectedDps,
                    ExpectedHitsToKill = expectedHits,
                    WholeHitsToKill = wholeHits,
                    ExpectedTimeToKillSeconds = timeToKill,
                    IncomingDamagePerHit = incomingHit,
                    IncomingDps = incomingDps,
                    PlayerSurvivalSeconds = survivalTime,
                    WinsDamageRace = timeToKill <= survivalTime
                });
            }
        }

        return rows;
    }

    public static double ResolveCriticalChance(DamageScalingScenario scenario)
    {
        double baseChance = Math.Max(0.0, scenario.BaseCriticalChancePercent + scenario.FlatCriticalChancePercent);
        double additional = Math.Max(-100.0, scenario.AdditionalCriticalChancePercent);
        double moreMultiplier = Math.Max(0.0, 1.0 + scenario.MoreCriticalChancePercent / 100.0);
        return Math.Clamp(baseChance * (1.0 + additional / 100.0) * moreMultiplier, 0.0, Math.Max(0.0, scenario.CriticalChanceCapPercent));
    }

    private static int ResolveSkillRank(SkillDefinition skill, DamageScalingScenario scenario, int creatureLevel, int maxCreatureLevel)
    {
        int maxSkillRank = Math.Max(1, skill.MaxLevel);
        if (!scenario.MatchSkillRankToCreatureLevel)
        {
            return Math.Clamp(scenario.SkillRank, 1, maxSkillRank);
        }

        if (maxCreatureLevel <= 1 || maxSkillRank <= 1)
        {
            return 1;
        }

        double progress = Math.Clamp((creatureLevel - 1.0) / (maxCreatureLevel - 1.0), 0.0, 1.0);
        return Math.Clamp(1 + (int)Math.Floor(progress * (maxSkillRank - 1)), 1, maxSkillRank);
    }

    private static double ResolveNonCriticalHit(double baseHitDamage, DamageScalingScenario scenario)
    {
        double increasedMultiplier = Math.Max(0.0, 1.0 + scenario.AdditionalDamagePercent / 100.0);
        double moreMultiplier = Math.Max(0.0, 1.0 + scenario.MoreDamagePercent / 100.0);
        return Math.Max(0.0, baseHitDamage + scenario.FlatDamage) * increasedMultiplier * moreMultiplier;
    }

    private static double ResolveExpectedHit(double nonCriticalHit, double criticalChancePercent, double criticalDamagePercent)
    {
        double criticalChance = Math.Clamp(criticalChancePercent / 100.0, 0.0, 1.0);
        double criticalMultiplier = Math.Max(0.0, criticalDamagePercent / 100.0);
        return nonCriticalHit * ((1.0 - criticalChance) + criticalChance * criticalMultiplier);
    }

    private static int ResolveHitsPerActivation(SkillDefinition skill)
    {
        if (string.Equals(skill.Timing.Mode, "sequence", StringComparison.OrdinalIgnoreCase))
        {
            return Math.Clamp(skill.Timing.HitCount, 2, 32);
        }

        if (string.Equals(skill.Timing.Mode, "channel", StringComparison.OrdinalIgnoreCase))
        {
            double interval = Math.Max(0.05, skill.Timing.HitIntervalSeconds);
            return Math.Max(1, (int)Math.Ceiling(Math.Max(interval, skill.Timing.MaxDurationSeconds) / interval));
        }

        return 1;
    }

    private static double ResolveDefaultActivationsPerSecond(SkillDefinition skill)
    {
        if (string.Equals(skill.Timing.Mode, "channel", StringComparison.OrdinalIgnoreCase))
        {
            return 1.0 / Math.Max(0.05, skill.Timing.MaxDurationSeconds + skill.CooldownSeconds);
        }

        return 1.0 / Math.Max(0.05, skill.CooldownSeconds);
    }
}

public sealed class DamageScalingScenario
{
    public string Code { get; set; } = "baseline";
    public int SkillRank { get; set; } = 1;
    public bool MatchSkillRankToCreatureLevel { get; set; }
    public int MinCreatureLevel { get; set; } = 1;
    public int MaxCreatureLevel { get; set; } = 100;
    public bool IncludeIneligibleRarities { get; set; } = true;
    public double BaseCreatureHealth { get; set; } = 20.0;
    public double EncounterHealthMultiplier { get; set; } = 1.0;
    public double? HitDamageOverride { get; set; }
    public double? SkillWeaponDamagePercentOverride { get; set; }
    public bool UseWeaponDamage { get; set; } = true;
    public int WeaponLevelLag { get; set; }
    public int? WeaponRequiredLevelOverride { get; set; }
    public double WeaponBaseDamageOverride { get; set; }
    public double FlatWeaponDamage { get; set; }
    public double AdditionalWeaponDamagePercent { get; set; }
    public double MoreWeaponDamagePercent { get; set; }
    public string WeaponRarityCode { get; set; } = "vrpg:common";
    public string WeaponRarityName { get; set; } = "Common";
    public double WeaponRarityMultiplier { get; set; } = 1.0;
    public double FlatDamage { get; set; }
    public double AdditionalDamagePercent { get; set; }
    public double MoreDamagePercent { get; set; }
    public double BaseCriticalChancePercent { get; set; } = 5.0;
    public double FlatCriticalChancePercent { get; set; }
    public double AdditionalCriticalChancePercent { get; set; }
    public double MoreCriticalChancePercent { get; set; }
    public double CriticalChanceCapPercent { get; set; } = 100.0;
    public double CriticalDamagePercent { get; set; } = 150.0;
    public double CastsPerSecond { get; set; }
    public double BaseCreatureHitDamage { get; set; } = 2.0;
    public double CreatureAttacksPerSecond { get; set; } = 2.0 / 3.0;
    public double PlayerHealth { get; set; } = 100.0;
    public double PlayerDamageReductionPercent { get; set; }
}

public sealed class DamageScalingRow
{
    public string Scenario { get; set; } = "";
    public string SkillCode { get; set; } = "";
    public string SkillName { get; set; } = "";
    public int SkillRank { get; set; }
    public int CreatureLevel { get; set; }
    public string RarityCode { get; set; } = "";
    public string RarityName { get; set; } = "";
    public bool RarityEligible { get; set; }
    public int AffixSlots { get; set; }
    public double BaseCreatureHealth { get; set; }
    public double EncounterHealthMultiplier { get; set; }
    public double HealthMultiplier { get; set; }
    public double TargetHealth { get; set; }
    public double CreatureDamageMultiplier { get; set; }
    public long CreatureExperience { get; set; }
    public double CreatureBuildPressureMultiplier { get; set; }
    public double EncounterBuildPressureMultiplier { get; set; }
    public int WeaponRequiredLevel { get; set; }
    public int WeaponLevelLag { get; set; }
    public string WeaponRarityCode { get; set; } = "";
    public string WeaponRarityName { get; set; } = "";
    public double WeaponRarityMultiplier { get; set; }
    public double WeaponLevelBaseDamage { get; set; }
    public double FinalWeaponDamage { get; set; }
    public double SkillWeaponDamagePercent { get; set; }
    public double NonCriticalHit { get; set; }
    public double FinalCriticalChancePercent { get; set; }
    public double CriticalDamagePercent { get; set; }
    public double ExpectedHit { get; set; }
    public int HitsPerActivation { get; set; }
    public double ExpectedDamagePerActivation { get; set; }
    public double CastsPerSecond { get; set; }
    public double HitsPerSecond { get; set; }
    public double ExpectedDps { get; set; }
    public double ExpectedHitsToKill { get; set; }
    public int WholeHitsToKill { get; set; }
    public double ExpectedTimeToKillSeconds { get; set; }
    public double IncomingDamagePerHit { get; set; }
    public double IncomingDps { get; set; }
    public double PlayerSurvivalSeconds { get; set; }
    public bool WinsDamageRace { get; set; }
}
