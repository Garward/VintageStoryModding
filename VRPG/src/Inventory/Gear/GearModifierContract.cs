using System;
using System.Collections.Generic;
using VRPG.Data.Definitions;

namespace VRPG.Inventory.Gear;

/// <summary>The mechanical modifier vocabulary shared by authoring, equipment, and stat consumers.</summary>
public static class GearModifierContract
{
    public static bool IsSupported(string? stat, string? operation)
    {
        if (stat == null || !stat.StartsWith("vrpg:", StringComparison.Ordinal)
            || stat != stat.Trim().ToLowerInvariant() || !GearModifierOperations.IsSupported(operation)) return false;
        // This legacy stat stores additional Health percentage points, not a flat Health amount.
        if (IsHealthPercentage(stat)) return operation == GearModifierOperations.Add;
        return IsBaked(stat) || IsCoreAttribute(stat) || MaximumResource(stat) != GearResourceStat.None
            || RegenerationResource(stat) != GearResourceStat.None || IsDamageStat(stat);
    }

    public static bool IsBaked(string? stat) => stat is "vrpg:weapon_power" or "vrpg:armor"
        or "vrpg:max_health" or "vrpg:max_magic_shield" or "vrpg:evasion" or "vrpg:fire_resistance"
        or "vrpg:cold_resistance" or "vrpg:lightning_resistance" or "vrpg:rust_resistance";

    public static bool IsCoreAttribute(string? stat) => stat is "vrpg:strength" or "vrpg:dexterity" or "vrpg:intelligence";

    public static bool IsHealthPercentage(string? stat) => NormalizeStat(stat) == "max_health_percent";

    public static GearResourceStat MaximumResource(string? stat) => CompactResourceStat(stat) switch
    {
        "maxhealth" or "health" => GearResourceStat.Health,
        "maxmana" or "mana" => GearResourceStat.Mana,
        "maxblood" or "blood" => GearResourceStat.Blood,
        "maxmagicshield" or "magicshield" or "shield" => GearResourceStat.MagicShield,
        _ => GearResourceStat.None
    };

    public static GearResourceStat RegenerationResource(string? stat) => CompactResourceStat(stat) switch
    {
        "healthregen" or "healthregeneration" or "hpregen" => GearResourceStat.Health,
        "manaregen" or "manaregeneration" or "mpregen" => GearResourceStat.Mana,
        "bloodregen" or "bloodregeneration" => GearResourceStat.Blood,
        "magicshieldregen" or "magicshieldregeneration" or "shieldregen" or "shieldregeneration" => GearResourceStat.MagicShield,
        _ => GearResourceStat.None
    };

    /// <summary>Damage modifiers match the skill's damage type or an exact authored tag.</summary>
    public static HashSet<string> ApplicableDamageStats(SkillDefinition skill)
    {
        var result = new HashSet<string>(StringComparer.Ordinal)
        {
            "damage", "total_damage", "skill_damage"
        };
        string damageType = NormalizeStat(skill.Damage.Type);
        if (damageType.Length > 0)
        {
            result.Add(damageType + "_damage");
            result.Add("all_" + damageType + "_damage");
        }
        if (damageType is "fire" or "cold" or "lightning" or "rust")
        {
            result.Add("elemental_damage");
            result.Add("all_elemental_damage");
        }
        var tags = new HashSet<string>(StringComparer.Ordinal);
        foreach (string value in skill.Tags)
        {
            string tag = NormalizeStat(value);
            if (tag.Length == 0) continue;
            tags.Add(tag);
            result.Add(tag + "_damage");
        }
        if (tags.Contains("area")) result.Add("area_dmg");
        if (tags.Contains("spell") && damageType.Length > 0)
        {
            result.Add("spell_" + damageType + "_damage");
            result.Add(damageType + "_spell_damage");
        }
        if (tags.Contains("attack") && damageType.Length > 0)
            result.Add("bonus_" + damageType + "_attack_damage");
        return result;
    }

    public static string NormalizeStat(string? stat)
    {
        string value = (stat ?? "").Trim().ToLowerInvariant();
        int separator = value.IndexOf(':');
        if (separator >= 0) value = value.Substring(separator + 1);
        return value.Replace('-', '_').Replace(' ', '_');
    }

    private static bool IsDamageStat(string stat)
    {
        string code = NormalizeStat(stat);
        // Any nonempty tag has a real consumer through ApplicableDamageStats; other stat families do not.
        return code is "damage" or "area_dmg"
            || code.Length > "_damage".Length && code.EndsWith("_damage", StringComparison.Ordinal);
    }

    private static string CompactResourceStat(string? stat)
    {
        string value = (stat ?? "").Trim().ToLowerInvariant();
        if (value.StartsWith("vrpg:", StringComparison.Ordinal)) value = value.Substring(5);
        return value.Replace("_", "").Replace("-", "").Replace(" ", "");
    }
}

public enum GearResourceStat { None, Health, Mana, Blood, MagicShield }
