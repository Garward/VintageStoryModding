using System;
using System.Reflection;
using VRPG.Client.PlayerMenu;
using VRPG.Client.PlayerMenu.Hub;
using VRPG.Client.PlayerMenu.Inventory;
using VRPG.Network;
using Vintagestory.API.Client;
using Xunit;

namespace VRPG.Tests;

public sealed class PlayerMenuRouteRetirementTests
{
    [Fact]
    public void ServerHubOpenIntentIsDistinctFromOrdinaryStateRefresh()
    {
        PropertyInfo? openRequested = typeof(OpenHubPacket).GetProperty("OpenRequested");

        Assert.NotNull(openRequested);
        var ordinary = new OpenHubPacket();
        Assert.False((bool)openRequested.GetValue(ordinary)!);
        openRequested.SetValue(ordinary, true);
        Assert.True((bool)openRequested.GetValue(ordinary)!);
    }

    [Fact]
    public void UnifiedRouteOpensRequestedPagesAndClosesSemanticMenuActions()
    {
        using var items = new PlayerItemClientService();
        using var hub = new HubPageClientService(_ => { }, new());
        var controller = new PlayerMenuController(items, hub);
        var mod = new VRPGModSystem();
        SetField(mod, "clientPlayerMenuController", controller);
        MethodInfo? open = typeof(VRPGModSystem).GetMethod("OpenPlayerMenu", BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(open);
        open.Invoke(mod, new object?[] { PlayerMenuEntryPoint.LegacyHubMenu, PlayerMenuPage.Stats });
        Assert.True(controller.Session.IsOpen);
        Assert.Equal(PlayerMenuPage.Stats, controller.Session.Page);

        open.Invoke(mod, new object?[] { PlayerMenuEntryPoint.InventoryMenu, null });
        Assert.False(controller.Session.IsOpen);

        controller.Enter(PlayerMenuEntryPoint.CharacterMenu);
        Assert.True(controller.Session.IsOpen);
        open.Invoke(mod, new object?[] { PlayerMenuEntryPoint.CharacterMenu, null });
        Assert.False(controller.Session.IsOpen);

        open.Invoke(mod, new object?[] { PlayerMenuEntryPoint.LegacyHubMenu, PlayerMenuPage.Library });
        Assert.True(controller.Session.IsOpen);
        Assert.Equal(PlayerMenuPage.Library, controller.Session.Page);
    }

    [Fact]
    public void HubStateRefreshStaysClosedWhileExplicitHubAndLibraryPacketsNavigate()
    {
        using var items = new PlayerItemClientService();
        using var hub = new HubPageClientService(_ => { }, new());
        var controller = new PlayerMenuController(items, hub);
        var mod = new VRPGModSystem();
        SetField(mod, "clientHubPages", hub);
        SetField(mod, "clientPlayerMenuController", controller);
        MethodInfo receiveHub = typeof(VRPGModSystem).GetMethod("ReceivePlayerMenuHub", BindingFlags.Instance | BindingFlags.NonPublic)!;
        MethodInfo receiveLibrary = typeof(VRPGModSystem).GetMethod("OpenPlayerMenuLibrary", BindingFlags.Instance | BindingFlags.NonPublic)!;

        receiveHub.Invoke(mod, new object[] { new OpenHubPacket() });
        Assert.False(controller.Session.IsOpen);

        receiveHub.Invoke(mod, new object[] { new OpenHubPacket { OpenRequested = true } });
        Assert.True(controller.Session.IsOpen);
        Assert.Equal(PlayerMenuPage.Stats, controller.Session.Page);

        controller.Close();
        receiveLibrary.Invoke(mod, new object[] { new OpenLibraryPacket() });
        Assert.True(controller.Session.IsOpen);
        Assert.Equal(PlayerMenuPage.Library, controller.Session.Page);
    }

    [Fact]
    public void HubHotkeyTogglesStatsThroughTheUnifiedRoute()
    {
        using var items = new PlayerItemClientService();
        using var hub = new HubPageClientService(_ => { }, new());
        var controller = new PlayerMenuController(items, hub);
        var mod = new VRPGModSystem();
        SetField(mod, "clientPlayerMenuController", controller);
        MethodInfo hotkey = typeof(VRPGModSystem).GetMethod("OnHubHotkey", BindingFlags.Instance | BindingFlags.NonPublic)!;

        Assert.True((bool)hotkey.Invoke(mod, new object?[] { null })!);
        Assert.True(controller.Session.IsOpen);
        Assert.Equal(PlayerMenuPage.Stats, controller.Session.Page);

        Assert.True((bool)hotkey.Invoke(mod, new object?[] { null })!);
        Assert.False(controller.Session.IsOpen);
    }

    [Fact]
    public void OrdinaryLegacyDialogsHaveNoRuntimeOwnerButTalentEditorDoes()
    {
        const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;

        Assert.Null(typeof(VRPGModSystem).GetField("clientHubDialog", fields));
        Assert.Null(typeof(VRPGModSystem).GetField("clientLibraryDialog", fields));
        Assert.NotNull(typeof(VRPGModSystem).GetField("clientTalentEditorDialog", fields));
    }

    private static void SetField(object owner, string name, object value) =>
        owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(owner, value);
}
