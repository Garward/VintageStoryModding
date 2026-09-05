using System;
using System.Reflection;
using VRPG.Config;
using VRPG.Data.Definitions;
using VRPG.Inventory.Gear;
using VRPG.Modules.Rpg.Players;
using VRPG.Modules.Rpg.Combat;
using VRPG.Modules.Rpg.Stats;
using VRPG.Modules.Rpg.Talents;
using Xunit;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.Server;

namespace VRPG.Tests;

public sealed class RpgResourceCalculatorTests
{
    [Theory]
    [InlineData(2e38f, 2e38f)]
    [InlineData(1.8e38f, 2.2e38f)]
    public void LargeFiniteResourceModifierMidpointsDoNotOverflow(float min, float max)
    {
        var gear = new EquippedGearStats(0, 0, 0, 0, 0, 0, 0, 0, 0, new[]
        {
            new StatModifierDefinition { Stat = "max_mana", Operation = "add", Min = min, Max = max },
            new StatModifierDefinition { Stat = "health_regen", Operation = "add", Min = min, Max = max }
        });
        var calculator = new RpgResourceCalculator(new RpgModuleConfig(), new TalentTreeCatalog());
        var state = new RpgPlayerState();
        Assert.InRange(calculator.CalculateMaximums(state, gear).Mana, 1.99e38f, 2.01e38f);
        Assert.InRange(calculator.CalculateRegeneration(state, gear).Health, 1.99e38f, 2.01e38f);
    }

    [Fact]
    public void LargeExactGearResourceRollRemainsFiniteAndExact()
    {
        var instance = EquippedGearStatResolverTests.Gear() with { Affixes = new[]
        {
            new GearAffixRoll("vrpg:resources", 1, new[]
            {
                new GearModifierRoll("vrpg:max_mana", "add", 2e38f),
                new GearModifierRoll("vrpg:health_regen", "add", 2e38f)
            })
        } };
        var gear = new EquippedGearStatResolver().Resolve("p", 1, EquippedGearStatResolverTests.Snapshot(1, instance));
        var calculator = new RpgResourceCalculator(new RpgModuleConfig(), new TalentTreeCatalog());
        Assert.True(gear.Valid);
        Assert.Equal(2e38f, calculator.CalculateMaximums(new RpgPlayerState(), gear).Mana);
        Assert.Equal(2e38f, calculator.CalculateRegeneration(new RpgPlayerState(), gear).Health);
    }

    [Fact]
    public void AllMaximumAndRegenerationResultsSaturateOnlyAtTheFloatBoundary()
    {
        var config = new RpgModuleConfig();
        config.Resources.BloodMaxHealthRatio = 1;
        config.Resources.BloodRegenHealthRatio = 1;
        var talents = ExtremeTalents();
        var state = new RpgPlayerState();
        state.Talents.Add("vrpg:extreme");
        var gear = ExtremeGear();
        var calculator = new RpgResourceCalculator(config, talents);
        var maximums = calculator.CalculateMaximums(state, gear);
        var regen = calculator.CalculateRegeneration(state, gear);
        Assert.Equal(float.MaxValue, maximums.Health);
        Assert.Equal(float.MaxValue, maximums.Mana);
        Assert.Equal(float.MaxValue, maximums.Blood);
        Assert.Equal(float.MaxValue, maximums.MagicShield);
        Assert.Equal(float.MaxValue, regen.Health);
        Assert.Equal(float.MaxValue, regen.Mana);
        Assert.Equal(float.MaxValue, regen.Blood);
        Assert.Equal(float.MaxValue, regen.MagicShield);
    }

    [Fact]
    public void FlatAdditionRetainsDoublePrecisionBeforeReducingMoreLayer()
    {
        var config = new RpgModuleConfig();
        config.Resources.BaseMaxHealth = 2e38f;
        var talents = new TalentTreeCatalog();
        talents.Replace("test", "Test", new[] { new TalentNodeDefinition { Code = "vrpg:half", Modifiers = new[]
        { new StatModifierDefinition { Stat = "max_health", Operation = "more", Min = -50, Max = -50 } } } });
        var state = new RpgPlayerState();
        state.Talents.Add("vrpg:half");
        Assert.Equal(2e38f, new RpgResourceCalculator(config, talents).CalculateMaximums(state, ExtremeGear()).Health);
    }

