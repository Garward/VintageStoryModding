using System;
using System.Reflection;
using System.Linq;
using System.Collections.Generic;
using HarmonyLib;
using VRPG.Patches;
using VRPG.Modules.Rpg.Stats;
using VRPG.Config;
using VRPG.Inventory.Gear;
using VRPG.Inventory.Networking;
using VRPG.Modules.Rpg;
using VRPG.Modules.Rpg.Talents;
using VRPG.Network;
using VRPG.Inventory;
using VRPG.Inventory.Persistence;
using Vintagestory.API.Server;
using Xunit;

namespace VRPG.Tests;

[Collection("EngineItemBirth")]
public sealed class PlayerMenuServerReadinessTests
{
    [Theory]
    [InlineData(true, true, true, false)]
    [InlineData(false, true, true, false)]
    [InlineData(true, false, true, false)]
    [InlineData(true, true, false, false)]
    public void CapabilityCannotAdvertiseReadinessWithoutPlayerCustody(bool enabled, bool gearReady, bool serviceReady, bool expected)
    {
        var f = new GearReadinessValidatorTests.Fixture();
        var config = new RpgModuleConfig();
        var channel = DispatchProxy.Create<IServerNetworkChannel, ServerProxy>();
        using var module = new RpgModule(config, f.Data, channel, () => f.Archive);
        var runtime = (RpgGearRuntime)Field("gearRuntime").GetValue(module)!;
        if (gearReady) Assert.True(runtime.ValidateAndRecover(_ => true).Ready);
        config.InventoryReplacement.Enabled = enabled;
        if (serviceReady)
        {
            var talents = new TalentTreeCatalog();
            var service = new PlayerItemNetworkService(f.Archive.Directory, new(), new(),
                new(new GearComparisonService(f.Data, new(talents), new(config, talents))), runtime.Equipment);
            Field("playerItems").SetValue(module, service);
        }
        typeof(RpgModule).GetMethod("SendPlayerMenuCapabilities", BindingFlags.Instance | BindingFlags.NonPublic, new[] { typeof(IServerPlayer) })!
            .Invoke(module, new object[] { new PlayerDefenseServiceTests.Fixture().Player });
        var packet = Assert.IsType<PlayerMenuCapabilityPacket>(((ServerProxy)(object)channel).Packet);
        Assert.Equal(expected, packet.DialogReplacementReady);
        Assert.Equal(expected, packet.ItemStoreReady);
    }

