using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using VRPG.Data.Definitions;
using VRPG.Inventory;

namespace VRPG.Data;

/// <summary>Validates starter previews and grants without executing class-specific code.</summary>
public static class StarterLoadoutDefinitionValidator
{
    private static readonly HashSet<string> PrimaryAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "strength",
        "dexterity",
        "intelligence"
    };

    private static readonly HashSet<string> EquipmentRoles = new(StringComparer.Ordinal)
    {
        PlayerEquipmentRoles.MainWeapon, PlayerEquipmentRoles.Head, PlayerEquipmentRoles.Body,
        PlayerEquipmentRoles.Legs, PlayerEquipmentRoles.Hands, PlayerEquipmentRoles.Feet
    };

    private static readonly HashSet<string> AuthoringOverrides = new(StringComparer.OrdinalIgnoreCase)
    {
        "level", "requiredLevel", "rarity", "rarityCode", "affixes", "setBonus", "setBonuses", "setCode"
    };

    public static void Validate(
        ClassDefinition definition,
        IReadOnlyList<SkillDefinition> skills,
        IReadOnlyList<GearBaseDefinition> bases,
        Func<string, bool> assetExists,
        ICollection<string> errors)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(skills);
        ArgumentNullException.ThrowIfNull(bases);
        ArgumentNullException.ThrowIfNull(assetExists);
        ArgumentNullException.ThrowIfNull(errors);

        StarterLoadoutDefinition starter = definition.Starter
            ?? throw new ArgumentException("The class has no starter definition.", nameof(definition));
        string label = string.IsNullOrWhiteSpace(definition.Code) ? "<missing class code>" : definition.Code;

        if (starter.GrantSchemaVersion < 1)
        {
            errors.Add(label + ": starter grantSchemaVersion must be at least 1.");
        }
        if (string.IsNullOrWhiteSpace(starter.CombatPattern))
        {
            errors.Add(label + ": starter combatPattern is required for the choice screen.");
        }
        if (string.IsNullOrWhiteSpace(starter.DefensiveApproach))
        {
            errors.Add(label + ": starter defensiveApproach is required for the choice screen.");
        }
        if (!PrimaryAttributes.Contains(starter.PrimaryAttribute ?? ""))
        {
            errors.Add(label + ": starter primaryAttribute must be strength, dexterity, or intelligence.");
        }

        ValidateItems(label, starter, bases, assetExists, errors);

        string skillCode = NormalizeCode(starter.SkillCode, "vrpg");
        SkillDefinition? skill = null;
        for (int i = 0; i < skills.Count; i++)
        {
            if (string.Equals(NormalizeCode(skills[i].Code, "vrpg"), skillCode, StringComparison.OrdinalIgnoreCase))
            {
                skill = skills[i];
                break;
            }
        }

        if (skill == null)
        {
            errors.Add(label + ": starter references unknown skill " + starter.SkillCode + ".");
        }
        else
        {
            if (!string.Equals(
                    NormalizeCode(skill.ClassCode, "vrpg"),
                    NormalizeCode(definition.Code, "vrpg"),
                    StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(label + ": starter skill must belong to the same class.");
            }
            if (skill.RequiredLevel > 1)
            {
                errors.Add(label + ": starter skill must be usable at level 1.");
            }
        }
    }

    internal static void ValidateItems(
        string label,
        StarterLoadoutDefinition starter,
        IReadOnlyList<GearBaseDefinition> bases,
        Func<string, bool> assetExists,
        ICollection<string> errors)
    {
        ValidateOverrides(label, starter.ExtensionData, errors);
        var roles = new HashSet<string>(StringComparer.Ordinal);
        StarterItemDefinition[] items = starter.Items ?? Array.Empty<StarterItemDefinition>();
        if (items.Length != EquipmentRoles.Count)
            errors.Add(label + ": starter must contain exactly six items.");
        foreach (StarterItemDefinition? item in items)
        {
            if (item != null && !roles.Add(item.EquipmentRole ?? ""))
                errors.Add(label + ": starter contains duplicate role " + item.EquipmentRole + ".");
            ValidateItem(label, item, bases, assetExists, errors);
        }
        foreach (string role in EquipmentRoles)
        {
            if (!roles.Contains(role)) errors.Add(label + ": starter is missing role " + role + ".");
        }
    }

    private static void ValidateItem(
        string label,
        StarterItemDefinition? item,
        IReadOnlyList<GearBaseDefinition> bases,
        Func<string, bool> assetExists,
        ICollection<string> errors)
    {
        if (item == null)
        {
            errors.Add(label + ": starter item is required.");
            return;
        }

        ValidateOverrides(label, item.ExtensionData, errors);
        string role = item.EquipmentRole ?? "";
        if (!EquipmentRoles.Contains(role))
        {
            errors.Add(label + ": starter item equipmentRole is not supported.");
        }
        string code = NormalizeCode(item.GearBaseCode, "vrpg");
        if (code.StartsWith("game:", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add(label + ": starter must reference a VRPG gear base, not a raw game item.");
            return;
        }
        GearBaseDefinition? gearBase = bases.FirstOrDefault(candidate =>
            string.Equals(NormalizeCode(candidate.Code, "vrpg"), code, StringComparison.OrdinalIgnoreCase));
        if (gearBase == null)
        {
            errors.Add(label + ": starter references unknown gear base " + item.GearBaseCode + ".");
            return;
        }
        if (!string.Equals(gearBase.EquipmentRole, role, StringComparison.Ordinal))
            errors.Add(label + ": starter gear base role does not match " + role + ".");
        if (string.IsNullOrWhiteSpace(gearBase.VisualChassis) || !assetExists(gearBase.VisualChassis))
            errors.Add(label + ": starter chassis does not exist: " + gearBase.VisualChassis + ".");
    }

    private static void ValidateOverrides(string label, IDictionary<string, JToken>? fields, ICollection<string> errors)
    {
        if (fields == null) return;
        foreach (string field in fields.Keys)
        {
            if (AuthoringOverrides.Contains(field))
                errors.Add(label + ": starter " + field + " is not supported; starters are level 1 Common with no affixes or set bonuses.");
        }
    }

    private static string NormalizeCode(string? code, string defaultDomain)
    {
        string value = (code ?? "").Trim();
        return value.Length == 0 || value.Contains(':') ? value : defaultDomain + ":" + value;
    }
}