    [Fact]
    public void DerivedBloodUsesDoubleHealthBeforeOutputSaturation()
    {
        var config = new RpgModuleConfig();
        config.Resources.BaseMaxHealth = 2e38f;
        config.Resources.Regen.BaseHealthRegenPerSecond = 2e38f;
        config.Resources.BloodMaxHealthRatio = 0.25f;
        config.Resources.BloodRegenHealthRatio = 0.25f;
        var talents = new TalentTreeCatalog();
        talents.Replace("test", "Test", new[] { new TalentNodeDefinition { Code = "vrpg:health", Modifiers = new[]
        {
            new StatModifierDefinition { Stat = "max_health", Operation = "more", Min = 100, Max = 100 },
            new StatModifierDefinition { Stat = "health_regen", Operation = "more", Min = 100, Max = 100 }
        } } });
        var state = new RpgPlayerState();
        state.Talents.Add("vrpg:health");
        var calculator = new RpgResourceCalculator(config, talents);
        Assert.Equal(float.MaxValue, calculator.CalculateMaximums(state).Health);
        Assert.Equal(1e38f, calculator.CalculateMaximums(state).Blood);
        Assert.Equal(1e38f, calculator.CalculateRegeneration(state).Blood);
    }

    [Fact]
    public void RepeatedExtremeEquipmentRefreshKeepsEveryResourceFiniteAndPreservesPercentage()
    {
        var (service, player, store, _, _) = ResourceService(ExtremeTalents());
        var state = store.GetOrCreate(player);
        state.Talents.Add("vrpg:extreme");
        state.Mana = float.MaxValue;
        state.Blood = float.MaxValue;
        state.MagicShield = float.MaxValue;
        var tree = player.Entity.WatchedAttributes.GetTreeAttribute("health");
        tree.SetFloat("maxhealth", 2e38f);
        tree.SetFloat("currenthealth", 5e37f);
        service.EquippedStatsProvider = _ => ExtremeGear();
        for (int i = 0; i < 3; i++)
        {
            service.RefreshAfterEquipmentChange(player);
            var snapshot = service.BuildSnapshot(player);
            Assert.Equal(float.MaxValue, snapshot.MaxHealth);
            Assert.Equal(float.MaxValue / 4, snapshot.Health);
            Assert.Equal(float.MaxValue, tree.GetFloat("basemaxhealth"));
            Assert.All(new[] { snapshot.Health, snapshot.MaxHealth, snapshot.Mana, snapshot.MaxMana,
                snapshot.Blood, snapshot.MaxBlood, snapshot.MagicShield, snapshot.MaxMagicShield,
                snapshot.HealthRegenPerSecond, snapshot.ManaRegenPerSecond, snapshot.BloodRegenPerSecond,
                snapshot.MagicShieldRegenPerSecond, state.Mana, state.Blood, state.MagicShield }, value => Assert.True(float.IsFinite(value)));
        }
        service.EquippedStatsProvider = _ => EquippedGearStats.Empty;
        service.RefreshAfterEquipmentChange(player);
        Assert.Equal(50, tree.GetFloat("currenthealth")); // 25% of base 100 with 100% More.
        tree.SetFloat("currenthealth", 0);
        service.EquippedStatsProvider = _ => ExtremeGear();
        service.RefreshAfterEquipmentChange(player);
        Assert.Equal(0, tree.GetFloat("currenthealth"));
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void RefreshRepairsNonFinitePriorResourceStateWithoutInventingHealth(float invalid)
    {
        var (service, player, store, _, _) = ResourceService();
        var state = store.GetOrCreate(player);
        state.Mana = invalid;
        state.Blood = invalid;
        state.MagicShield = invalid;
        var tree = player.Entity.WatchedAttributes.GetTreeAttribute("health");
        tree.SetFloat("maxhealth", invalid);
        tree.SetFloat("currenthealth", invalid);
        service.RefreshAfterEquipmentChange(player);
        var snapshot = service.BuildSnapshot(player);
        Assert.Equal(0, snapshot.Health);
        Assert.Equal(100, snapshot.MaxHealth);
        Assert.Equal(0, tree.GetFloat("currenthealth"));
        Assert.All(new[] { state.Mana, state.Blood, state.MagicShield, state.MaxMana, state.MaxBlood,
            state.MaxMagicShield, tree.GetFloat("maxhealth"), tree.GetFloat("basemaxhealth") }, value => Assert.True(float.IsFinite(value)));
    }

    [Theory]
    [InlineData("health", float.NaN)]
    [InlineData("health", float.PositiveInfinity)]
    [InlineData("mana", float.NaN)]
    [InlineData("mana", float.PositiveInfinity)]
    [InlineData("blood", float.NaN)]
    [InlineData("blood", float.PositiveInfinity)]
    [InlineData("shield", float.NaN)]
    [InlineData("shield", float.PositiveInfinity)]
    public void PublicResourceSettersCannotWriteNonFiniteValues(string resource, float invalid)
    {
        var (service, player, store, _, _) = ResourceService();
        service.SetResource(player, resource, invalid, invalid);
        var state = store.GetOrCreate(player);
        var tree = player.Entity.WatchedAttributes.GetTreeAttribute("health");
        Assert.All(new[] { state.Mana, state.MaxMana, state.Blood, state.MaxBlood, state.MagicShield,
            state.MaxMagicShield, tree.GetFloat("currenthealth"), tree.GetFloat("maxhealth"), tree.GetFloat("basemaxhealth") },
            value => Assert.True(float.IsFinite(value)));
    }

    [Fact]
    public void SnapshotRepairsNonFiniteHealthEvenWhenMaximumIsUnchanged()
    {
        var (service, player, _, _, _) = ResourceService();
        var tree = player.Entity.WatchedAttributes.GetTreeAttribute("health");
        tree.SetFloat("maxhealth", 100);
        tree.SetFloat("currenthealth", float.NaN);
        Assert.Equal(0, service.BuildSnapshot(player).Health);
        Assert.Equal(0, tree.GetFloat("currenthealth"));
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void SpendingRejectsNonFiniteCostsWithoutCorruptingCurrentResources(float invalid)
    {
        var (service, player, store, _, _) = ResourceService();
        Assert.Throws<ArgumentOutOfRangeException>(() => service.TrySpend(player, "mana", invalid, out _, out _));
        Assert.True(float.IsFinite(store.GetOrCreate(player).Mana));
    }

    private static TalentTreeCatalog ExtremeTalents()
    {
        var modifiers = new System.Collections.Generic.List<StatModifierDefinition>();
        foreach (string code in new[] { "max_health", "max_mana", "max_blood", "max_magic_shield",
            "health_regen", "mana_regen", "blood_regen", "magic_shield_regen" })
            modifiers.Add(new StatModifierDefinition { Stat = code, Operation = "more", Min = 100, Max = 100 });
        var talents = new TalentTreeCatalog();
        talents.Replace("test", "Test", new[] { new TalentNodeDefinition { Code = "vrpg:extreme", Modifiers = modifiers.ToArray() } });
        return talents;
    }

    private static EquippedGearStats ExtremeGear()
    {
        var modifiers = new System.Collections.Generic.List<GearModifierRoll>();
        foreach (string code in new[] { "max_mana", "max_blood", "health_regen", "mana_regen", "magic_shield_regen" })
            modifiers.Add(new GearModifierRoll("vrpg:" + code, "add", 2e38f));
        var instance = EquippedGearStatResolverTests.Gear() with
        {
            Resolved = new GearResolvedStats(0, 0, 2e38f, 2e38f, 0, 0, 0, 0, 0),
            Affixes = new[] { new GearAffixRoll("vrpg:extreme", 1, modifiers) }
        };
        return new EquippedGearStatResolver().Resolve("p", 1, EquippedGearStatResolverTests.Snapshot(1, instance));
    }

    [Fact]
    public void EquipmentRefreshClampsShieldAndPreservesHealthPercentageIncludingDeath()
    {
        var (service, player, store, _, _) = ResourceService();
        var state = store.GetOrCreate(player);
        var tree = player.Entity.WatchedAttributes.GetTreeAttribute("health");
        tree.SetFloat("maxhealth", 100);
        tree.SetFloat("currenthealth", 25);
        var gear = new EquippedGearStatResolver().Resolve("p", 1,
            EquippedGearStatResolverTests.Snapshot(1, EquippedGearStatResolverTests.Gear() with
            { Resolved = new GearResolvedStats(0, 0, 100, 50, 0, 0, 0, 0, 0) }));
        service.EquippedStatsProvider = _ => gear;
        service.RefreshAfterEquipmentChange(player);
        Assert.Equal(200, tree.GetFloat("maxhealth"));
        Assert.Equal(50, tree.GetFloat("currenthealth"));
        state.MagicShield = 45;
        service.EquippedStatsProvider = _ => EquippedGearStats.Empty;
        service.RefreshAfterEquipmentChange(player);
        Assert.Equal(25, tree.GetFloat("currenthealth"));
        Assert.Equal(0, state.MagicShield);
        tree.SetFloat("currenthealth", 0);
        service.EquippedStatsProvider = _ => gear;
        service.RefreshAfterEquipmentChange(player);
        Assert.Equal(0, tree.GetFloat("currenthealth"));
    }

    [Fact]
    public void AbsorptionSpendsExactlyTheShieldConsumedAndDoesNotPersistOrSendZero()
    {
        var (service, player, store, storage, channel) = ResourceService();
        var state = store.GetOrCreate(player);
        state.MagicShield = 12;
        int writes = storage.Writes;
        Assert.Equal(0, service.AbsorbMagicShield(player, 5));
        Assert.Equal(7, state.MagicShield);
        Assert.Equal(writes + 1, storage.Writes);
        Assert.Equal(0, service.AbsorbMagicShield(player, 0));
        Assert.Equal(0, service.AbsorbMagicShield(player, -5));
        Assert.Equal(writes + 1, storage.Writes);
        Assert.Equal(3, service.AbsorbMagicShield(player, 10));
        Assert.Equal(0, state.MagicShield);
        writes = storage.Writes;
        int packets = channel.Packets;
        Assert.Equal(10, service.AbsorbMagicShield(player, 10));
        Assert.Equal(writes, storage.Writes);
        Assert.Equal(packets, channel.Packets);
    }

    private static (RpgResourceService Service, IServerPlayer Player, RpgPlayerStore Store, Storage Storage, ApiProxy Channel) ResourceService(TalentTreeCatalog? talents = null)
    {
        var config = new RpgModuleConfig();
        config.Resources.BaseMaxHealth = 100;
        config.Resources.BaseMaxMagicShield = 0;
        var store = new RpgPlayerStore();
        var storage = new Storage();
        store.Load(storage);
        var player = new TestPlayer();
        player.Entity.WatchedAttributes["health"] = new TreeAttribute();
        var channel = DispatchProxy.Create<IServerNetworkChannel, ApiProxy>();
        var service = new RpgResourceService(null!, config, null!, talents ?? new TalentTreeCatalog(), store, channel,
            new CombatStateService(null!, 1));
        return (service, player, store, storage, (ApiProxy)(object)channel);
    }

    public class ApiProxy : DispatchProxy
    {
        public int Packets;
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name == "SendPacket") { Packets++; return null; }
            throw new NotSupportedException(method.Name);
        }
    }

