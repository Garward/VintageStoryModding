using System;
using System.Collections.Generic;
using VRPG.Data.Definitions;

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

    private static readonly HashSet<string> EquipmentRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "mainweapon",
        "armorhead",
        "armorbody",
        "armorlegs"
    };

    public static void Validate(
        ClassDefinition definition,
        IReadOnlyList<SkillDefinition> skills,
        Func<string, bool> assetExists,
        ICollection<string> errors)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(skills);
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

        ValidateItem(label, starter.Weapon, "mainweapon", assetExists, errors);

        StarterItemDefinition[] armor = starter.Armor ?? Array.Empty<StarterItemDefinition>();
        if (armor.Length == 0)
        {
            errors.Add(label + ": starter must include at least one armor item.");
        }
        for (int i = 0; i < armor.Length; i++)
        {
            ValidateItem(label + $": starter armor[{i}]", armor[i], null, assetExists, errors);
        }

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

    private static void ValidateItem(
        string label,
        StarterItemDefinition? item,
        string? requiredRole,
        Func<string, bool> assetExists,
        ICollection<string> errors)
    {
        if (item == null)
        {
            errors.Add(label + ": starter item is required.");
            return;
        }

        string role = (item.EquipmentRole ?? "").Trim();
        if (string.IsNullOrWhiteSpace(item.Name))
        {
            errors.Add(label + ": starter item name is required for the grant preview.");
        }
        if (!EquipmentRoles.Contains(role))
        {
            errors.Add(label + ": starter item equipmentRole is not supported.");
        }
        else if (requiredRole != null && !string.Equals(role, requiredRole, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add(label + ": starter weapon must use the mainweapon equipment role.");
        }

        string code = NormalizeCode(item.Code, "game");
        if (code.Length == 0 || !assetExists(code))
        {
            errors.Add(label + ": starter item asset does not exist: " + item.Code + ".");
        }
    }

    private static string NormalizeCode(string? code, string defaultDomain)
    {
        string value = (code ?? "").Trim();
        return value.Length == 0 || value.Contains(':') ? value : defaultDomain + ":" + value;
    }
}
