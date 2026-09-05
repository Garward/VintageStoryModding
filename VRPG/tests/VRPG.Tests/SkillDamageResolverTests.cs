using VRPG.Data.Definitions;
using VRPG.Modules.Rpg.Players;
using VRPG.Modules.Rpg.Stats;
using VRPG.Modules.Rpg.Talents;
using Xunit;

namespace VRPG.Tests;

public sealed class SkillDamageResolverTests
{
    [Theory]
    [InlineData(2e38f, 2e38f)]
    [InlineData(1.8e38f, 2.2e38f)]
    public void LargeFiniteModifierMidpointsRemainRepresentable(float min, float max)
    {
        var gear = new EquippedGearStats(20, 0, 0, 0, 0, 0, 0, 0, 0,
            new[] { new StatModifierDefinition { Stat = "damage", Operation = "add", Min = min, Max = max } }, hasMainWeapon: true);
        float damage = new SkillDamageResolver(new TalentTreeCatalog()).Resolve(new RpgPlayerState(), new SkillDefinition(), 1, gear);
        Assert.InRange(damage, 1.99e38f, 2.01e38f);
    }

    [Fact]
    public void LargeExactGearRollReachesDamageWithoutPrematureSaturation()
    {
        var instance = EquippedGearStatResolverTests.Gear(20) with { Affixes = new[]
        {
            new VRPG.Inventory.Gear.GearAffixRoll("vrpg:damage", 1, new[]
            { new VRPG.Inventory.Gear.GearModifierRoll("vrpg:damage", "add", 2e38f) })
        } };
        var gear = new EquippedGearStatResolver().Resolve("p", 1, EquippedGearStatResolverTests.Snapshot(1, instance));
        Assert.True(gear.Valid);
        Assert.Equal(2e38f, gear.Modifiers[0].Min);
        Assert.Equal(2e38f, new SkillDamageResolver(new TalentTreeCatalog()).Resolve(new RpgPlayerState(), new SkillDefinition(), 1, gear));
    }

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
