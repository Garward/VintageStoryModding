using System;
using VRPG.Data.Definitions;

namespace VRPG.Modules.Rpg.Scaling;

/// <summary>Pure progression formulas shared by runtime scaling and offline balance tools.</summary>
public static class ScalingMath
{
    public static long CreatureExperience(ScalingDefinition scaling, int level, CreatureRarityScalingDefinition rarity)
    {
        float tierExperience = TierFor(scaling, level).ExperienceMultiplier;
        double baseExperience = 8 + level * 6 + Math.Pow(level, 1.35);
        double value = baseExperience * tierExperience * Math.Max(0.01f, rarity.ExperienceMultiplier);
        return Math.Max(1, (long)Math.Round(value));
    }

    public static float CreatureHealthMultiplier(ScalingDefinition scaling, int level, CreatureRarityScalingDefinition rarity)
    {
        CreatureStatScalingDefinition stats = scaling.CreatureStats;
        float levelMultiplier = 1f + Math.Max(0, level - 1) * Math.Max(0f, stats.HealthPerLevelPercent) / 100f;
        return Math.Max(0.01f, levelMultiplier * TierFor(scaling, level).HealthMultiplier * Math.Max(0.01f, rarity.HealthMultiplier));
    }

    public static float CreatureDamageMultiplier(ScalingDefinition scaling, int level, CreatureRarityScalingDefinition rarity)
    {
        CreatureStatScalingDefinition stats = scaling.CreatureStats;
        float levelMultiplier = 1f + Math.Max(0, level - 1) * Math.Max(0f, stats.DamagePerLevelPercent) / 100f;
        return Math.Max(0.01f, levelMultiplier * TierFor(scaling, level).DamageMultiplier * Math.Max(0.01f, rarity.DamageMultiplier));
    }

    public static float WeaponBaseDamage(ScalingDefinition scaling, int level, float baseDamage = 0f, float rarityMultiplier = 1f)
    {
        float levelBase = WeaponLevelBaseDamage(scaling, level, baseDamage);
        return levelBase * Math.Max(0.01f, rarityMultiplier);
    }

    public static float WeaponLevelBaseDamage(ScalingDefinition scaling, int requiredLevel, float baseDamage = 0f)
    {
        WeaponLevelScalingDefinition weapon = scaling.WeaponLevels;
        float baseValue = baseDamage > 0f ? baseDamage : Math.Max(0.01f, weapon.BaseDamage);
        int clampedLevel = Math.Clamp(requiredLevel, 1, Math.Max(1, scaling.MaxPlayerLevel));
        double growth = 1.0 + Math.Max(0f, weapon.DamagePerLevelPercent) / 100.0;
        return (float)(baseValue * Math.Pow(growth, clampedLevel - 1));
    }

    public static float ResolveWeaponDamage(
        float levelBaseDamage,
        float flatWeaponDamage = 0f,
        float additionalWeaponDamagePercent = 0f,
        float moreWeaponDamagePercent = 0f,
        float rarityMultiplier = 1f)
    {
        float additionalMultiplier = Math.Max(0f, 1f + additionalWeaponDamagePercent / 100f);
        float moreMultiplier = Math.Max(0f, 1f + moreWeaponDamagePercent / 100f);
        return Math.Max(0f, levelBaseDamage + flatWeaponDamage)
            * additionalMultiplier
            * moreMultiplier
            * Math.Max(0.01f, rarityMultiplier);
    }

    public static float CreatureBuildPressureMultiplier(
        ScalingDefinition scaling,
        int creatureLevel,
        CreatureRarityScalingDefinition rarity)
    {
        float healthGrowth = CreatureHealthMultiplier(scaling, creatureLevel, rarity);
        float levelOneWeapon = WeaponLevelBaseDamage(scaling, 1);
        float currentWeapon = WeaponLevelBaseDamage(scaling, creatureLevel);
        float weaponGrowth = currentWeapon / Math.Max(0.01f, levelOneWeapon);
        return healthGrowth / Math.Max(0.01f, weaponGrowth);
    }

    public static CreatureTierScalingDefinition TierFor(ScalingDefinition scaling, int level)
    {
        var result = new CreatureTierScalingDefinition { StartsAtLevel = 1 };
        CreatureTierScalingDefinition[] tiers = scaling.CreatureStats.Tiers;
        for (int i = 0; i < tiers.Length; i++)
        {
            int startsAt = Math.Max(1, tiers[i].StartsAtLevel);
            if (level >= startsAt && startsAt >= result.StartsAtLevel)
            {
                result = tiers[i];
            }
        }

        return result;
    }
}
