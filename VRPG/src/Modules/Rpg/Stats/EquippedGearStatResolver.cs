using System;
using System.Collections.Generic;
using System.Linq;
using VRPG.Data.Definitions;
using VRPG.Inventory.Gear;

namespace VRPG.Modules.Rpg.Stats;

/// <summary>Immutable equipped values; mutable definition objects are copied at the boundary.</summary>
public sealed record EquippedGearStats
{
    private readonly StatModifierDefinition[] modifiers;

    public EquippedGearStats(float weaponPower, float armor, float maximumHealth, float maximumMagicShield,
        float evasion, float fireResistance, float coldResistance, float lightningResistance, float rustResistance,
        StatModifierDefinition[] modifiers, bool hasMainWeapon = false, bool valid = true)
    {
        WeaponPower = weaponPower;
        Armor = armor;
        MaximumHealth = maximumHealth;
        MaximumMagicShield = maximumMagicShield;
        Evasion = evasion;
        FireResistance = fireResistance;
        ColdResistance = coldResistance;
        LightningResistance = lightningResistance;
        RustResistance = rustResistance;
        this.modifiers = Copy(modifiers);
        HasMainWeapon = hasMainWeapon;
        Valid = valid;
    }

    public static EquippedGearStats Empty { get; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0, Array.Empty<StatModifierDefinition>());
    public static EquippedGearStats Invalid { get; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0, Array.Empty<StatModifierDefinition>(), valid: false);
    public bool Valid { get; }
    public bool HasMainWeapon { get; }
    public float WeaponPower { get; }
    public float Armor { get; }
    public float MaximumHealth { get; }
    public float MaximumMagicShield { get; }
    public float Evasion { get; }
    public float FireResistance { get; }
    public float ColdResistance { get; }
    public float LightningResistance { get; }
    public float RustResistance { get; }
    public StatModifierDefinition[] Modifiers => Copy(modifiers);
    public float AttackWeaponPower => !Valid ? 0f : HasMainWeapon ? WeaponPower : 10f;

    private static StatModifierDefinition[] Copy(StatModifierDefinition[] source) => source.Select(value => new StatModifierDefinition
    {
        Stat = value.Stat, Operation = value.Operation, Min = value.Min, Max = value.Max
    }).ToArray();
}

/// <summary>Replaces one cached immutable total per player whenever item-store revision changes.</summary>
public sealed class EquippedGearStatResolver
{
    private readonly Dictionary<string, (long Revision, EquippedGearStats Stats)> cache = new(StringComparer.OrdinalIgnoreCase);

    public EquippedGearStats Resolve(string playerUid, long revision, GearEquipmentSnapshot snapshot)
    {
        // Validate before the cache lookup: an invalid snapshot must never expose an older valid result.
        if (string.IsNullOrWhiteSpace(playerUid) || snapshot == null || !snapshot.Valid || revision != snapshot.Revision
            || !Validate(snapshot.Entries))
        {
            if (!string.IsNullOrWhiteSpace(playerUid)) cache.Remove(playerUid);
            return EquippedGearStats.Invalid;
        }

        if (cache.TryGetValue(playerUid, out var cached) && cached.Revision == revision) return cached.Stats;

        double[] sums = new double[9];
        bool main = false;
        var modifiers = new List<StatModifierDefinition>();
        foreach (GearEquipmentEntry entry in snapshot.Entries)
        {
            GearResolvedStats stats = entry.Instance.Resolved;
            if (entry.RoleCode == "weapon.main")
            {
                sums[0] = stats.WeaponPower;
                main = true;
            }
            float[] values = Values(stats);
            for (int i = 1; i < sums.Length; i++) sums[i] += values[i];
            foreach (GearModifierRoll modifier in entry.Instance.Affixes.SelectMany(affix => affix.Modifiers))
            {
                // These nine stats were permanently resolved by GearInstanceAuthor, including all layers.
                if (IsBaked(modifier.Stat) || !IsDownstream(modifier.Stat)) continue;
                modifiers.Add(new StatModifierDefinition
                {
                    Stat = modifier.Stat, Operation = modifier.Operation, Min = modifier.Value, Max = modifier.Value
                });
            }
        }

        var result = sums.Any(value => value > float.MaxValue)
            ? EquippedGearStats.Invalid
            : new EquippedGearStats((float)sums[0], (float)sums[1], (float)sums[2], (float)sums[3],
                (float)sums[4], (float)sums[5], (float)sums[6], (float)sums[7], (float)sums[8], modifiers.ToArray(), main);
        cache[playerUid] = (revision, result);
        return result;
    }

