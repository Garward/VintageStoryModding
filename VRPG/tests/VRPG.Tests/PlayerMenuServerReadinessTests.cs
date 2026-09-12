using System;
using System.Reflection;
using VRPG.Config;
using VRPG.Inventory.Gear;
using VRPG.Inventory.Networking;
using VRPG.Modules.Rpg;
using VRPG.Modules.Rpg.Talents;
using VRPG.Network;
using Vintagestory.API.Server;
using Xunit;

namespace VRPG.Tests;

public sealed class PlayerMenuServerReadinessTests
{
    [Theory]
    [InlineData(true, true, true, true)]
    [InlineData(false, true, true, false)]
    [InlineData(true, false, true, false)]
    [InlineData(true, true, false, false)]
    public void CapabilityAdvertisesReplacementOnlyWithEnabledProfileAndProductionService(bool enabled, bool gearReady, bool serviceReady, bool expected)
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
        typeof(RpgModule).GetMethod("SendPlayerMenuCapabilities", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(module, new object[] { new PlayerDefenseServiceTests.Fixture().Player });
        var packet = Assert.IsType<PlayerMenuCapabilityPacket>(((ServerProxy)(object)channel).Packet);
        Assert.Equal(expected, packet.DialogReplacementReady);
        Assert.Equal(expected, packet.ItemStoreReady);
    }
    private static FieldInfo Field(string name) => typeof(RpgModule).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!;
    public class ServerProxy : DispatchProxy
    {
        public object? Packet;
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name == "SendPacket") Packet = args![0];
            return method.ReturnType == typeof(IServerNetworkChannel) ? this : null;
        }
    }
}
