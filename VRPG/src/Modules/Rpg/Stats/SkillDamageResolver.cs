using System;
using System.Collections.Generic;
using VRPG.Data.Definitions;
using VRPG.Inventory.Gear;
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

    // Compatibility for callers explicitly calculating an unequipped character.
    // Server damage and preview callers must pass the actual view, including Invalid.
    public float Resolve(RpgPlayerState state, SkillDefinition skill, int skillLevel) =>
        Resolve(state, skill, skillLevel, EquippedGearStats.Empty);

    public float Resolve(RpgPlayerState state, SkillDefinition skill, int skillLevel, EquippedGearStats gear)
    {
        if (!gear.Valid) return 0f;
        var applicableStats = GearModifierContract.ApplicableDamageStats(skill);
        return ResolveDamage(state, skill, gear, applicableStats,
            gear.AttackWeaponPower * (double)skill.WeaponDamagePercentAtLevel(skillLevel) / 100.0);
    }

    public float ResolveAttack(RpgPlayerState state, bool projectile, EquippedGearStats gear)
    {
        if (!gear.Valid) return 0f;
        var attack = new SkillDefinition { Tags = projectile ? new[] { "attack", "projectile" } : new[] { "attack", "melee" } };
        var applicableStats = GearModifierContract.ApplicableDamageStats(attack);
        applicableStats.Remove("skill_damage");
        return ResolveDamage(state, attack, gear, applicableStats, gear.AttackWeaponPower);
    }

    private float ResolveDamage(RpgPlayerState state, SkillDefinition skill, EquippedGearStats gear,
        HashSet<string> applicableStats, double baseDamage)
    {
        var modifiers = new StatModifierAccumulator();

        AddAttributeScaling(modifiers, state, skill, gear);
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
                if (!applicableStats.Contains(GearModifierContract.NormalizeStat(modifier.Stat)))
                {
                    continue;
                }

                modifiers.Add(modifier.Operation, StatValueMath.ModifierValue(modifier));
            }
        }

        foreach (StatModifierDefinition modifier in gear.Modifiers)
        {
            if (applicableStats.Contains(GearModifierContract.NormalizeStat(modifier.Stat)))
                modifiers.Add(modifier.Operation, StatValueMath.ModifierValue(modifier));
        }

        double resolved = modifiers.Resolve(baseDamage);
        return StatValueMath.NonNegativeFloat(resolved);
    }

    private static void AddAttributeScaling(StatModifierAccumulator modifiers, RpgPlayerState state, SkillDefinition skill,
        EquippedGearStats gear)
    {
        // These values mirror the player-facing core attribute definitions.
        modifiers.Add("increased", CoreAttributeResolver.GetEffectiveValue(state, "strength", gear) * 0.1);
        if (HasTag(skill, "projectile"))
        {
            modifiers.Add("increased", CoreAttributeResolver.GetEffectiveValue(state, "dexterity", gear) * 0.2);
        }

        if (HasTag(skill, "spell"))
        {
            modifiers.Add("increased", CoreAttributeResolver.GetEffectiveValue(state, "intelligence", gear) * 0.2);
        }
    }

    private static bool HasTag(SkillDefinition skill, string expected)
    {
        for (int i = 0; i < skill.Tags.Length; i++)
        {
            if (string.Equals(GearModifierContract.NormalizeStat(skill.Tags[i]), expected, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

}
