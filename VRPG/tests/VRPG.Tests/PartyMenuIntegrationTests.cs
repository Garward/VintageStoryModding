using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Cairo;
using VRPG.Client.PlayerMenu;
using VRPG.Client.PlayerMenu.Components;
using VRPG.Client.PlayerMenu.Inventory;
using VRPG.Client.PlayerMenu.Shell;
using VRPG.Network;
using VRPG.Parties;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Xunit;

namespace VRPG.Tests;

[Collection("Inventory Cairo")]
public sealed class PartyMenuIntegrationTests : IDisposable
{
    private readonly InventoryCompositionTestScope scope = new();
    private readonly float oldScale = RuntimeEnv.GUIScale;
    public void Dispose() { scope.Dispose(); RuntimeEnv.GUIScale = oldScale; }
    private sealed class RootBounds : ElementBounds { public override double absX => 0; public override double absY => 0; }
    internal static void CairoReady()
    {
        RuntimeEnv.GUIScale = 1;
        InventoryCompositionTestScope.EnsureCairo();
    }
    internal static List<(string Id, MenuRect Bounds, FittedButton Button)> Draw(PlayerMenuShell shell, int width, int height, string? pngPath = null)
    {
        CairoReady();
        using var surface = new ImageSurface((Format)0, width, height);
        using var context = new Context(surface);
        typeof(PlayerMenuShell).GetMethod("Draw", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(shell, new object[] { context });
        if (pngPath != null) surface.WriteToPng(pngPath);
        return (List<(string, MenuRect, FittedButton)>)typeof(PlayerMenuShell).GetField("controls", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(shell)!;
    }
    [Theory]
    [InlineData(1120, 680)]
    [InlineData(800, 520)]
    [InlineData(400, 350)]
    public void PartyShellReservesHeaderKeepsSixPagesAndMouseCreateSendsRealCommand(int width, int height)
    {
        using var f = new PartyPanelFixture("none");
        var controller = new PlayerMenuController(new PlayerItemClientService(), f.Panel);
        controller.OpenParty();
        var api = DispatchProxy.Create<ICoreClientAPI, InventoryCompositionCaptureTests.CaptureProxy>();
        using var shell = new PlayerMenuShell(api, ElementBounds.Fixed(0, 0, width, height), PlayerMenuLayout.Resolve(width, height),
            controller.Session, null, id => f.Panel.Handle(id.StartsWith("party:") ? id[6..] : id), party: f.Panel);
        var controls = Draw(shell, width, height);
        Assert.Equal(6, controls.Count(c => c.Id.StartsWith("page:")));
        Assert.Contains(controls, c => c.Id == "party");
        Assert.DoesNotContain(controls, c => c.Id is "search" or "quantity");
        foreach (var control in controls)
        { Assert.InRange(control.Bounds.X, 0, width); Assert.InRange(control.Bounds.Right, 0, width); Assert.InRange(control.Bounds.Bottom, 0, height); }
        var header = controls.Where(c => c.Bounds.Y < 50).ToArray();
        for (int i = 0; i < header.Length; i++) for (int j = i + 1; j < header.Length; j++)
            Assert.True(header[i].Bounds.Right <= header[j].Bounds.X || header[j].Bounds.Right <= header[i].Bounds.X);
        var create = controls.Single(c => c.Id == "party:create");
        shell.Bounds.ParentBounds = new RootBounds();
        shell.OnMouseUpOnElement(api, new MouseEvent((int)create.Bounds.X + 2, (int)create.Bounds.Y + 2, EnumMouseButton.Left, 0));
        Assert.NotNull(f.Client.Pending);
    }

    [Fact]
    public void TabAndEnterReachCreateWithoutChangingInventorySelection()
    {
        using var f = new PartyPanelFixture("none");
        var controller = new PlayerMenuController(new PlayerItemClientService(), f.Panel);
        controller.Enter(PlayerMenuEntryPoint.InventoryMenu);
        controller.Session.Update(new(SelectedEntry: "copper", Search: "ore", ScrollAnchor: 3));
        controller.OpenParty();
        var api = DispatchProxy.Create<ICoreClientAPI, InventoryCompositionCaptureTests.CaptureProxy>();
        using var shell = new PlayerMenuShell(api, ElementBounds.Fixed(0, 0, 400, 350), PlayerMenuLayout.Resolve(400, 350),
            controller.Session, null, id => f.Panel.Handle(id.StartsWith("party:") ? id[6..] : id), party: f.Panel);
        var controls = Draw(shell, 400, 350);
        for (int i = 0; i < controls.Count + 1 && controller.Session.Current.Focus != "party:create"; i++) shell.AdvanceFocus(false);
        Assert.Equal("party:create", controller.Session.Current.Focus);
        shell.OnKeyDown(api, new KeyEvent { KeyCode = (int)GlKeys.Enter });
        Assert.NotNull(f.Client.Pending);
        controller.Back();
        Assert.Equal("copper", controller.Session.Current.SelectedEntry);
        Assert.Equal("ore", controller.Session.Current.Search);
        Assert.Equal(3, controller.Session.Current.ScrollAnchor);
    }

    [Theory]
    [InlineData("loading", "Loading party…")]
    [InlineData("unavailable", "Party unavailable. Try Refresh.")]
    [InlineData("none", "You are not in a party.")]
    public void CompactEmptyStatesExplainActualHealthWithoutFalseSuccess(string scenario, string expected)
    {
        using var f = new PartyPanelFixture(scenario);
        var controller = new PlayerMenuController(new PlayerItemClientService(), f.Panel); controller.OpenParty();
        var api = DispatchProxy.Create<ICoreClientAPI, InventoryCompositionCaptureTests.CaptureProxy>();
        using var shell = new PlayerMenuShell(api, ElementBounds.Fixed(0, 0, 400, 350), PlayerMenuLayout.Resolve(400, 350), controller.Session, null, _ => { }, party: f.Panel);
        Draw(shell, 400, 350);
        Assert.Contains(expected, scope.Text);
        Assert.DoesNotContain("Party updated.", scope.Text);
        Assert.DoesNotContain("Select a person to see their options.", scope.Text);
    }

    [Fact]
    public void MaxUnbrokenNameHasScrollableFullDetailAndAnchoredActions()
    {
        using var f = new PartyPanelFixture("long-unbroken");
        var controller = new PlayerMenuController(new PlayerItemClientService(), f.Panel); controller.OpenParty();
        f.Panel.Select("b");
        var api = DispatchProxy.Create<ICoreClientAPI, InventoryCompositionCaptureTests.CaptureProxy>();
        using var shell = new PlayerMenuShell(api, ElementBounds.Fixed(0, 0, 400, 350), PlayerMenuLayout.Resolve(400, 350), controller.Session, null, _ => { }, party: f.Panel);
        var controls = Draw(shell, 400, 350);
        Assert.Equal(128, f.Client.View!.Members[1].DisplayName.Length);
        int maximum = (int)typeof(PlayerMenuShell).GetField("partyDetailMaximum", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(shell)!;
        Assert.True(maximum > 0);
        var remove = controls.Single(c => c.Id == "party:remove").Bounds;
        shell.OnKeyDown(api, new KeyEvent { KeyCode = (int)GlKeys.PageDown });
        Assert.True(f.Panel.State.DetailScroll > 0);
        controls.Clear(); Draw(shell, 400, 350);
        Assert.Equal(remove, controls.Single(c => c.Id == "party:remove").Bounds);
        Assert.Contains(controls, c => c.Id == "party:transfer" && c.Bounds.Bottom <= 350);
    }

    [Fact]
    public void CompactDirectoryHasReachableRowsSearchPagingAndInviteAfterSelection()
    {
        using var f = new PartyPanelFixture("leader");
        var controller = new PlayerMenuController(new PlayerItemClientService(), f.Panel); controller.OpenParty();
        f.Panel.Handle("section:online");
        var api = DispatchProxy.Create<ICoreClientAPI, InventoryCompositionCaptureTests.CaptureProxy>();
        using var shell = new PlayerMenuShell(api, ElementBounds.Fixed(0, 0, 400, 350), PlayerMenuLayout.Resolve(400, 350), controller.Session, null,
            id => f.Panel.Handle(id[6..]), party: f.Panel);
        var controls = Draw(shell, 400, 350);
        Assert.Contains(controls, c => c.Id == "party:search");
        Assert.Contains(controls, c => c.Id == "party:find");
        Assert.Contains(controls, c => c.Id == "party:next" && c.Button.CanActivate);
        var row = controls.Single(c => c.Id == "party:select:c");
        shell.Bounds.ParentBounds = new RootBounds();
        shell.OnMouseUpOnElement(api, new MouseEvent((int)row.Bounds.X + 2, (int)row.Bounds.Y + 2, EnumMouseButton.Left, 0));
        Assert.Equal("c", f.Panel.State.SelectedId);
        controls.Clear(); Draw(shell, 400, 350);
        Assert.Contains(controls, c => c.Id == "party:invite" && c.Button.CanActivate && c.Bounds.Bottom <= 350);
        Assert.DoesNotContain(controls, c => c.Id == "party:search");
    }

    [Fact]
    public void UnavailableDirectoryExplainsHealthAndHasNoSearchControl()
    {
        using var f = new PartyPanelFixture("unavailable");
        var controller = new PlayerMenuController(new PlayerItemClientService(), f.Panel); controller.OpenParty();
        f.Panel.Handle("section:online");
        var api = DispatchProxy.Create<ICoreClientAPI, InventoryCompositionCaptureTests.CaptureProxy>();
        using var shell = new PlayerMenuShell(api, ElementBounds.Fixed(0, 0, 400, 350), PlayerMenuLayout.Resolve(400, 350), controller.Session, null, _ => { }, party: f.Panel);
        var controls = Draw(shell, 400, 350);
        Assert.DoesNotContain(controls, c => c.Id == "party:search");
        Assert.Contains("Party unavailable. Try Refresh.", scope.Text);
    }

    [Fact]
    public void MousePartyButtonDoesNotOverwriteReturnFocusBeforeOpening()
    {
        using var f = new PartyPanelFixture("none");
        var controller = new PlayerMenuController(new PlayerItemClientService(), f.Panel);
        controller.Enter(PlayerMenuEntryPoint.InventoryMenu);
        controller.Session.Update(new(Search: "ore", Focus: "search", SelectedEntry: "copper", ScrollAnchor: 3));
        var before = controller.Session.Current;
        var api = DispatchProxy.Create<ICoreClientAPI, InventoryCompositionCaptureTests.CaptureProxy>();
        using var shell = new PlayerMenuShell(api, ElementBounds.Fixed(0, 0, 400, 350), PlayerMenuLayout.Resolve(400, 350), controller.Session, null,
            id => { if (id == "party") controller.OpenParty(); }, party: f.Panel);
        var controls = Draw(shell, 400, 350);
        var button = controls.Single(c => c.Id == "party"); shell.Bounds.ParentBounds = new RootBounds();
        shell.OnMouseUpOnElement(api, new MouseEvent((int)button.Bounds.X + 2, (int)button.Bounds.Y + 2, EnumMouseButton.Left, 0));
        Assert.True(f.Panel.State.IsOpen);
        controller.Back();
        Assert.Equal(before, controller.Session.Current);
    }

    [Theory]
    [InlineData("many-invites", "invitations", 7)]
    [InlineData("online-many", "online", 15)]
    public void LargestListsScrollToTheirLastRecordWithBoundedHitControls(string scenario, string section, int maximum)
    {
        using var f = new PartyPanelFixture(scenario);
        var controller = new PlayerMenuController(new PlayerItemClientService(), f.Panel); controller.OpenParty(); f.Panel.Handle("section:" + section);
        var api = DispatchProxy.Create<ICoreClientAPI, InventoryCompositionCaptureTests.CaptureProxy>();
        using var shell = new PlayerMenuShell(api, ElementBounds.Fixed(0, 0, 400, 350), PlayerMenuLayout.Resolve(400, 350), controller.Session, null, _ => { }, party: f.Panel);
        var controls = Draw(shell, 400, 350);
        for (int i = 0; i < 20; i++) shell.OnKeyDown(api, new KeyEvent { KeyCode = (int)GlKeys.PageDown });
        Assert.Equal(maximum, f.Panel.State.Scroll);
        controls.Clear(); Draw(shell, 400, 350);
        var row = Assert.Single(controls.Where(c => c.Id.StartsWith("party:select:")));
        Assert.True(row.Bounds.Bottom <= 276);
        Assert.EndsWith(scenario == "many-invites" ? 8.ToString("x32") : "target15", row.Id);
    }

    [Theory]
    [InlineData("find")]
    [InlineData("next")]
    public void CompactRejectedDirectoryRequestShowsFeedbackBesideSearch(string intent)
    {
        using var f = new PartyPanelFixture("leader");
        var controller = new PlayerMenuController(new PlayerItemClientService(), f.Panel); controller.OpenParty(); f.Panel.Handle("section:online");
        f.RejectDirectory(intent);
        var api = DispatchProxy.Create<ICoreClientAPI, InventoryCompositionCaptureTests.CaptureProxy>();
        using var shell = new PlayerMenuShell(api, ElementBounds.Fixed(0, 0, 400, 350), PlayerMenuLayout.Resolve(400, 350), controller.Session, null, _ => { }, party: f.Panel);
        var controls = Draw(shell, 400, 350);
        Assert.False(f.Panel.State.ShowDetails);
        Assert.Contains("Please wait a moment, then try again.", scope.Text);
        Assert.Contains(controls, c => c.Id == "party:find" && c.Button.CanActivate);
        Assert.Contains(controls, c => c.Id == "party:select:c" && c.Bounds.Bottom <= 276);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DetailScrollClampsWhenUpdatedRosterShortensDisplayedContent(bool removed)
    {
        using var f = new PartyPanelFixture("long-unbroken");
        var controller = new PlayerMenuController(new PlayerItemClientService(), f.Panel); controller.OpenParty(); f.Panel.Select("b");
        var api = DispatchProxy.Create<ICoreClientAPI, InventoryCompositionCaptureTests.CaptureProxy>();
        using var shell = new PlayerMenuShell(api, ElementBounds.Fixed(0, 0, 400, 350), PlayerMenuLayout.Resolve(400, 350), controller.Session, null, _ => { }, party: f.Panel);
        var controls = Draw(shell, 400, 350);
        for (int i = 0; i < 20; i++) shell.OnKeyDown(api, new KeyEvent { KeyCode = (int)GlKeys.PageDown });
        Assert.True(f.Panel.State.DetailScroll > 0);
        if (removed) { f.Panel.Handle("remove"); f.CompleteRemove(); }
        else f.Transport.Receive(new PartySnapshotPacket { SessionId = "session", Sequence = 2, Revision = 8,
            SelfUid = "a", Health = PartyHealth.Healthy, PartyId = "22222222222222222222222222222222", LeaderUid = "a",
            Members = new[] { new PartyMemberPacket { Uid = "a", DisplayName = "Rowan", IsOnline = true }, new PartyMemberPacket { Uid = "b", DisplayName = "Ash", IsOnline = true } } });
        controls.Clear(); scope.Text.Clear(); Draw(shell, 400, 350);
        Assert.Equal(0, f.Panel.State.DetailScroll);
        if (removed)
        {
            Assert.Contains("Select a person to see their options.", scope.Text);
            Assert.Contains("Party updated.", scope.Text);
            Assert.DoesNotContain(controls, c => c.Id is "party:remove" or "party:transfer");
            Assert.Contains(controls, c => c.Id == "party:refresh" && c.Button.CanActivate);
        }
        else Assert.Contains(controls, c => c.Id == "party:remove" && c.Button.CanActivate);
    }
}
