using System;
using System.Collections.Generic;
using System.Linq;
using VRPG.Data;
using VRPG.Modules.Rpg.Players;
using VRPG.Modules.Rpg.Stats;
using VRPG.Inventory.Queries;
using VRPG.Network;

namespace VRPG.Inventory.Gear;

/// <summary>Projects an exact role replacement with the same resolvers used by combat.</summary>
public sealed class GearComparisonService
{
    private readonly VRPGDataRegistry data;
    private readonly GearEquipmentService equipment;
    private readonly SkillDamageResolver damage;
    private readonly RpgResourceCalculator resources;

    public GearComparisonService(VRPGDataRegistry data, SkillDamageResolver damage, RpgResourceCalculator resources)
    {
        this.data = data;
        this.damage = damage;
        this.resources = resources;
        equipment = new(data);
    }

    public string Compare(IPlayerItemStore store, RpgPlayerState state, string entryId, string role,
        PlayerItemDetailResult result)
    {
        if (!store.TryGet(entryId, out var detail) || detail == null) return "entry-missing";
        if (detail.Prototype.Kind != PlayerItemStorageKind.Unique || detail.Prototype.Category != PlayerItemCategories.Gear
            || detail.Quantity != 1 || detail.Location is not (PlayerItemLocation.Stored or PlayerItemLocation.Equipped)
            || !GearItemStateCodec.TryDecode(detail.Prototype.ExactState, out var candidate, out _)
            || candidate == null || !GearReadinessValidator.ValidateDefinitions(candidate, data, out _)) return "invalid-gear";
        if (role != candidate.EquipmentRole || data.GearBases.Get(candidate.BaseCode)?.EquipmentRole != role)
            return "equipment-role-mismatch";
        var modifiers = candidate.Affixes.SelectMany(x => x.Modifiers).Where(x => !GearModifierContract.IsBaked(x.Stat));
        if (modifiers.Take(120).Count() > 119 || modifiers.Any(x => !PlayerItemQueryService.Text(x.Stat, 128)))
            return "metadata-limit";
        var current = equipment.Snapshot(store);
        if (!current.Valid || current.Revision != result.StoreRevision) return "stale-store";
        var replaced = current.Entries.Where(x => x.RoleCode != role).ToList();
        replaced.Add(new(role, entryId, candidate));
        // Separate resolvers avoid the runtime's per-revision cache returning the before view for the hypothetical view.
        var before = new EquippedGearStatResolver().Resolve("comparison", current.Revision, current);
        var after = new EquippedGearStatResolver().Resolve("comparison", current.Revision,
            new(current.Revision, true, replaced.ToArray(), ""));
        if (!before.Valid || !after.Valid) return "invalid-gear";
        result.RequiredLevel = candidate.RequiredLevel;
        result.ReferenceLevel = Math.Clamp(state.Level, 1, 100);
        result.Effects = Effects(candidate).ToArray();
        var comparisons = new List<PlayerItemComparisonPacket>();
        void Add(string code, double oldValue, double newValue, string unit = "points", string condition = "") =>
            comparisons.Add(new() { StatCode = code, Before = oldValue, After = newValue,
                Difference = newValue - oldValue, UnitCode = unit, ConditionCode = condition });
        Add("weapon_power", before.AttackWeaponPower, after.AttackWeaponPower);
        Add("armor", before.Armor, after.Armor);
        var oldMaximums = resources.CalculateMaximums(state, before);
        var newMaximums = resources.CalculateMaximums(state, after);
        Add("maximum_health", oldMaximums.Health, newMaximums.Health);
        Add("maximum_mana", oldMaximums.Mana, newMaximums.Mana);
        Add("maximum_magic_shield", oldMaximums.MagicShield, newMaximums.MagicShield);
        Add("maximum_blood", oldMaximums.Blood, newMaximums.Blood, "points", "blood-unlocked");
        var oldRegeneration = resources.CalculateRegeneration(state, before);
        var newRegeneration = resources.CalculateRegeneration(state, after);
        Add("health_regeneration", oldRegeneration.Health, newRegeneration.Health, "points-per-second");
        Add("mana_regeneration", oldRegeneration.Mana, newRegeneration.Mana, "points-per-second");
        Add("magic_shield_regeneration", oldRegeneration.MagicShield, newRegeneration.MagicShield, "points-per-second");
        Add("blood_regeneration", oldRegeneration.Blood, newRegeneration.Blood, "points-per-second", "blood-unlocked");
        Add("evasion", GearScalingMath.EffectiveEvasion(before.Evasion, 0) * 100,
            GearScalingMath.EffectiveEvasion(after.Evasion, 0) * 100, "percentage-points", "eligible-hit-zero-accuracy");
        Add("armor_mitigation", GearScalingMath.ArmorMitigation(before.Armor, GearScalingMath.AttackPressure(result.ReferenceLevel)) * 100,
            GearScalingMath.ArmorMitigation(after.Armor, GearScalingMath.AttackPressure(result.ReferenceLevel)) * 100,
            "percentage-points", "physical-hit-reference-level");
        Add("fire_resistance", GearScalingMath.ClampElementalResistance(before.FireResistance) * 100,
            GearScalingMath.ClampElementalResistance(after.FireResistance) * 100, "percentage-points", "zero-penetration-exposure");
        Add("cold_resistance", GearScalingMath.ClampElementalResistance(before.ColdResistance) * 100,
            GearScalingMath.ClampElementalResistance(after.ColdResistance) * 100, "percentage-points", "zero-penetration-exposure");
        Add("lightning_resistance", GearScalingMath.ClampElementalResistance(before.LightningResistance) * 100,
            GearScalingMath.ClampElementalResistance(after.LightningResistance) * 100, "percentage-points", "zero-penetration-exposure");
        Add("rust_resistance", GearScalingMath.ClampElementalResistance(before.RustResistance) * 100,
            GearScalingMath.ClampElementalResistance(after.RustResistance) * 100, "percentage-points", "zero-penetration-exposure");
        foreach (string attribute in new[] { "strength", "dexterity", "intelligence" })
            Add(attribute, CoreAttributeResolver.GetEffectiveValue(state, attribute, before), CoreAttributeResolver.GetEffectiveValue(state, attribute, after));
        Add("melee_damage", damage.ResolveAttack(state, false, before), damage.ResolveAttack(state, false, after), "damage", "melee-hit");
        Add("projectile_damage", damage.ResolveAttack(state, true, before), damage.ResolveAttack(state, true, after), "damage", "projectile-hit");
        foreach (string skillCode in state.EquippedSkills.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().Take(8))
        {
            if (!PlayerItemQueryService.Text(skillCode, 128)) continue;
            var skill = data.Skills.Get(skillCode);
            if (skill == null) continue;
            int level = state.GetSkillLevel(skillCode);
            Add(skillCode, damage.Resolve(state, skill, level, before), damage.Resolve(state, skill, level, after), "damage", "equipped-skill");
        }
        result.Comparison = comparisons.ToArray();
        // No transformation runtime exists in the current authority; unique identity alone is not an effect.
        return state.Level < candidate.RequiredLevel ? "requirement-not-met" : "";
    }

    private static IEnumerable<PlayerItemEffectPacket> Effects(GearInstance gear)
    {
        var stats = gear.Resolved;
        string[] codes = { "weapon_power", "armor", "maximum_health", "maximum_magic_shield", "evasion",
            "fire_resistance", "cold_resistance", "lightning_resistance", "rust_resistance" };
        float[] values = { stats.WeaponPower, stats.Armor, stats.MaximumHealth, stats.MaximumMagicShield, stats.Evasion,
            stats.FireResistance, stats.ColdResistance, stats.LightningResistance, stats.RustResistance };
        for (int i = 0; i < codes.Length; i++)
            yield return new() { StatCode = codes[i], Operation = "resolved", Value = values[i], UnitCode = i >= 4 ? "fraction" : "points" };
        foreach (var modifier in gear.Affixes.SelectMany(x => x.Modifiers).Where(x => !GearModifierContract.IsBaked(x.Stat)))
            yield return new() { StatCode = modifier.Stat, Operation = modifier.Operation, Value = modifier.Value,
                UnitCode = modifier.Operation == "add" ? "points" : "percent", ConditionCode = "applicable-runtime-stat" };
    }
}
