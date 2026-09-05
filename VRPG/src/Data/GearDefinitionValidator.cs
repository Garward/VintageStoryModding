using System;
using System.Collections.Generic;
using System.Linq;
using VRPG.Data.Definitions;
using VRPG.Inventory;
using VRPG.Inventory.Gear;

namespace VRPG.Data;

/// <summary>Validates the complete data-owned gear definition graph before it is used by authoring.</summary>
public static class GearDefinitionValidator
{
    private const int MaximumAffixes = 6;
    private const int MaximumRequiredLevel = 100;

    private static readonly (string Code, float WeaponPowerScalar, int? MinAffixes, int? MaxAffixes)[] RequiredRaritySeeds =
    {
        ("vrpg:common", 1.00f, 0, 1),
        ("vrpg:rare", 1.15f, 3, 5),
        ("vrpg:unique", 1.20f, null, null)
    };

    public static void ValidateOrThrow(
        IReadOnlyCollection<GearBaseDefinition> bases,
        IReadOnlyCollection<GearRarityDefinition> rarities,
        IReadOnlyCollection<GearAffixDefinition> affixes,
        Func<string, bool> chassisExists)
    {
        string[] errors = Validate(bases, rarities, affixes, chassisExists);
        if (errors.Length > 0)
        {
            throw new InvalidOperationException(
                "VRPG gear data validation failed:\n- " + string.Join("\n- ", errors));
        }
    }

    public static string[] Validate(
        IReadOnlyCollection<GearBaseDefinition> bases,
        IReadOnlyCollection<GearRarityDefinition> rarities,
        IReadOnlyCollection<GearAffixDefinition> affixes,
        Func<string, bool> chassisExists)
    {
        ArgumentNullException.ThrowIfNull(bases);
        ArgumentNullException.ThrowIfNull(rarities);
        ArgumentNullException.ThrowIfNull(affixes);
        ArgumentNullException.ThrowIfNull(chassisExists);

        CanonicalizeDefinitions(bases, rarities, affixes);
        var errors = new List<string>();
        Dictionary<string, GearBaseDefinition> basesByCode = Index(bases, "gear base", errors);
        Dictionary<string, GearRarityDefinition> raritiesByCode = Index(rarities, "rarity", errors);
        Index(affixes, "affix", errors);

        ValidateRarities(rarities, raritiesByCode, errors);
        ValidateBases(bases, chassisExists, errors);
        ValidateUniqueIdentities(basesByCode.Values, raritiesByCode, errors);
        ValidateAffixes(affixes, bases, raritiesByCode, errors);

        return errors.ToArray();
    }

    private static Dictionary<string, T> Index<T>(
        IEnumerable<T> records,
        string kind,
        ICollection<string> errors)
        where T : class, IVrpgDataRecord
    {
        var indexed = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
        foreach (T record in records)
        {
            if (record == null)
            {
                errors.Add($"{kind} record cannot be null.");
                continue;
            }

            string code = NormalizeCode(record.Code);
            if (!IsNamespacedCode(code))
            {
                errors.Add($"{kind} '{DisplayCode(record.Code)}' must have a code.");
                continue;
            }

            if (!indexed.TryAdd(code, record))
            {
                errors.Add($"duplicate {kind} code '{code}'.");
            }
        }

        return indexed;
    }

