using VRPG.Data.Definitions;
using VRPG.Modules.Rpg.Players;
using VRPG.Modules.Rpg.Stats;
using VRPG.Modules.Rpg.Talents;
using Xunit;

namespace VRPG.Tests;

public sealed class SkillDamageResolverTests
{
    [Fact]
    public void CompatibilityPreviewDoesNotOverrideExplicitInvalidEquipment()
    {
        var resolver = new SkillDamageResolver(new TalentTreeCatalog());
        var state = new RpgPlayerState();
        var skill = new SkillDefinition();
        Assert.Equal(10, resolver.Resolve(state, skill, 1));
        Assert.Equal(0, resolver.Resolve(state, skill, 1, EquippedGearStats.Invalid));
    }

    [Fact]
    public void SkillsUseWeaponCoefficientAndIndependentMoreFactorsInsteadOfLegacyDamage()
    {
        var talents = new TalentTreeCatalog();
        talents.Replace("test", "Test", new[] { new TalentNodeDefinition
        {
            Code = "vrpg:test", Modifiers = new[] { new StatModifierDefinition { Stat = "fire_damage", Operation = "more", Min = 20, Max = 20 } }
        } });
        var state = new RpgPlayerState { Level = 80 };
        state.Talents.Add("vrpg:test");
        var instance = EquippedGearStatResolverTests.Gear(25) with
        {
            Affixes = new[] { new VRPG.Inventory.Gear.GearAffixRoll("vrpg:damage", 1, new[]
            {
                new VRPG.Inventory.Gear.GearModifierRoll("vrpg:weapon_power", "more", 100),
                new VRPG.Inventory.Gear.GearModifierRoll("vrpg:fire_damage", "more", 50)
            }) }
        };
        var gear = new EquippedGearStatResolver().Resolve("p", 1, EquippedGearStatResolverTests.Snapshot(1, instance));
        var skill = new SkillDefinition { Damage = new SkillDamageDefinition
        {
            Type = "fire", Base = 999, WeaponDamagePercent = 200, WeaponDamagePerLevelPercent = 10
        } };
        Assert.Equal(99, new SkillDamageResolver(talents).Resolve(state, skill, 2, gear), 4);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MissingWeaponUsesTenAndInvalidEquipmentFailsClosed(bool valid)
    {
        var snapshot = EquippedGearStatResolverTests.Snapshot(1) with { Valid = valid };
        var gear = new EquippedGearStatResolver().Resolve("p", 1, snapshot);
        var skill = new SkillDefinition { Damage = new SkillDamageDefinition { Base = 999, WeaponDamagePercent = 150 } };
        Assert.Equal(valid ? 15 : 0, new SkillDamageResolver(new TalentTreeCatalog()).Resolve(new RpgPlayerState { Level = 100 }, skill, 1, gear));
    }
}
