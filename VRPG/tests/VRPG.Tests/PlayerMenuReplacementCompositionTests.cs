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
    [Theory]
    [InlineData(true)][InlineData(false)]
    public void ResourceHandlerUpdatesClosedMenuBeforeExistingCombatHud(bool unified)
    {
        var api = DispatchProxy.Create<ICoreClientAPI, PlayerMenuReplacementLifecycleTests.ApiProxy>();
        var channel = DispatchProxy.Create<IClientNetworkChannel, ChannelProxy>();
        var mod = new VRPGModSystem();
        typeof(VRPGModSystem).GetMethod("InitializeClientPlayerMenu", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(mod, new object[] { api, channel });
        mod.Config.Modules.Rpg.Hud.UnifiedCombatConsole = unified;
        mod.Config.Modules.Rpg.Enabled = false;
        // Exercise the real retained-resource methods without their constructors' native GL composition.
        var hudType = unified ? typeof(VRPG.Client.HudElementVRPGCombatConsole) : typeof(VRPG.Client.HudElementVRPGSkillHotbar);
        object hud = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(hudType);
        string hudField = unified ? "clientCombatConsole" : "clientSkillHotbar";
        typeof(VRPGModSystem).GetField(hudField, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(mod, hud);
        var controller = Assert.IsType<PlayerMenuController>(Field(mod, "clientPlayerMenuController"));
        controller.Hub!.Receive(new OpenHubPacket());
        bool changedBeforeHud = false;
        controller.Hub.Changed += () => changedBeforeHud = Field(hud, "resources") == null;
        typeof(VRPGModSystem).GetMethod("ReceivePlayerResources", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(mod, new object[] { api, new RpgResourcePacket { Level = 12 } });
        Assert.False(controller.Session.IsOpen);
        Assert.True(changedBeforeHud);
        Assert.Equal(12, controller.Hub.Packet!.Resources!.Level);
        Assert.Equal(12, Assert.IsType<RpgResourcePacket>(Field(hud, "resources")).Level);
        typeof(VRPGModSystem).GetField(hudField, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(mod, null);
        typeof(VRPGModSystem).GetMethod("OnClientPlayerMenuLeaveWorld", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(mod, null);
        Assert.Null(controller.Hub.Packet);
        mod.Dispose();
    }

    [Fact]
    public void LibraryNavigationReusesProductionCompositionAndPreservesConnectionState()
    {
        var api = DispatchProxy.Create<ICoreClientAPI, PlayerMenuReplacementLifecycleTests.ApiProxy>();
        var channel = DispatchProxy.Create<IClientNetworkChannel, ChannelProxy>();
        var mod = new VRPGModSystem();
        typeof(VRPGModSystem).GetMethod("InitializeClientPlayerMenu", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(mod, new object[] { api, channel });
        var menu = Field(mod, "clientPlayerMenu");
        var controller = Assert.IsType<PlayerMenuController>(Field(mod, "clientPlayerMenuController"));
        controller.Hub!.Receive(new OpenHubPacket());
        controller.Enter(PlayerMenuEntryPoint.InventoryMenu);
        controller.Hub.Receive(new OpenLibraryPacket());
        controller.Navigate(PlayerMenuPage.Library);
        Assert.Equal(PlayerMenuPage.Library, controller.Session.Page);
        Assert.Same(menu, Field(mod, "clientPlayerMenu"));
        Assert.Null(Field(mod, "clientLibraryDialog"));
        Assert.Null(Field(mod, "clientHubDialog"));
        mod.Dispose();
    }

    [Fact]
    public void ProductionActionsSendExistingAuthoritativePacketPayloads()
    {
        var api = DispatchProxy.Create<ICoreClientAPI, PlayerMenuReplacementLifecycleTests.ApiProxy>();
        var channel = DispatchProxy.Create<IClientNetworkChannel, ChannelProxy>();
        var mod = new VRPGModSystem();
        typeof(VRPGModSystem).GetMethod("InitializeClientPlayerMenu", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(mod, new object[] { api, channel });
        var controller = Assert.IsType<PlayerMenuController>(Field(mod, "clientPlayerMenuController"));
        var actions = controller.Hub!.Actions;
        actions.AdjustStat("strength", -1); actions.UpdateOptions(true, false); actions.EquipSkill(3, "strike");
        actions.ApplyTalentPlan(new[] { "root" }, new[] { "leaf" }); actions.OpenTalentEditor();
        var sent = ((ChannelProxy)(object)channel).Sent;
        var stat = Assert.IsType<StatAllocationRequestPacket>(sent[0]); Assert.Equal("strength", stat.StatCode); Assert.Equal(-1, stat.Delta);
        var options = Assert.IsType<UpdateHubOptionsPacket>(sent[1]); Assert.True(options.ShowCooldownNotifications); Assert.False(options.ShowResourceNotifications);
        var skill = Assert.IsType<SkillEquipRequestPacket>(sent[2]); Assert.Equal(3, skill.Slot); Assert.Equal("strike", skill.SkillCode);
        var plan = Assert.IsType<TalentPlanRequestPacket>(sent[3]); Assert.Equal(new[] { "root" }, plan.Allocate); Assert.Equal(new[] { "leaf" }, plan.Refund);
        Assert.IsType<TalentEditorOpenRequestPacket>(sent[4]);
        Assert.Same(mod.Config.Modules.Rpg.Hud, actions.ResourceHud); Assert.Same(mod.Config.Modules.Rpg.CombatVisuals, actions.CombatVisuals);
        mod.Config.Modules.Rpg.SkillHotbar.SlotCount = 7;
        controller.Hub.Receive(new OpenHubPacket { Options = null! });
        Assert.Equal(7, controller.Hub.Packet!.Options.SkillHotbarSlots);
        mod.Dispose();
    }

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
        Assert.NotNull(controller.Hub);
        Assert.NotNull(controller.Party);
        Assert.NotNull(mod.PartyClient);
        Assert.Same(controller.Hub, Field(mod, "clientHubPages"));
        Assert.Same(controller.Items, Field(mod, "clientPlayerItems"));
        Assert.NotNull(((ChannelProxy)(object)channel).Capability);
        Assert.IsType<PlayerMenuReplacement>(Field(mod, "clientPlayerMenuReplacement"));
        mod.Dispose();
        Assert.Null(controller.Hub.Packet);
        Assert.Null(Field(mod, "clientHubPages"));
        Assert.Null(mod.PartyClient);
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
        public System.Collections.Generic.List<object> Sent = new();
        public NetworkServerMessageHandler<PlayerMenuCapabilityPacket>? Capability;
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name == "SendPacket") Sent.Add(args![0]!);
            if (method!.Name == "SetMessageHandler" && method.GetGenericArguments()[0] == typeof(PlayerMenuCapabilityPacket))
                Capability = (NetworkServerMessageHandler<PlayerMenuCapabilityPacket>)args![0]!;
            return method.ReturnType == typeof(IClientNetworkChannel) ? this : null;
        }
    }
}

[Collection("Inventory Cairo")]
public sealed class PlayerMenuHubCompositionTests
{
    [Fact]
    public void NarrowGalleryStatsInspectorAcceptsPageUpAndPageDown()
    {
        using var scope = new InventoryCompositionTestScope();
        var session = new PlayerMenuSessionState();
        session.Navigate(PlayerMenuPage.Stats);
        session.Update(session.Current with { InspectorOnly = true, SelectedEntry = "preview-0" });
        var api = DispatchProxy.Create<ICoreClientAPI, InventoryCompositionCaptureTests.CaptureProxy>();
        using var shell = new VRPG.Client.PlayerMenu.Shell.PlayerMenuShell(api, ElementBounds.Fixed(0, 0, 400, 350),
            VRPG.Client.PlayerMenu.Shell.PlayerMenuLayout.Resolve(400, 350), session,
            new VRPG.Client.PlayerMenu.Shell.PlayerMenuGallery(), _ => { });
        using var surface = new Cairo.ImageSurface((Cairo.Format)0, 400, 350);
        using var context = new Cairo.Context(surface);
        shell.GetType().GetMethod("Draw", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(shell, new object[] { context });

        var down = new KeyEvent { KeyCode = (int)GlKeys.PageDown };
        shell.OnKeyDown(api, down);
        Assert.True(down.Handled);
        Assert.True(session.Current.InspectorScroll > 0);

        var up = new KeyEvent { KeyCode = (int)GlKeys.PageUp };
        shell.OnKeyDown(api, up);
        Assert.True(up.Handled);
        Assert.Equal(0, session.Current.InspectorScroll);
    }

    [Theory]
    [InlineData(PlayerMenuPage.Stats)][InlineData(PlayerMenuPage.Skills)][InlineData(PlayerMenuPage.Talents)]
    [InlineData(PlayerMenuPage.Library)][InlineData(PlayerMenuPage.Options)]
    public void SupportedRoutesComposeLoadingThenNativeEmptyOrHonestUnavailableBody(PlayerMenuPage route)
    {
        using var scope = new InventoryCompositionTestScope();
        using var items = new PlayerItemClientService();
        using var inventory = new InventoryPage(items);
        using var hub = new VRPG.Client.PlayerMenu.Hub.HubPageClientService(_ => { }, new());
        var controller = new PlayerMenuController(inventory, () => 0, hub);
        controller.Enter(PlayerMenuEntryPoint.InventoryMenu); controller.Navigate(route);
        var api = DispatchProxy.Create<ICoreClientAPI, InventoryCompositionCaptureTests.CaptureProxy>();
        using var shell = new VRPG.Client.PlayerMenu.Shell.PlayerMenuShell(api, ElementBounds.Fixed(0, 0, 400, 350),
            VRPG.Client.PlayerMenu.Shell.PlayerMenuLayout.Resolve(400, 350), controller.Session, null, _ => { }, inventory, hub: hub);
        using var surface = new Cairo.ImageSurface((Cairo.Format)0, 400, 350);
        using var context = new Cairo.Context(surface);
        var draw = shell.GetType().GetMethod("Draw", BindingFlags.Instance | BindingFlags.NonPublic)!;
        draw.Invoke(shell, new object[] { context });
        Assert.Contains("Loading page…", scope.Text);
        hub.Receive(new OpenHubPacket()); scope.Text.Clear();
        draw.Invoke(shell, new object[] { context });
        Assert.Contains(route == PlayerMenuPage.Stats ? "Character stats are not available yet."
            : route == PlayerMenuPage.Skills ? "No classes are available yet." : route == PlayerMenuPage.Talents ? "Loading talents…"
            : route == PlayerMenuPage.Library ? "No library entries yet." : "Cooldown notices", scope.Text);
        Assert.DoesNotContain("This page is unavailable in inventory inspection.", scope.Text);
        hub.Receive(new TalentTreeSnapshotPacket { SchemaVersion = -1 }); scope.Text.Clear();
        draw.Invoke(shell, new object[] { context });
        Assert.Contains("Could not load this page. Try again.", scope.Text);
        Assert.Contains("Retry", scope.Text);
        var down = new KeyEvent { KeyCode = (int)GlKeys.Down };
        shell.OnKeyDown(api, down);
        Assert.False(down.Handled, "Unavailable Hub bodies must not consume Inventory browse input.");
    }
}