    private static void ValidateRarities(
        IEnumerable<GearRarityDefinition> rarities,
        IReadOnlyDictionary<string, GearRarityDefinition> raritiesByCode,
        ICollection<string> errors)
    {
        foreach (GearRarityDefinition rarity in rarities)
        {
            if (rarity == null) continue;
            string label = "rarity '" + DisplayCode(rarity.Code) + "'";
            if (string.IsNullOrWhiteSpace(rarity.Name)) errors.Add(label + " requires a name.");
            if (rarity.Weight <= 0) errors.Add(label + " weight must be positive.");
            if (rarity.MinAffixes < 0 || rarity.MaxAffixes < rarity.MinAffixes || rarity.MaxAffixes > MaximumAffixes)
            {
                errors.Add(label + $" affix caps must be ordered from 0 through {MaximumAffixes}.");
            }
            if (!float.IsFinite(rarity.StatScalar) || rarity.StatScalar <= 0f)
            {
                errors.Add(label + " statScalar must be finite and positive.");
            }
            if (!float.IsFinite(rarity.WeaponPowerScalar) || rarity.WeaponPowerScalar <= 0f)
            {
                errors.Add(label + " weaponPowerScalar must be finite and positive.");
            }
        }

        foreach ((string code, float scalar, int? minAffixes, int? maxAffixes) in RequiredRaritySeeds)
        {
            if (!raritiesByCode.TryGetValue(code, out GearRarityDefinition? rarity))
            {
                errors.Add($"missing required rarity seed '{code}'.");
            }
            else if (rarity.WeaponPowerScalar != scalar)
            {
                errors.Add($"rarity '{code}' must use weaponPowerScalar {scalar:0.00}.");
            }
            else if (minAffixes is int expectedMinimum && maxAffixes is int expectedMaximum
                && (rarity.MinAffixes != expectedMinimum || rarity.MaxAffixes != expectedMaximum))
            {
                errors.Add($"rarity '{code}' must use the canonical {expectedMinimum}-{expectedMaximum} affix profile.");
            }
        }

        if (raritiesByCode.TryGetValue("vrpg:unique", out GearRarityDefinition? unique) && !unique.UniqueLike)
        {
            errors.Add("rarity 'vrpg:unique' must require authored identity support.");
        }
    }

    private static void ValidateBases(
        IEnumerable<GearBaseDefinition> bases,
        Func<string, bool> chassisExists,
        ICollection<string> errors)
    {
        foreach (GearBaseDefinition definition in bases)
        {
            if (definition == null) continue;
            string label = "gear base '" + DisplayCode(definition.Code) + "'";
            if (string.IsNullOrWhiteSpace(definition.Name)) errors.Add(label + " requires a name.");
            if (!PlayerEquipmentRoles.BuiltIn.Contains(definition.EquipmentRole ?? ""))
            {
                errors.Add(label + " has an unknown equipment role.");
            }
            if (string.IsNullOrWhiteSpace(definition.Family)) errors.Add(label + " requires a family.");
            ValidateValues(label, "tag", definition.Tags, requireAny: true, errors);
            ValidateValues(label, "affix family", definition.AffixFamilies, requireAny: true, errors);

            string chassis = NormalizeCode(definition.VisualChassis);
            if (!IsNamespacedCode(chassis) || !chassisExists(chassis))
            {
                errors.Add(label + " references an unavailable visual chassis '" + DisplayCode(definition.VisualChassis) + "'.");
            }

            ValidateRoleProfile(label, definition, errors);
            ValidateModifiers(label + " implicit", definition.Implicits, requireAny: false, errors);
            if (!string.IsNullOrWhiteSpace(definition.UniqueCode) && !IsNamespacedCode(NormalizeCode(definition.UniqueCode)))
            {
                errors.Add(label + " uniqueCode must be namespaced when supplied.");
            }
        }
    }

    private static void ValidateRoleProfile(string label, GearBaseDefinition definition, ICollection<string> errors)
    {
        if (!float.IsFinite(definition.LevelOneWeaponPower) || definition.LevelOneWeaponPower < 0f)
        {
            errors.Add(label + " levelOneWeaponPower must be finite and non-negative.");
        }
        if (!float.IsFinite(definition.LevelOneArmor) || definition.LevelOneArmor < 0f)
        {
            errors.Add(label + " levelOneArmor must be finite and non-negative.");
        }

        string role = definition.EquipmentRole ?? "";
        bool weapon = role.StartsWith("weapon.", StringComparison.OrdinalIgnoreCase);
        bool armor = role.StartsWith("armor.", StringComparison.OrdinalIgnoreCase);
        if (!weapon && definition.LevelOneWeaponPower != 0f)
        {
            errors.Add(label + " may use Weapon Power only on a weapon equipment role.");
        }
        if (!armor && definition.LevelOneArmor != 0f)
        {
            errors.Add(label + " may use Armor only on an armor equipment role.");
        }
    }

