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

    // Temporary preview compatibility until server composition supplies gear to RpgModule.BuildSkillState.
    // Authoritative damage callers must always pass the actual view, including Invalid.
    public float Resolve(RpgPlayerState state, SkillDefinition skill, int skillLevel) =>
        Resolve(state, skill, skillLevel, EquippedGearStats.Empty);

    public float Resolve(RpgPlayerState state, SkillDefinition skill, int skillLevel, EquippedGearStats gear)
    {
        if (!gear.Valid) return 0f;
        var applicableStats = BuildApplicableStats(skill);
        return ResolveDamage(state, skill, gear, applicableStats,
            gear.AttackWeaponPower * (double)skill.WeaponDamagePercentAtLevel(skillLevel) / 100.0);
    }

    public float ResolveAttack(RpgPlayerState state, bool projectile, EquippedGearStats gear)
    {
        if (!gear.Valid) return 0f;
        var attack = new SkillDefinition { Tags = projectile ? new[] { "attack", "projectile" } : new[] { "attack", "melee" } };
        var applicableStats = BuildApplicableStats(attack);
        applicableStats.Remove("skill_damage");
        return ResolveDamage(state, attack, gear, applicableStats, gear.AttackWeaponPower);
    }

    private float ResolveDamage(RpgPlayerState state, SkillDefinition skill, EquippedGearStats gear,
        HashSet<string> applicableStats, double baseDamage)
    {
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

                modifiers.Add(modifier.Operation, StatValueMath.ModifierValue(modifier));
            }
        }

        foreach (StatModifierDefinition modifier in gear.Modifiers)
        {
            if (applicableStats.Contains(NormalizeStatCode(modifier.Stat)))
                modifiers.Add(modifier.Operation, StatValueMath.ModifierValue(modifier));
        }

        double resolved = modifiers.Resolve(baseDamage);
        return StatValueMath.NonNegativeFloat(resolved);
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