    private static bool Validate(GearEquipmentEntry[] entries)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var roles = new HashSet<string>(StringComparer.Ordinal);
        foreach (GearEquipmentEntry entry in entries)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.EntryId) || !ids.Add(entry.EntryId)
                || string.IsNullOrWhiteSpace(entry.RoleCode) || !roles.Add(entry.RoleCode)
                || entry.Instance == null || entry.Instance.EquipmentRole != entry.RoleCode
                || entry.Instance.SchemaVersion != GearInstanceSchema.CurrentVersion
                || entry.Instance.GenerationRuleVersion is <= 0 or > GearInstanceSchema.CurrentGenerationRules
                || entry.Instance.RequiredLevel is < 1 or > 100
                || !Normalized(entry.Instance.BaseCode) || !Normalized(entry.Instance.RarityCode)
                || !Normalized(entry.Instance.VisualChassisAssetCode) || !Normalized(entry.RoleCode)
                || entry.Instance.Resolved == null
                || Values(entry.Instance.Resolved).Any(value => !float.IsFinite(value) || value < 0)) return false;
            var affixCodes = new HashSet<string>(StringComparer.Ordinal);
            foreach (GearAffixRoll affix in entry.Instance.Affixes)
            {
                if (!Normalized(affix.AffixCode) || affix.Tier <= 0 || !affixCodes.Add(affix.AffixCode)) return false;
                foreach (GearModifierRoll modifier in affix.Modifiers)
                {
                    if (!Normalized(modifier.Stat) || !float.IsFinite(modifier.Value)
                        || !GearModifierOperations.IsSupported(modifier.Operation)) return false;
                }
            }
        }
        return true;
    }

    private static float[] Values(GearResolvedStats stats) => new[] { stats.WeaponPower, stats.Armor,
        stats.MaximumHealth, stats.MaximumMagicShield, stats.Evasion, stats.FireResistance,
        stats.ColdResistance, stats.LightningResistance, stats.RustResistance };

    private static bool IsBaked(string stat) => stat is "vrpg:weapon_power" or "vrpg:armor"
        or "vrpg:max_health" or "vrpg:max_magic_shield" or "vrpg:evasion" or "vrpg:fire_resistance"
        or "vrpg:cold_resistance" or "vrpg:lightning_resistance" or "vrpg:rust_resistance";

    private static bool Normalized(string? code) => !string.IsNullOrWhiteSpace(code)
        && code == code.Trim().ToLowerInvariant();

    private static bool IsDownstream(string stat)
    {
        string code = stat.Substring(stat.IndexOf(':') + 1);
        // Damage categories are selected by skill tags/type. Resource categories match the calculator.
        if (code == "damage" || code == "area_dmg" || code.EndsWith("_damage", StringComparison.Ordinal)) return true;
        return code.Replace("_", "").Replace("-", "") is "mana" or "maxmana" or "blood" or "maxblood"
            or "healthregen" or "healthregeneration" or "hpregen" or "manaregen" or "manaregeneration"
            or "mpregen" or "bloodregen" or "bloodregeneration" or "magicshieldregen" or "magicshieldregeneration"
            or "shieldregen" or "shieldregeneration";
    }
}
