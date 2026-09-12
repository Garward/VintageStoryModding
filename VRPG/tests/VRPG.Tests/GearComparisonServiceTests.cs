using System;
using System.Linq;
using VRPG.Config;
using VRPG.Inventory.Gear;
using VRPG.Inventory.Queries;
using VRPG.Inventory;
using VRPG.Modules.Rpg.Players;
using VRPG.Modules.Rpg.Stats;
using VRPG.Modules.Rpg.Talents;
using VRPG.Network;
using Xunit;

namespace VRPG.Tests;

public sealed class GearComparisonServiceTests
{
    [Fact]
    public void BuildRevisionHasAnIndependentAuthoritativeSource()
    {
        Assert.NotNull(typeof(RpgPlayerStore).GetMethod("GetBuildRevision"));
    }

    [Fact]
    public void MutableBuildChangesInvalidateAtEqualInventoryRevisionWhileTicksDoNot()
    {
        var players = new RpgPlayerStore();
        var items = new InMemoryPlayerItemStore();
        var state = players.GetOrCreate("p");
        long previous = players.GetBuildRevision("p", items.Equipment());
        state.Mana -= 1; state.MagicShield += 2; state.Blood += 1; state.Experience++;
        Assert.Equal(previous, players.GetBuildRevision("p", items.Equipment()));
        foreach (Action mutation in new Action[] { () => state.BaseStats["strength"] = 10,
            () => state.Talents.Add("vrpg:strength"), () => state.SkillLevels["vrpg:slash"] = 1,
            () => state.EquippedSkills[0] = "vrpg:slash", () => state.Level++ })
        {
            mutation();
            long current = players.GetBuildRevision("p", items.Equipment());
            Assert.True(current > previous);
            previous = current;
        }
        Assert.Equal(0, items.Revision);
    }

    [Fact]
    public void EquipmentChangesBuildButStoredPickupDoesNot()
    {
        var f = new GearReadinessValidatorTests.Fixture();
        var players = new RpgPlayerStore();
        long initial = players.GetBuildRevision("p", f.Store.Equipment());
        string id = f.Insert(GearItemStateCodec.Encode(GearReadinessValidatorTests.Instance()));
        Assert.Equal(initial, players.GetBuildRevision("p", f.Store.Equipment()));
        f.Store.Equip("equip", id, PlayerEquipmentRoles.MainWeapon, f.Store.Revision);
        Assert.True(players.GetBuildRevision("p", f.Store.Equipment()) > initial);
    }

    [Fact]
    public void CandidateReplacesSelectedRoleAndUsesRuntimeDamageAndMitigation()
    {
        var f = new GearReadinessValidatorTests.Fixture();
        var oldGear = GearReadinessValidatorTests.Instance();
        f.Insert(GearItemStateCodec.Encode(oldGear), PlayerEquipmentRoles.MainWeapon);
        var nextGear = oldGear with { Resolved = oldGear.Resolved with { WeaponPower = 84, Armor = 300 } };
        string id = f.Insert(GearItemStateCodec.Encode(nextGear));
        var state = new RpgPlayerState { Level = 20 };
        state.BaseStats["strength"] = 50;
        var talents = new TalentTreeCatalog();
        var damage = new SkillDamageResolver(talents);
        var service = new PlayerItemDetailService(new GearComparisonService(f.Data, damage, new(new RpgModuleConfig(), talents)));
        var result = service.Detail(f.Store, state, 7, new() { ConnectionGeneration = 1, QueryId = 3,
            EntryId = id, RoleCode = PlayerEquipmentRoles.MainWeapon });
        Assert.Equal("", result.RejectionCode);
        Assert.Equal(id, result.EntryId);
        Assert.Equal(20, result.RequiredLevel);
        Assert.Equal(20, result.ReferenceLevel);
        var power = Assert.Single(result.Comparison.Where(x => x.StatCode == "weapon_power"));
        Assert.Equal(42, power.Before); Assert.Equal(84, power.After); Assert.Equal(42, power.Difference);
        var mitigation = Assert.Single(result.Comparison.Where(x => x.StatCode == "armor_mitigation"));
        Assert.Equal(GearScalingMath.ArmorMitigation(300, GearScalingMath.AttackPressure(20)) * 100, mitigation.After);
        Assert.Equal("percentage-points", mitigation.UnitCode);
        Assert.Equal("physical-hit-reference-level", mitigation.ConditionCode);
        var melee = Assert.Single(result.Comparison.Where(x => x.StatCode == "melee_damage"));
        Assert.Equal(44.1f, (float)melee.Before, 3);
        Assert.Equal(88.2f, (float)melee.After, 3);
        Assert.Empty(result.GainedTransformations);
        Assert.Empty(result.LostTransformations);
        var action = Assert.Single(result.Actions);
        Assert.Equal(PlayerItemAction.Equip, action.Action);
        Assert.True(action.Enabled);
        Assert.DoesNotContain(result.Comparison, x => x.StatCode.Contains("score"));
    }