    // The native player interface has an internal member that DispatchProxy cannot implement.
    // Keep native entity/world-data access and skip only inventory/network initialization.
    private sealed class TestPlayer() : ServerPlayer(null!, new ServerWorldPlayerData { EntityPlayer = new EntityPlayer() })
    {
        public override string PlayerUID => "p";
        protected override void Init() { }
    }

    private sealed class Storage : IRpgPlayerStateStorage
    {
        public int Writes;
        public byte[]? Read() => null;
        public void Write(byte[] data) => Writes++;
    }

    [Fact]
    public void EquippedResourcesAreFlatBeforeTalentLayersWithoutReapplyingBakedAffixes()
    {
        var config = new RpgModuleConfig();
        config.Resources.BaseMaxHealth = 100;
        config.Resources.BaseMaxMagicShield = 10;
        var talents = new TalentTreeCatalog();
        talents.Replace("test", "Test", new[] { new TalentNodeDefinition { Code = "vrpg:test", Modifiers = new[]
        {
            new StatModifierDefinition { Stat = "max_health", Operation = "increased", Min = 20, Max = 20 },
            new StatModifierDefinition { Stat = "max_health", Operation = "more", Min = 50, Max = 50 },
            new StatModifierDefinition { Stat = "max_magic_shield", Operation = "more", Min = 20, Max = 20 }
        } } });
        var state = new RpgPlayerState();
        state.Talents.Add("vrpg:test");
        var instance = EquippedGearStatResolverTests.Gear(health: 50) with
        {
            Resolved = new GearResolvedStats(0, 0, 50, 40, 0, 0, 0, 0, 0),
            Affixes = new[] { new GearAffixRoll("vrpg:health", 1, new[] { new GearModifierRoll("vrpg:max_health", "add", 50) }) }
        };
        var gear = new EquippedGearStatResolver().Resolve("p", 1, EquippedGearStatResolverTests.Snapshot(1, instance));
        var calculator = new RpgResourceCalculator(config, talents);
        var totals = calculator.CalculateMaximums(state, gear);
        Assert.Equal(270, totals.Health, 4);
        Assert.Equal(60, totals.MagicShield, 4);
        Assert.Equal(180, calculator.CalculateMaximums(state, EquippedGearStats.Empty).Health, 4);
    }

