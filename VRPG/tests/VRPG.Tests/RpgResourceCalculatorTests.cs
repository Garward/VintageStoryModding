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

    private static (RpgResourceService Service, IServerPlayer Player, RpgPlayerStore Store, Storage Storage, ApiProxy Channel) ResourceService()
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
        var service = new RpgResourceService(null!, config, null!, new TalentTreeCatalog(), store, channel,
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
