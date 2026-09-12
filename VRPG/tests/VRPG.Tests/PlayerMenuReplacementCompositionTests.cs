using System;
using System.Reflection;
using VRPG.Client.PlayerMenu;
using VRPG.Network;
using Vintagestory.API.Client;
using Xunit;

namespace VRPG.Tests;

[Collection("EntityDamageScalingPatch")]
public sealed class PlayerMenuReplacementCompositionTests
{
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