    private static void ValidateUniqueIdentities(
        IEnumerable<GearBaseDefinition> bases,
        IReadOnlyDictionary<string, GearRarityDefinition> rarities,
        ICollection<string> errors)
    {
        var uniqueCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool hasAuthoredIdentity = false;
        foreach (GearBaseDefinition definition in bases)
        {
            if (definition == null || string.IsNullOrWhiteSpace(definition.UniqueCode)) continue;
            hasAuthoredIdentity = true;
            string code = NormalizeCode(definition.UniqueCode);
            if (!uniqueCodes.Add(code)) errors.Add($"duplicate authored unique identity '{code}'.");
        }

        if (bases.Any() && rarities.TryGetValue("vrpg:unique", out GearRarityDefinition? unique)
            && unique.UniqueLike && !hasAuthoredIdentity)
        {
            errors.Add("rarity 'vrpg:unique' requires at least one authored identity on a gear base.");
        }
    }

    private static void ValidateAffixes(
        IEnumerable<GearAffixDefinition> affixes,
        IReadOnlyCollection<GearBaseDefinition> bases,
        IReadOnlyDictionary<string, GearRarityDefinition> rarities,
        ICollection<string> errors)
    {
        var supportedFamilies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (GearBaseDefinition definition in bases)
        {
            foreach (string family in definition?.AffixFamilies ?? Array.Empty<string>())
            {
                if (!string.IsNullOrWhiteSpace(family)) supportedFamilies.Add(family.Trim());
            }
        }

        foreach (GearAffixDefinition affix in affixes)
        {
            if (affix == null) continue;
            string label = "affix '" + DisplayCode(affix.Code) + "'";
            if (string.IsNullOrWhiteSpace(affix.Name)) errors.Add(label + " requires a name.");
            if (string.IsNullOrWhiteSpace(affix.Family))
            {
                errors.Add(label + " requires a family.");
            }
            else if (bases.Count > 0 && !supportedFamilies.Contains(affix.Family.Trim()))
            {
                errors.Add(label + " family is not supported by any gear base.");
            }

            if (bases.Count > 0 && !string.IsNullOrWhiteSpace(affix.Family)
                && !HasCompatibleBase(affix, bases))
            {
                errors.Add(label + " has no compatible gear base for its family and tags.");
            }

            ValidateValues(label, "required tag", affix.RequiredTags, requireAny: true, errors);
            ValidateValues(label, "excluded tag", affix.ExcludedTags, requireAny: false, errors);
            ValidateRequiredExcludedOverlap(label, affix.RequiredTags, affix.ExcludedTags, errors);
            ValidateEquipmentRoles(label, affix.AllowedEquipmentRoles, affix.ExcludedEquipmentRoles, errors);
            if (string.IsNullOrWhiteSpace(affix.MutualExclusionGroup))
            {
                errors.Add(label + " requires an exclusion group.");
            }

            string minimumRarity = NormalizeCode(affix.MinimumRarityCode);
            if (!rarities.ContainsKey(minimumRarity))
            {
                errors.Add(label + " references an unknown minimum rarity '" + DisplayCode(affix.MinimumRarityCode) + "'.");
            }

            ValidateTiers(label, affix.Tiers, errors);
        }
    }

    private static void ValidateTiers(string label, GearAffixTierDefinition[]? tiers, ICollection<string> errors)
    {
        GearAffixTierDefinition[] values = tiers ?? Array.Empty<GearAffixTierDefinition>();
        if (values.Length == 0)
        {
            errors.Add(label + " requires at least one tier.");
            return;
        }

        int previousTier = 0;
        int previousMaximum = 0;
        for (int index = 0; index < values.Length; index++)
        {
            GearAffixTierDefinition? tier = values[index];
            if (tier == null)
            {
                errors.Add(label + $" tier[{index}] cannot be null.");
                continue;
            }

            string tierLabel = label + $" tier {tier.Tier}";
            if (tier.Tier <= previousTier)
            {
                errors.Add(tierLabel + " must be ordered by an increasing tier number.");
            }
            previousTier = Math.Max(previousTier, tier.Tier);
            if (tier.MinimumRequiredLevel < 1 || tier.MaximumRequiredLevel < tier.MinimumRequiredLevel
                || tier.MaximumRequiredLevel > MaximumRequiredLevel)
            {
                errors.Add(tierLabel + $" must cover an ordered level range from 1 through {MaximumRequiredLevel}.");
            }
            if (index > 0 && tier.MinimumRequiredLevel <= previousMaximum)
            {
                errors.Add(tierLabel + " has a level overlap with the preceding tier.");
            }
            previousMaximum = Math.Max(previousMaximum, tier.MaximumRequiredLevel);
            ValidateModifiers(tierLabel, tier.Modifiers, requireAny: true, errors);
        }
    }