    [Theory]
    [InlineData("suppression", false)]
    [InlineData("suppression", true)]
    [InlineData("persistence", false)]
    [InlineData("guard", false)]
    [InlineData("cursor", false)]
    public void JoinedPlayerLosingAdventureReadinessReceivesWithdrawalAndLosesQueryAndActionAuthority(string failure, bool requestFirst)
    {
        using var custody = new PlayerItemCustodyBootstrapTests.LifecycleScope();
        custody.Start(true);
        custody.Raise("SaveGameLoaded");
        var archive = custody.Module!.Service!;
        var native = new NativeLegacyInventoryFixture();
        using var connection = new NativeDisconnectFixture(native.Player);
        using var translations = new InventoryCompositionTestScope();
        var channel = DispatchProxy.Create<IServerNetworkChannel, ServerProxy>();
        var proxy = (ServerProxy)(object)channel;
        var config = new RpgModuleConfig();
        using var module = new RpgModule(config, TestGearDefinitions.Registry(), channel, () => archive, () => custody.Module.Adventure);
        var runtime = (RpgGearRuntime)Field("gearRuntime").GetValue(module)!;
        Assert.True(runtime.ValidateAndRecover(_ => true).Ready);
        Field("skillDamage").SetValue(module, new SkillDamageResolver(new TalentTreeCatalog()));
        typeof(RpgModule).GetMethod("InitializePlayerItems", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(module, null);
        var service = (PlayerItemNetworkService)Field("playerItems").GetValue(module)!;
        Field("serverApi").SetValue(module, connection.Api);
        try
        {
            var join = typeof(RpgModule).GetMethod("BeginAdventureSession", BindingFlags.Instance | BindingFlags.NonPublic)!;
            Assert.True((bool)join.Invoke(module, new object[] { native.Player })!);
            long generation = Assert.Single(proxy.Packets.OfType<PlayerItemSessionPacket>()).ConnectionGeneration;
            Assert.True((bool)join.Invoke(module, new object[] { native.Player })!);
            Assert.All(proxy.Packets.OfType<PlayerItemSessionPacket>(), packet => Assert.Equal(generation, packet.ConnectionGeneration));
            var query = new PlayerItemQueryRequest { ConnectionGeneration = generation, QueryId = 1 };
            Assert.Equal("", service.Query(native.Player, query).RejectionCode);
            long revision = archive.Directory.ForPlayer("player").Revision;
            switch (failure)
            {
                case "suppression": EngineItemFlowPatch.AdventureSuppression = null; break;
                case "persistence": AccessTools.Field(typeof(PlayerItemPersistenceModule), "checkpointHealthy").SetValue(custody.Module, false); break;
                case "guard": NativeInventoryTickGuardPatch.Reset(); break;
                case "cursor": custody.Module.RequireCursorRecovery("player"); break;
            }
            if (requestFirst) Assert.Equal("adventure-unavailable", service.Query(native.Player, query).RejectionCode);
            else service.RevokeUnavailableSessions();
            var capability = Assert.IsType<PlayerMenuCapabilityPacket>(proxy.Packet);
            Assert.False(capability.ItemStoreReady);
            Assert.False(capability.DialogReplacementReady);
            Assert.Equal("unauthorized-player", service.Query(native.Player, query).RejectionCode);
            Assert.Equal("unauthorized-player", service.Action(native.Player, new PlayerItemActionRequest
            {
                ConnectionGeneration = generation, RequestId = "lost-authority", RequestSequence = 1,
                EntryId = "retained-item", Action = PlayerItemAction.Destroy, Quantity = 1
            }).RejectionCode);
            Assert.Equal(revision, archive.Directory.ForPlayer("player").Revision);
            var departure = Assert.Single(connection.Packets);
            Assert.Equal(17, departure.ClientId);
            Assert.Contains("Reconnect", departure.Packet.DisconnectPlayer.DisconnectReason);
            Assert.False(connection.Server.Clients.ContainsKey(17));
            service.RevokeUnavailableSessions();
            Assert.Single(connection.Packets);
        }
        finally { Field("serverApi").SetValue(module, null); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void JoinPublishesSessionOnlyAfterConfirmedMigrationAndRefusesFailure(bool failMigration)
    {
        var storage = new AdventureProfileTests.Storage();
        var archive = new PlayerItemArchiveService(new(storage));
        Assert.True(archive.Load().Success);
        var profile = AdventureProfileTests.Profile(archive, () => true);
        var native = new NativeLegacyInventoryFixture();
        var player = native.Player;
        using var connection = new NativeDisconnectFixture(player);
        using var translations = new InventoryCompositionTestScope();
        var channel = DispatchProxy.Create<IServerNetworkChannel, ServerProxy>();
        var proxy = (ServerProxy)(object)channel;
        proxy.OnPacket = packet =>
        {
            if (packet is PlayerItemSessionPacket || packet is PlayerMenuCapabilityPacket { ItemStoreReady: true })
            {
                Assert.Equal(0, native.Quantity);
                Assert.True(profile.CanParticipate("player", true));
            }
        };
        var data = TestGearDefinitions.Registry();
        var config = new RpgModuleConfig();
        using var module = new RpgModule(config, data, channel, () => archive, () => profile);
        Field("serverApi").SetValue(module, connection.Api);
        var runtime = (RpgGearRuntime)Field("gearRuntime").GetValue(module)!;
        Assert.True(runtime.ValidateAndRecover(_ => true).Ready);
        var talents = new TalentTreeCatalog();
        Field("playerItems").SetValue(module, new PlayerItemNetworkService(archive.Directory, new(), new(),
            new(new GearComparisonService(data, new(talents), new(config, talents))), runtime.Equipment));
        storage.FailWrites = failMigration;
        typeof(RpgModule).GetMethod("BeginAdventureSession", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(module, new object[] { player });
        var capability = Assert.IsType<PlayerMenuCapabilityPacket>(proxy.Packet);
        Assert.Equal(!failMigration, capability.ItemStoreReady);
        Assert.Equal(!failMigration, capability.DialogReplacementReady);
        Assert.Equal(failMigration ? 1 : 2, proxy.PacketCount);
        // The departing client's packet, not the public API method argument, owns the recovery message.
        Field("serverApi").SetValue(module, null);
        Assert.Equal(failMigration ? 1 : 0, connection.Packets.Count);
        if (failMigration)
        {
            var departure = Assert.Single(connection.Packets);
            Assert.Equal(17, departure.ClientId);
            Assert.Equal(9, departure.Packet.Id);
            Assert.Equal("Your inventory could not be opened. Reconnect to try again. If this continues, ask a server admin for help.",
                departure.Packet.DisconnectPlayer.DisconnectReason);
            Assert.False(connection.Server.Clients.ContainsKey(17));
        }
    }
    private static FieldInfo Field(string name) => typeof(RpgModule).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!;
    public class ServerProxy : DispatchProxy
    {
        public object? Packet;
        public int PacketCount;
        public readonly List<object> Packets = new();
        public Action<object>? OnPacket;
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name == "SendPacket") { Packet = args![0]; PacketCount++; Packets.Add(Packet!); OnPacket?.Invoke(Packet!); }
            return method.ReturnType == typeof(IServerNetworkChannel) ? this : null;
        }
    }
}