    [Fact]
    public void EquipmentRegenerationRollsApplyOnlyToTheirResource()
    {
        var config = new RpgModuleConfig();
        config.Resources.Regen.BaseHealthRegenPerSecond = 2;
        config.Resources.Regen.BaseMagicShieldRegenPerSecond = 3;
        var instance = EquippedGearStatResolverTests.Gear() with { Affixes = new[]
        {
            new GearAffixRoll("vrpg:regen", 1, new[] { new GearModifierRoll("vrpg:magic_shield_regen", "more", 50) })
        } };
        var gear = new EquippedGearStatResolver().Resolve("p", 1, EquippedGearStatResolverTests.Snapshot(1, instance));
        var result = new RpgResourceCalculator(config, new TalentTreeCatalog()).CalculateRegeneration(new RpgPlayerState(), gear);
        Assert.Equal(2, result.Health);
        Assert.Equal(4.5f, result.MagicShield);
    }

    [Fact]
    public void InvalidTotalsCannotContributePartialResourcesOrRegenerationModifiers()
    {
        var config = new RpgModuleConfig();
        config.Resources.BaseMaxHealth = 100;
        config.Resources.BaseMaxMagicShield = 0;
        config.Resources.Regen.BaseHealthRegenPerSecond = 2;
        var invalid = new EquippedGearStats(999, 999, 999, 999, 0, 0, 0, 0, 0,
            new[] { new StatModifierDefinition { Stat = "health_regen", Operation = "add", Min = 999, Max = 999 } }, valid: false);
        var calculator = new RpgResourceCalculator(config, new TalentTreeCatalog());
        var state = new RpgPlayerState();
        Assert.Equal(100, calculator.CalculateMaximums(state, invalid).Health);
        Assert.Equal(0, calculator.CalculateMaximums(state, invalid).MagicShield);
        Assert.Equal(2, calculator.CalculateRegeneration(state, invalid).Health);
    }
}