    private static bool HasCompatibleBase(GearAffixDefinition affix, IEnumerable<GearBaseDefinition> bases)
    {
        var required = new HashSet<string>(
            (affix.RequiredTags ?? Array.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim()),
            StringComparer.OrdinalIgnoreCase);
        var excluded = new HashSet<string>(
            (affix.ExcludedTags ?? Array.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim()),
            StringComparer.OrdinalIgnoreCase);

        foreach (GearBaseDefinition definition in bases)
        {
            if (definition == null
                || !(definition.AffixFamilies ?? Array.Empty<string>()).Any(family =>
                    string.Equals(family?.Trim(), affix.Family.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var tags = new HashSet<string>(
                (definition.Tags ?? Array.Empty<string>())
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => value.Trim()),
                StringComparer.OrdinalIgnoreCase);
            if (required.IsSubsetOf(tags) && !excluded.Overlaps(tags) && IsRoleCompatible(affix, definition.EquipmentRole)) return true;
        }

        return false;
    }

    private static void ValidateEquipmentRoles(
        string label,
        string[]? allowedRoles,
        string[]? excludedRoles,
        ICollection<string> errors)
    {
        ValidateValues(label, "allowed equipment role", allowedRoles, requireAny: false, errors);
        ValidateValues(label, "excluded equipment role", excludedRoles, requireAny: false, errors);
        var allowed = new HashSet<string>(allowedRoles ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        foreach (string role in allowed)
        {
            if (!string.IsNullOrWhiteSpace(role) && !PlayerEquipmentRoles.BuiltIn.Contains(role.Trim()))
            {
                errors.Add(label + " has an unknown allowed equipment role '" + role.Trim() + "'.");
            }
        }
        foreach (string role in excludedRoles ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(role)) continue;
            string trimmed = role.Trim();
            if (!PlayerEquipmentRoles.BuiltIn.Contains(trimmed))
            {
                errors.Add(label + " has an unknown excluded equipment role '" + trimmed + "'.");
            }
            else if (allowed.Contains(trimmed))
            {
                errors.Add(label + " cannot both allow and exclude equipment role '" + trimmed + "'.");
            }
        }
    }

    private static bool IsRoleCompatible(GearAffixDefinition affix, string? equipmentRole)
    {
        string role = equipmentRole?.Trim() ?? "";
        string[] allowed = affix.AllowedEquipmentRoles ?? Array.Empty<string>();
        string[] excluded = affix.ExcludedEquipmentRoles ?? Array.Empty<string>();
        bool allowedByList = allowed.Length == 0 || allowed.Any(value => string.Equals(value?.Trim(), role, StringComparison.OrdinalIgnoreCase));
        return allowedByList && !excluded.Any(value => string.Equals(value?.Trim(), role, StringComparison.OrdinalIgnoreCase));
    }

    private static void CanonicalizeDefinitions(
        IEnumerable<GearBaseDefinition> bases,
        IEnumerable<GearRarityDefinition> rarities,
        IEnumerable<GearAffixDefinition> affixes)
    {
        foreach (GearBaseDefinition definition in bases)
        {
            if (definition == null) continue;
            definition.Code = NormalizeCode(definition.Code);
            definition.UniqueCode = NormalizeOptionalCode(definition.UniqueCode);
            CanonicalizeModifierStats(definition.Implicits);
        }
        foreach (GearRarityDefinition rarity in rarities)
        {
            if (rarity != null) rarity.Code = NormalizeCode(rarity.Code);
        }
        foreach (GearAffixDefinition affix in affixes)
        {
            if (affix == null) continue;
            affix.Code = NormalizeCode(affix.Code);
            affix.MinimumRarityCode = NormalizeCode(affix.MinimumRarityCode);
            foreach (GearAffixTierDefinition tier in affix.Tiers ?? Array.Empty<GearAffixTierDefinition>())
            {
                if (tier != null) CanonicalizeModifierStats(tier.Modifiers);
            }
        }
    }

    private static void CanonicalizeModifierStats(StatModifierDefinition[]? modifiers)
    {
        foreach (StatModifierDefinition modifier in modifiers ?? Array.Empty<StatModifierDefinition>())
        {
            if (modifier != null && !string.IsNullOrWhiteSpace(modifier.Stat))
            {
                modifier.Stat = NormalizeCode(modifier.Stat);
            }
        }
    }

    private static void ValidateModifiers(
        string label,
        StatModifierDefinition[]? modifiers,
        bool requireAny,
        ICollection<string> errors)
    {
        StatModifierDefinition[] values = modifiers ?? Array.Empty<StatModifierDefinition>();
        if (requireAny && values.Length == 0)
        {
            errors.Add(label + " requires at least one modifier.");
            return;
        }

        for (int index = 0; index < values.Length; index++)
        {
            StatModifierDefinition? modifier = values[index];
            if (modifier == null)
            {
                errors.Add(label + $" modifier[{index}] cannot be null.");
                continue;
            }

            string modifierLabel = label + $" modifier[{index}]";
            if (string.IsNullOrWhiteSpace(modifier.Stat)) errors.Add(modifierLabel + " requires a stat.");
            if (!GearModifierOperations.IsSupported(modifier.Operation))
            {
                errors.Add(modifierLabel + " has an unsupported operation.");
            }
            if (!float.IsFinite(modifier.Min) || !float.IsFinite(modifier.Max))
            {
                errors.Add(modifierLabel + " rolls must be finite.");
            }
            else if (modifier.Max < modifier.Min)
            {
                errors.Add(modifierLabel + " rolls must be ordered.");
            }
        }
    }

    private static void ValidateValues(
        string label,
        string kind,
        string[]? values,
        bool requireAny,
        ICollection<string> errors)
    {
        string[] entries = values ?? Array.Empty<string>();
        if (requireAny && entries.Length == 0)
        {
            errors.Add(label + " requires at least one " + kind + ".");
            return;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string value in entries)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                errors.Add(label + " has a missing " + kind + ".");
            }
            else if (!seen.Add(value.Trim()))
            {
                errors.Add(label + " has a duplicate " + kind + " '" + value.Trim() + "'.");
            }
        }
    }

    private static void ValidateRequiredExcludedOverlap(
        string label,
        string[]? required,
        string[]? excluded,
        ICollection<string> errors)
    {
        var requiredValues = new HashSet<string>(
            (required ?? Array.Empty<string>()).Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()),
            StringComparer.OrdinalIgnoreCase);
        foreach (string excludedTag in excluded ?? Array.Empty<string>())
        {
            if (!string.IsNullOrWhiteSpace(excludedTag) && requiredValues.Contains(excludedTag.Trim()))
            {
                errors.Add(label + " cannot both require and exclude tag '" + excludedTag.Trim() + "'.");
            }
        }
    }

    private static string NormalizeCode(string? code)
    {
        string value = (code ?? "").Trim();
        return value.Length == 0 || value.Contains(':') ? value : "vrpg:" + value;
    }

    private static string NormalizeOptionalCode(string? code)
    {
        return string.IsNullOrWhiteSpace(code) ? "" : NormalizeCode(code);
    }

    private static bool IsNamespacedCode(string code)
    {
        int separator = code.IndexOf(':');
        return separator > 0 && separator < code.Length - 1;
    }

    private static string DisplayCode(string? code) => string.IsNullOrWhiteSpace(code) ? "<missing code>" : code.Trim();
}
