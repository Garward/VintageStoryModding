using System;
using System.Reflection;
using VRPG.Client.PlayerMenu;
using VRPG.Client.PlayerMenu.Inventory;
using VRPG.Network;
using Vintagestory.API.Client;
using Xunit;

namespace VRPG.Tests;

[Collection("EntityDamageScalingPatch")]
public sealed class PlayerMenuReplacementCompositionTests
{
    [Fact]
    public void ModDisposalCleansConnectionAndOrdinaryTickWhileNativeRestorationRetries()
    {
        var api = DispatchProxy.Create<ICoreClientAPI, PlayerMenuReplacementLifecycleTests.ApiProxy>();
        var proxy = (PlayerMenuReplacementLifecycleTests.ApiProxy)(object)api;
        var channel = DispatchProxy.Create<IClientNetworkChannel, ChannelProxy>();
        var mod = new VRPGModSystem();
        typeof(VRPGModSystem).GetField("clientApi", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(mod, api);
        typeof(VRPGModSystem).GetMethod("InitializeClientPlayerMenu", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(mod, new object[] { api, channel });
        var inventory = new FailingNativeDialog(api, VanillaMenuActions.Inventory);
        var character = new FailingNativeDialog(api, VanillaMenuActions.Character);
        proxy.Dialogs.Add(inventory); proxy.Dialogs.Add(character);
        var items = Assert.IsType<PlayerItemClientService>(Field(mod, "clientPlayerItems"));
        items.BeginSession(new() { ConnectionGeneration = 1 });
        ((ChannelProxy)(object)channel).Capability!(new()
        { ProtocolVersion = PlayerMenuCapabilityProtocol.CurrentVersion, InventoryReplacementEnabled = true, ItemStoreReady = true, DialogReplacementReady = true });
        var replacement = Assert.IsType<PlayerMenuReplacement>(Field(mod, "clientPlayerMenuReplacement"));
        Assert.True(replacement.IsInstalled);
        inventory.FailBind = true;
        Assert.Null(Record.Exception(mod.Dispose));
        Assert.Null(Field(mod, "clientPlayerItems"));
        Assert.Equal(0, items.ConnectionGeneration);
        Assert.Single(proxy.Ticks);
        Assert.True(replacement.RestorationPending);
        proxy.RunTicks();
        Assert.False(replacement.RestorationPending);
        Assert.Same(inventory, proxy.Handlers[VanillaMenuActions.Inventory].Target);
        Assert.Same(character, proxy.Handlers[VanillaMenuActions.Character].Target);
        Assert.Empty(proxy.Ticks);
    }

    [Fact]
    public void ClientCompositionCreatesProductionMenuAndCapabilityHandlerWithoutEnvironmentSwitch()
    {
        var api = DispatchProxy.Create<ICoreClientAPI, PlayerMenuReplacementLifecycleTests.ApiProxy>();
        var channel = DispatchProxy.Create<IClientNetworkChannel, ChannelProxy>();
        var mod = new VRPGModSystem();
        var initialize = typeof(VRPGModSystem).GetMethod("InitializeClientPlayerMenu", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(initialize);
        initialize.Invoke(mod, new object[] { api, channel });
        Assert.IsType<GuiDialogVrpgPlayerMenu>(Field(mod, "clientPlayerMenu"));
        var controller = Assert.IsType<PlayerMenuController>(Field(mod, "clientPlayerMenuController"));
        Assert.NotNull(controller.Inventory);
        Assert.Same(controller.Items, Field(mod, "clientPlayerItems"));
        Assert.NotNull(((ChannelProxy)(object)channel).Capability);
        Assert.IsType<PlayerMenuReplacement>(Field(mod, "clientPlayerMenuReplacement"));
        mod.Dispose();
    }
    private static object? Field(object owner, string name) => owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(owner);
    private sealed class FailingNativeDialog(ICoreClientAPI api, string action) : GuiDialog(api)
    {
        public bool FailBind;
        public override string ToggleKeyCombinationCode => action;
        public override void OnBlockTexturesLoaded()
        {
            if (FailBind) { FailBind = false; throw new InvalidOperationException("Native bind failure."); }
            base.OnBlockTexturesLoaded();
        }
    }
    public class ChannelProxy : DispatchProxy
    {
        public NetworkServerMessageHandler<PlayerMenuCapabilityPacket>? Capability;
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name == "SetMessageHandler" && method.GetGenericArguments()[0] == typeof(PlayerMenuCapabilityPacket))
                Capability = (NetworkServerMessageHandler<PlayerMenuCapabilityPacket>)args![0]!;
            return method.ReturnType == typeof(IClientNetworkChannel) ? this : null;
        }
    }
}
