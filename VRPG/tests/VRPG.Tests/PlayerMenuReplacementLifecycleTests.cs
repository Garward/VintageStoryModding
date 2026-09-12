using System;
using System.Collections.Generic;
using System.Reflection;
using VRPG.Client.PlayerMenu;
using VRPG.Client.PlayerMenu.Inventory;
using VRPG.Network;
using Vintagestory.API.Client;
using Xunit;

namespace VRPG.Tests;

public sealed class PlayerMenuReplacementLifecycleTests
{
    [Fact]
    public void PendingRestorationRemainsExplicitUntilTheActualNativeHandlerRecovers()
    {
        using var f = new Fixture(); f.AddOwners();
        f.Items.BeginSession(new() { ConnectionGeneration = 1 }); f.Replacement.Receive(Ready());
        var proxy = (ApiProxy)(object)f.Api;
        f.Inventory!.BindFailures = 3;
        f.Replacement.Receive(new());
        Assert.True(f.Replacement.RestorationPending);
        Assert.NotNull(f.Replacement.LastFailure);
        Assert.IsType<PlayerMenuEntryDialog>(proxy.Handlers[VanillaMenuActions.Inventory].Target);
        proxy.RunTicks();
        Assert.True(f.Replacement.RestorationPending);
        Assert.IsType<PlayerMenuEntryDialog>(proxy.Handlers[VanillaMenuActions.Inventory].Target);
        proxy.RunTicks();
        Assert.False(f.Replacement.RestorationPending);
        Assert.Same(f.Inventory, proxy.Handlers[VanillaMenuActions.Inventory].Target);
        Assert.Empty(proxy.Ticks);
    }
    [Theory]
    [InlineData("capability")]
    [InlineData("disconnect")]
    [InlineData("leave")]
    [InlineData("dispose")]
    public void TransientRestorationFailureRecoversWithoutReadinessAndCannotSkipCleanup(string reason)
    {
        using var f = new Fixture(); f.AddOwners();
        f.Items.BeginSession(new() { ConnectionGeneration = 1 });
        f.Replacement.Receive(Ready());
        f.Controller.Enter(PlayerMenuEntryPoint.CharacterMenu);
        var proxy = (ApiProxy)(object)f.Api;
        Assert.IsType<PlayerMenuEntryDialog>(proxy.Handlers[VanillaMenuActions.Inventory].Target);
        f.Inventory!.BindFailures = 1;
        Exception? failure = Record.Exception(() =>
        {
            switch (reason)
            {
                case "capability": f.Replacement.Receive(new()); break;
                case "disconnect": f.Items.ResetConnection(); break;
                case "leave": f.Replacement.LeaveWorld(); break;
                case "dispose": f.Replacement.Dispose(); break;
            }
        });
        Assert.Null(failure);
        Assert.False(f.Controller.Session.IsOpen);
        if (reason is "leave" or "disconnect") Assert.Equal(0, f.Items.ConnectionGeneration);
        if (reason == "dispose")
        {
            // Ordinary ModSystem ticks are removed by disposal: use the lifecycle's scheduled retry.
            Assert.NotEmpty(proxy.Ticks);
            proxy.RunTicks();
        }
        else f.Replacement.Poll();
        Assert.Same(f.Inventory, proxy.Handlers[VanillaMenuActions.Inventory].Target);
        Assert.Same(f.Character, proxy.Handlers[VanillaMenuActions.Character].Target);
        Assert.Empty(proxy.Ticks);
        if (reason == "dispose")
        {
            int binds = f.NativeBinds.Count;
            f.Items.ResetConnection(); f.Replacement.Receive(Ready()); f.Replacement.Poll();
            Assert.Equal(binds, f.NativeBinds.Count);
        }
    }
    [Fact]
    public void CapabilityAndNativeRegistrationMayArriveInEitherOrder()
    {
        using var f = new Fixture();
        f.Replacement.Receive(Ready());
        f.Replacement.Poll();
        Assert.False(f.Replacement.IsInstalled);
        f.Items.BeginSession(new() { ConnectionGeneration = 1 });
        f.Replacement.Poll();
        Assert.False(f.Replacement.IsInstalled);
        f.AddOwners(); f.Replacement.Poll();
        Assert.True(f.Replacement.IsInstalled);
        Assert.IsType<PlayerMenuEntryDialog>(f.Dialogs[0]);
        Assert.IsType<PlayerMenuEntryDialog>(f.Dialogs[1]);
        f.Replacement.Receive(Ready());
        Assert.Equal(2, f.Dialogs.Count);
    }