    [Fact]
    public void WrongRoleRejectsAndLevelRequirementKeepsInspectionAvailable()
    {
        var f = new GearReadinessValidatorTests.Fixture();
        string id = f.Insert(GearItemStateCodec.Encode(GearReadinessValidatorTests.Instance()));
        var talents = new TalentTreeCatalog();
        var service = new PlayerItemDetailService(new GearComparisonService(f.Data, new(talents), new(new RpgModuleConfig(), talents)));
        var request = new PlayerItemDetailRequest { ConnectionGeneration = 1, QueryId = 1, EntryId = id, RoleCode = PlayerEquipmentRoles.Head };
        Assert.Equal("equipment-role-mismatch", service.Detail(f.Store, new(), 1, request).RejectionCode);
        request.RoleCode = PlayerEquipmentRoles.MainWeapon;
        var result = service.Detail(f.Store, new(), 1, request);
        Assert.Equal("requirement-not-met", result.RejectionCode);
        Assert.NotEmpty(result.Comparison);
        Assert.False(result.Actions[0].Enabled);
        Assert.Empty(f.Store.Equipment().Entries);
    }

    [Fact]
    public void ReservedStoredGearIsAdvertisedAsUnavailableBeforeEquip()
    {
        var f = new GearReadinessValidatorTests.Fixture();
        string id = f.Insert(GearItemStateCodec.Encode(GearReadinessValidatorTests.Instance()));
        Assert.True(f.Store.Reserve("reserve", new[] { new PlayerItemAmount(id, 1) }, f.Store.Revision).Success);
        var talents = new TalentTreeCatalog();
        var result = new PlayerItemDetailService(new GearComparisonService(f.Data, new(talents), new(new RpgModuleConfig(), talents)))
            .Detail(f.Store, new() { Level = 20 }, 1, new() { ConnectionGeneration = 1, QueryId = 1, EntryId = id, RoleCode = PlayerEquipmentRoles.MainWeapon });
        var action = Assert.Single(result.Actions);
        Assert.Equal(PlayerItemAction.Equip, action.Action); Assert.False(action.Enabled); Assert.Equal("entry-reserved", action.ReasonCode);
    }

    [Fact]
    public void GearResourceComparisonUsesFullRuntimeTotals()
    {
        var f = new GearReadinessValidatorTests.Fixture();
        string id = f.Insert(GearItemStateCodec.Encode(GearReadinessValidatorTests.Instance()));
        var state = new RpgPlayerState { Level = 20 };
        state.BaseStats["strength"] = 50;
        var config = new RpgModuleConfig();
        var talents = new TalentTreeCatalog();
        var result = new PlayerItemDetailService(new GearComparisonService(f.Data, new(talents), new(config, talents)))
            .Detail(f.Store, state, 1, new() { ConnectionGeneration = 1, QueryId = 1, EntryId = id, RoleCode = PlayerEquipmentRoles.MainWeapon });
        var expectedBefore = new RpgResourceCalculator(config, talents).CalculateMaximums(state, EquippedGearStats.Empty);
        var health = Assert.Single(result.Comparison.Where(x => x.StatCode == "maximum_health"));
        Assert.Equal(expectedBefore.Health, health.Before);
    }

    [Fact]
    public void AuthoredEffectCodesCannotBreakDetailPayloadBounds()
    {
        var f = new GearReadinessValidatorTests.Fixture();
        var original = GearReadinessValidatorTests.Instance();
        var gear = original with { Affixes = new[] { new GearAffixRoll("vrpg:flat-power", 1,
            new[] { new GearModifierRoll("vrpg:" + new string('x', 1000) + "_damage", "add", 1) }) } };
        string id = f.Insert(GearItemStateCodec.Encode(gear));
        var talents = new TalentTreeCatalog();
        var service = new PlayerItemDetailService(new GearComparisonService(f.Data, new(talents), new(new RpgModuleConfig(), talents)));
        var result = service.Detail(f.Store, new() { Level = 20 }, 1, new() { ConnectionGeneration = 1, QueryId = 1,
            EntryId = id, RoleCode = PlayerEquipmentRoles.MainWeapon });
        Assert.Equal("metadata-limit", result.RejectionCode);
        Assert.Empty(result.Effects);
        Assert.Empty(result.Comparison);
    }

    [Theory]
    [InlineData("vrpg:max_health_percent", "add", "percentage-points")]
    [InlineData("vrpg:health_regeneration", "add", "points-per-second")]
    [InlineData("vrpg:mana_regen", "add", "points-per-second")]
    [InlineData("vrpg:blood_regen", "add", "points-per-second")]
    [InlineData("vrpg:magic_shield_regen", "add", "points-per-second")]
    [InlineData("vrpg:health_regeneration", "increased", "percent")]
    [InlineData("vrpg:health_regeneration", "more", "percent")]
    [InlineData("vrpg:max_mana", "add", "points")]
    [InlineData("vrpg:strength", "add", "points")]
    public void EffectUnitsRespectTheRuntimeStatAndOperation(string stat, string operation, string unit)
    {
        var f = new GearReadinessValidatorTests.Fixture();
        var gear = GearReadinessValidatorTests.Instance() with { Affixes = new[] { new GearAffixRoll("vrpg:flat-power", 1,
            new[] { new GearModifierRoll(stat, operation, 7.5f) }) } };
        string id = f.Insert(GearItemStateCodec.Encode(gear));
        var talents = new TalentTreeCatalog();
        var service = new PlayerItemDetailService(new GearComparisonService(f.Data, new(talents), new(new RpgModuleConfig(), talents)));
        var result = service.Detail(f.Store, new() { Level = 20 }, 1, new() { ConnectionGeneration = 1, QueryId = 1,
            EntryId = id, RoleCode = gear.EquipmentRole });
        Assert.Equal("", result.RejectionCode);
        var effect = Assert.Single(result.Effects.Where(x => x.StatCode == stat));
        Assert.Equal(unit, effect.UnitCode);
        Assert.Equal(operation, effect.Operation);
        Assert.Equal(7.5, effect.Value);
    }
}
