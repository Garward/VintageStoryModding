using System;
using System.Reflection;
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
    [InlineData(false)]
    [InlineData(true)]
    public void JoinPublishesSessionOnlyAfterConfirmedMigrationAndRefusesFailure(bool failMigration)
    {
        using var translations = new InventoryCompositionTestScope();
        var storage = new AdventureProfileTests.Storage();
        var archive = new PlayerItemArchiveService(new(storage));
        Assert.True(archive.Load().Success);
        var profile = AdventureProfileTests.Profile(archive, () => true);
        var native = new NativeLegacyInventoryFixture();
        var player = native.Player;
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
        Assert.Equal(failMigration, player.DisconnectReason != null);
    }
    private static FieldInfo Field(string name) => typeof(RpgModule).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!;
    public class ServerProxy : DispatchProxy
    {
        public object? Packet;
        public int PacketCount;
        public Action<object>? OnPacket;
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name == "SendPacket") { Packet = args![0]; PacketCount++; OnPacket?.Invoke(Packet!); }
            return method.ReturnType == typeof(IServerNetworkChannel) ? this : null;
        }
    }
}
