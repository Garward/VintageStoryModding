using System;
using System.Collections.Generic;
using VRPG.Data.Definitions;
using VRPG.Modules.Rpg.Players;
using VRPG.Modules.Rpg.Talents;

namespace VRPG.Modules.Rpg.Stats;

/// <summary>Resolves server-authoritative skill damage from skill level, attributes, and allocated talents.</summary>
public sealed class SkillDamageResolver
{
    private readonly TalentTreeCatalog talents;

    public SkillDamageResolver(TalentTreeCatalog talents)
    {
        this.talents = talents;
    }

    public float Resolve(RpgPlayerState state, SkillDefinition skill, int skillLevel)
    {
        var applicableStats = BuildApplicableStats(skill);
        var modifiers = new StatModifierAccumulator();

        AddAttributeScaling(modifiers, state, skill);
        for (int talentIndex = 0; talentIndex < state.Talents.Count; talentIndex++)
        {
            TalentNodeDefinition? talent = talents.Get(state.Talents[talentIndex]);
            if (talent == null)
            {
                continue;
            }

            for (int modifierIndex = 0; modifierIndex < talent.Modifiers.Length; modifierIndex++)
            {
                StatModifierDefinition modifier = talent.Modifiers[modifierIndex];
                if (!applicableStats.Contains(NormalizeStatCode(modifier.Stat)))
                {
                    continue;
                }

                modifiers.Add(modifier.Operation, Midpoint(modifier));
            }
        }

        double resolved = modifiers.Resolve(skill.DamageAtLevel(skillLevel));
        return (float)Math.Min(float.MaxValue, resolved);
    }

    private static HashSet<string> BuildApplicableStats(SkillDefinition skill)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "damage",
            "total_damage",
            "skill_damage"
        };

        string damageType = NormalizeStatCode(skill.Damage.Type);
        if (!string.IsNullOrWhiteSpace(damageType))
        {
            result.Add(damageType + "_damage");
            result.Add("all_" + damageType + "_damage");
        }

        bool elemental = damageType is "fire" or "cold" or "lightning" or "rust";
        if (elemental)
        {
            result.Add("elemental_damage");
            result.Add("all_elemental_damage");
        }

        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < skill.Tags.Length; i++)
        {
            string tag = NormalizeStatCode(skill.Tags[i]);
            if (string.IsNullOrWhiteSpace(tag))
            {
                continue;
            }

            tags.Add(tag);
            result.Add(tag + "_damage");
        }

        if (tags.Contains("area"))
        {
            result.Add("area_dmg");
        }

        if (tags.Contains("spell") && !string.IsNullOrWhiteSpace(damageType))
        {
            result.Add("spell_" + damageType + "_damage");
            result.Add(damageType + "_spell_damage");
        }

        if (tags.Contains("attack") && !string.IsNullOrWhiteSpace(damageType))
        {
            result.Add("bonus_" + damageType + "_attack_damage");
        }

        return result;
    }

    private static void AddAttributeScaling(StatModifierAccumulator modifiers, RpgPlayerState state, SkillDefinition skill)
    {
        // These values mirror the player-facing core attribute definitions.
        modifiers.Add("increased", GetBaseStat(state, "strength") * 0.1);
        if (HasTag(skill, "projectile"))
        {
            modifiers.Add("increased", GetBaseStat(state, "dexterity") * 0.2);
        }

        if (HasTag(skill, "spell"))
        {
            modifiers.Add("increased", GetBaseStat(state, "intelligence") * 0.2);
        }
    }

    private static bool HasTag(SkillDefinition skill, string expected)
    {
        for (int i = 0; i < skill.Tags.Length; i++)
        {
            if (string.Equals(NormalizeStatCode(skill.Tags[i]), expected, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static int GetBaseStat(RpgPlayerState state, string code)
    {
        if (state.BaseStats.TryGetValue(code, out int value))
        {
            return value;
        }

        return state.BaseStats.TryGetValue("vrpg:" + code, out value) ? value : 0;
    }

    private static float Midpoint(StatModifierDefinition modifier)
    {
        return modifier.Max != 0f || modifier.Min != 0f
            ? (modifier.Min + modifier.Max) / 2f
            : 0f;
    }

    private static string NormalizeStatCode(string? code)
    {
        string value = (code ?? "").Trim().ToLowerInvariant();
        int separator = value.IndexOf(':');
        if (separator >= 0)
        {
            value = value.Substring(separator + 1);
        }

        return value.Replace('-', '_').Replace(' ', '_');
    }
}
