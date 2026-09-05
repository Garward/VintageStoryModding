using System;
using VRPG.Inventory.Gear;
using VRPG.Modules.Rpg.Stats;
using Xunit;

namespace VRPG.Tests;

public sealed class EquippedGearStatResolverTests
{
    [Fact]
    public void ResolverAggregatesEachEquippedEntryOnceAndInvalidatesByRevision()
    {
        var resolver = new EquippedGearStatResolver();
        var first = Snapshot(4, Gear(25, health: 5), Gear(armor: 40, health: 10));
        var totals = resolver.Resolve("player", 4, first);
        Assert.Same(totals, resolver.Resolve("player", 4, first));
        Assert.Equal(25, totals.WeaponPower);
        Assert.Equal(40, totals.Armor);
        Assert.Equal(15, totals.MaximumHealth);
        var after = resolver.Resolve("player", 5, new GearEquipmentSnapshot(5, true, new[] { first.Entries[0] }, ""));
        Assert.Equal(0, after.Armor);
        Assert.Equal(5, after.MaximumHealth);
        Assert.Equal(40, totals.Armor);
        Assert.Equal(0, resolver.Resolve("other", 4, Snapshot(4)).WeaponPower);
    }

    [Fact]
    public void OnlyMainWeaponContributesPowerAndUnequipRemovesAllContributions()
    {
        var resolver = new EquippedGearStatResolver();
        var instance = Gear(90, 40, 15) with
        {
            Resolved = new GearResolvedStats(90, 40, 15, 25, 35, 45, 55, 65, 75),
            Affixes = new[] { new GearAffixRoll("vrpg:damage", 1, new[] { new GearModifierRoll("vrpg:attack_damage", "more", 20) }) }
        };
        var totals = resolver.Resolve("player", 1, Snapshot(1, Gear(20), instance));
        Assert.Equal(20, totals.WeaponPower);
        Assert.Equal(25, totals.MaximumMagicShield);
        Assert.Equal(35, totals.Evasion);
        Assert.Equal(45, totals.FireResistance);
        Assert.Equal(55, totals.ColdResistance);
        Assert.Equal(65, totals.LightningResistance);
        Assert.Equal(75, totals.RustResistance);
        Assert.Single(totals.Modifiers);
        var empty = resolver.Resolve("player", 2, Snapshot(2));
        Assert.False(empty.HasMainWeapon);
        Assert.Equal(0, empty.Armor + empty.MaximumHealth + empty.MaximumMagicShield + empty.Evasion
            + empty.FireResistance + empty.ColdResistance + empty.LightningResistance + empty.RustResistance);
        Assert.Empty(empty.Modifiers);
    }

    [Fact]
    public void InvalidDuplicateAndCorruptViewsCannotExposePartialOrCachedTotals()
    {
        var resolver = new EquippedGearStatResolver();
        var good = Snapshot(1, Gear(25));
        resolver.Resolve("player", 1, good);
        foreach (var bad in new[]
        {
            good with { Valid = false },
            new GearEquipmentSnapshot(1, true, new[] { good.Entries[0], good.Entries[0] }, ""),
            Snapshot(1, Gear(float.NaN)),
            good with { Revision = 2 }
        })
        {
            var totals = resolver.Resolve("player", 1, bad);
            Assert.False(totals.Valid);
            Assert.False(totals.HasMainWeapon);
            Assert.Equal(0, totals.WeaponPower);
            Assert.Empty(totals.Modifiers);
        }
    }

    [Fact]
    public void ResolvedAffixesAreNotReappliedAndCachedModifiersCannotBeMutated()
    {
        var gear = Gear(30, 12, 15) with
        {
            Affixes = new[] { new GearAffixRoll("vrpg:test", 1, new[]
            {
                new GearModifierRoll("vrpg:weapon_power", "more", 50),
                new GearModifierRoll("vrpg:armor", "increased", 20),
                new GearModifierRoll("vrpg:max_health", "add", 15),
                new GearModifierRoll("vrpg:unused_category", "more", 200),
                new GearModifierRoll("vrpg:fire_damage", "more", 17.25f)
            }) }
        };
        var totals = new EquippedGearStatResolver().Resolve("player", 1, Snapshot(1, gear));
        Assert.Equal(30, totals.WeaponPower);
        Assert.Equal(12, totals.Armor);
        Assert.Equal(15, totals.MaximumHealth);
        Assert.Single(totals.Modifiers);
        Assert.Equal(17.25f, totals.Modifiers[0].Min);
        totals.Modifiers[0].Min = 999;
        Assert.Equal(17.25f, totals.Modifiers[0].Min);
    }

    [Fact]
    public void CorruptIdentityAndDuplicateAffixesFailClosed()
    {
        var affix = new GearAffixRoll("vrpg:test", 1, new[] { new GearModifierRoll("vrpg:fire_damage", "more", 50) });
        foreach (var instance in new[]
        {
            Gear(25) with { SchemaVersion = 99 },
            Gear(25) with { RequiredLevel = 0 },
            Gear(25) with { BaseCode = "" },
            Gear(25) with { Affixes = new[] { affix, affix } }
        })
            Assert.False(new EquippedGearStatResolver().Resolve("p", 1, Snapshot(1, instance)).Valid);
    }

    [Fact]
    public void AlternateWeaponCannotSuppressUnarmedFallback()
    {
        var weapon = Gear(90) with { EquipmentRole = "weapon.alternate1" };
        var snapshot = new GearEquipmentSnapshot(1, true, new[] { new GearEquipmentEntry(weapon.EquipmentRole, "alternate", weapon) }, "");
        var gear = new EquippedGearStatResolver().Resolve("p", 1, snapshot);
        Assert.False(gear.HasMainWeapon);
        Assert.Equal(0, gear.WeaponPower);
        Assert.Equal(10, gear.AttackWeaponPower);
    }

    internal static GearInstance Gear(float weaponPower = 0, float armor = 0, float health = 0) => new(
        1, 1, "vrpg:test", 1, "vrpg:ordinary", "weapon.main", "game:sword-iron", Array.Empty<GearAffixRoll>(),
        new GearResolvedStats(weaponPower, armor, health, 0, 0, 0, 0, 0, 0),
        new GearProvenance("test", "vrpg:test", Array.Empty<string>(), 1), "", Array.Empty<byte>());

    internal static GearEquipmentSnapshot Snapshot(long revision, params GearInstance[] instances)
    {
        var entries = new GearEquipmentEntry[instances.Length];
        string[] roles = { "weapon.main", "armor.head", "armor.body", "armor.legs" };
        for (int i = 0; i < entries.Length; i++)
            entries[i] = new GearEquipmentEntry(roles[i], "entry-" + i, instances[i] with { EquipmentRole = roles[i] });
        return new GearEquipmentSnapshot(revision, true, entries, "");
    }
}