    [Theory]
    [InlineData("capability")]
    [InlineData("protocol")]
    [InlineData("disconnect")]
    [InlineData("leave")]
    [InlineData("dispose")]
    public void LifecycleLossClosesSharedMenuAndRestoresBothNativeHandlers(string reason)
    {
        using var f = new Fixture(); f.AddOwners();
        f.Items.BeginSession(new() { ConnectionGeneration = 1 });
        f.Replacement.Receive(Ready()); f.Replacement.Poll();
        Assert.True(f.Replacement.IsInstalled);
        f.Controller.Enter(PlayerMenuEntryPoint.CharacterMenu);
        switch (reason)
        {
            case "capability": f.Replacement.Receive(new()); break;
            case "protocol": var bad = Ready(); bad.ProtocolVersion++; f.Replacement.Receive(bad); break;
            case "disconnect": f.Items.ResetConnection(); break;
            case "leave": f.Replacement.LeaveWorld(); break;
            case "dispose": f.Replacement.Dispose(); break;
        }
        Assert.False(f.Controller.Session.IsOpen);
        Assert.False(f.Replacement.IsInstalled);
        Assert.Equal(new GuiDialog[] { f.Inventory!, f.Character! }, f.Dialogs);
        Assert.Equal(2, f.NativeBinds.Count);
        f.Replacement.Poll(); Assert.False(f.Replacement.IsInstalled);
    }

    private static PlayerMenuCapabilityPacket Ready() => new()
    { ProtocolVersion = PlayerMenuCapabilityProtocol.CurrentVersion, InventoryReplacementEnabled = true, ItemStoreReady = true, DialogReplacementReady = true };
    private sealed class Fixture : IDisposable
    {
        public readonly List<GuiDialog> Dialogs = new();
        public readonly List<string> NativeBinds = new();
        public readonly PlayerItemClientService Items = new();
        public readonly InventoryPage Page;
        public readonly PlayerMenuController Controller;
        public readonly GuiDialogVrpgPlayerMenu Menu;
        public readonly PlayerMenuReplacement Replacement;
        public readonly ICoreClientAPI Api;
        public NativeDialog? Inventory, Character;
        public Fixture()
        {
            Api = DispatchProxy.Create<ICoreClientAPI, ApiProxy>();
            ((ApiProxy)(object)Api).Dialogs = Dialogs;
            Page = new(Items); Controller = new(Page); Menu = new(Api, Controller);
            Replacement = new(Api, Controller, Menu);
        }
        public void AddOwners()
        {
            Inventory = new(Api, VanillaMenuActions.Inventory, NativeBinds);
            Character = new(Api, VanillaMenuActions.Character, NativeBinds);
            Dialogs.Add(Inventory); Dialogs.Add(Character);
        }
        public void Dispose() { Replacement.Dispose(); Menu.Dispose(); Page.Dispose(); Items.Dispose(); }
    }
    private sealed class NativeDialog(ICoreClientAPI api, string action, List<string> binds) : GuiDialog(api)
    {
        public int BindFailures;
        public override string ToggleKeyCombinationCode => action;
        public override void OnBlockTexturesLoaded()
        {
            binds.Add(action);
            if (BindFailures > 0) { BindFailures--; throw new InvalidOperationException("Transient native handler failure."); }
            base.OnBlockTexturesLoaded();
        }
    }
    public class ApiProxy : DispatchProxy
    {
        public List<GuiDialog> Dialogs = new();
        public Dictionary<string, Delegate> Handlers = new();
        public Dictionary<long, Delegate> Ticks = new();
        public void RunTicks()
        {
            foreach (var tick in new List<Delegate>(Ticks.Values)) tick.DynamicInvoke(0.025f);
        }
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name == "get_LoadedGuis") return Dialogs;
            if (method.Name == "SetHotKeyHandler") { Handlers[(string)args![0]!] = (Delegate)args[1]!; return null; }
            if (method.Name == "RegisterGameTickListener") { long id = Ticks.Count + 1; Ticks[id] = (Delegate)args![0]!; return id; }
            if (method.Name == "UnregisterGameTickListener") { Ticks.Remove((long)args![0]!); return null; }
            Type type = method.ReturnType;
            if (type.IsInterface)
            {
                var proxy = typeof(DispatchProxy).GetMethod("Create", Type.EmptyTypes)!.MakeGenericMethod(type, typeof(ApiProxy)).Invoke(null, null)!;
                ((ApiProxy)proxy).Dialogs = Dialogs; ((ApiProxy)proxy).Handlers = Handlers; ((ApiProxy)proxy).Ticks = Ticks; return proxy;
            }
            return type.IsValueType && type != typeof(void) ? Activator.CreateInstance(type) : null;
        }
    }
}
