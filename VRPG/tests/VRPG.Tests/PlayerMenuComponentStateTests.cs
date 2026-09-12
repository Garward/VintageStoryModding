using System.Linq;
using VRPG.Client.PlayerMenu;
using VRPG.Client.PlayerMenu.Components;
using VRPG.Client.PlayerMenu.Inventory;
using VRPG.Client.PlayerMenu.Shell;
using VRPG.Inventory;
using VRPG.Network;
using Xunit;

namespace VRPG.Tests;

public sealed class PlayerMenuComponentStateTests
{
    [Theory]
    [InlineData(120, 400, 350)]
    [InlineData(512, 800, 520)]
    public void KeyboardCanReachEveryEntryWhileKeepingFocusVisibleAndSelectionSeparate(int count, int width, int height)
    {
        var layout = PlayerMenuLayout.Resolve(width, height);
        string[] ids = Enumerable.Range(0, count).Select(index => "preview-" + index).ToArray();
        var view = new PlayerMenuViewState(SelectedEntry: "kept-selection");
        for (int index = 0; index < count; index++)
        {
            view = PlayerMenuNavigation.MoveBrowserFocus(view, ids, PlayerMenuBrowseKey.Down, layout.VisibleRows);
            Assert.Equal("entry:" + ids[index], view.Focus);
            Assert.Equal("kept-selection", view.SelectedEntry);
            Assert.InRange(index - view.ScrollAnchor, 0, layout.VisibleRows - 1);
            Assert.True(layout.BrowserRow(index - view.ScrollAnchor).Bottom <= layout.BrowserContent.Bottom);
        }
        view = PlayerMenuNavigation.MoveBrowserFocus(view, ids, PlayerMenuBrowseKey.Home, layout.VisibleRows);
        Assert.Equal("entry:preview-0", view.Focus); Assert.Equal(0, view.ScrollAnchor);
        view = PlayerMenuNavigation.MoveBrowserFocus(view, ids, PlayerMenuBrowseKey.End, layout.VisibleRows);
        Assert.Equal("entry:preview-" + (count - 1), view.Focus);
        Assert.Equal(count - layout.VisibleRows, view.ScrollAnchor);
    }

    [Fact]
    public void PagingUsesFocusedBrowserAndDoesNotMoveInspectorScroll()
    {
        string[] ids = Enumerable.Range(0, 120).Select(index => "preview-" + index).ToArray();
        var view = new PlayerMenuViewState(Focus: "entry:preview-0", InspectorScroll: 96);
        Assert.True(PlayerMenuNavigation.BrowserHasKeyboardFocus(view, alternatePanes: false));
        view = PlayerMenuNavigation.MoveBrowserFocus(view, ids, PlayerMenuBrowseKey.PageDown, 5);
        Assert.Equal("entry:preview-5", view.Focus); Assert.Equal(96, view.InspectorScroll);
        view = PlayerMenuNavigation.MoveBrowserFocus(view, ids, PlayerMenuBrowseKey.PageUp, 5);
        Assert.Equal("entry:preview-0", view.Focus);
        Assert.False(PlayerMenuNavigation.BrowserHasKeyboardFocus(view with { InspectorOnly = true }, alternatePanes: true));
        Assert.False(PlayerMenuNavigation.BrowserHasKeyboardFocus(view with { Focus = "equipment" }, alternatePanes: false));
    }
    [Fact]
    public void ClosedDisclosureHasNoInteractiveChildrenAndReducedMotionIsImmediate()
    {
        var disclosure = new MechanicalDisclosure();
        Assert.False(disclosure.ChildrenInteractive);
        disclosure.Toggle(); Assert.True(disclosure.ChildrenInteractive);
        Assert.Equal(0, disclosure.RevealMilliseconds(true));
        disclosure.Close(); Assert.False(disclosure.ChildrenInteractive);
    }
    [Fact]
    public void DisclosureMovesOnlyDuringTransitionAndReducedMotionSettlesImmediately()
    {
        var disclosure = new MechanicalDisclosure();
        disclosure.SetExpanded(true, reducedMotion: false);
        Assert.True(disclosure.Advance(.07f));
        Assert.InRange(disclosure.VisibleFraction, .4, .6);
        disclosure.SetExpanded(false, reducedMotion: false);
        Assert.False(disclosure.ChildrenInteractive);
        disclosure.Advance(.2f); Assert.Equal(0, disclosure.VisibleFraction);
        Assert.False(disclosure.Advance(.2f));
        disclosure.SetExpanded(true, reducedMotion: true);
        Assert.Equal(1, disclosure.VisibleFraction); Assert.False(disclosure.Advance(.1f));
    }
    [Theory]
    [InlineData(MenuControlState.Disabled)][InlineData(MenuControlState.Locked)][InlineData(MenuControlState.Pending)]
    public void BlockedButtonsCannotActivate(MenuControlState state) => Assert.False(new FittedButton("try", "try", state).CanActivate);
    [Fact]
    public void TypingWinsBeforeSemanticActionsAndCloseRetainsConnectionPendingAction()
    {
        var service = new PlayerItemClientService();
        service.BeginSession(new() { ConnectionGeneration = 1 });
        long query = service.QueueQuery(new(), 0);
        Assert.True(service.Receive(new PlayerItemQueryResult { ConnectionGeneration = 1, QueryId = query }));
        var action = service.Submit(new() { EntryId = "example", Quantity = 1, Action = PlayerItemAction.Junk });
        Assert.NotNull(action);
        var controller = new PlayerMenuController(service);
        controller.Enter(PlayerMenuEntryPoint.InventoryMenu);
        controller.Enter(PlayerMenuEntryPoint.CharacterMenu, textInputFocused: true);
        Assert.True(controller.Session.IsOpen);
        controller.Enter(PlayerMenuEntryPoint.CharacterMenu);
        Assert.False(controller.Session.IsOpen); Assert.Equal(1, service.PendingMutations);
    }
    [Fact]
    public void ChangingWorldResetsConnectionAndPendingStateButSameWorldDoesNot()
    {
        var service = new PlayerItemClientService();
        var controller = new PlayerMenuController(service);
        controller.SetWorld("one"); service.BeginSession(new() { ConnectionGeneration = 9 });
        controller.SetWorld("one"); Assert.Equal(9, service.ConnectionGeneration);
        controller.SetWorld("two"); Assert.Equal(0, service.ConnectionGeneration);
    }

    [Fact]
    public void CharacterEntryQueuesTheInventoryQueryOnItsInjectedWorldClock()
    {
        var service = new PlayerItemClientService(); service.BeginSession(new() { ConnectionGeneration = 1 });
        using var page = new InventoryPage(service);
        var controller = new PlayerMenuController(page, () => 1_000);

        controller.Enter(PlayerMenuEntryPoint.CharacterMenu);

        Assert.Null(service.Pump(1_199));
        Assert.Equal(PlayerItemCategories.Gear, service.Pump(1_200)!.Category);
    }

    [Fact]
    public void WorldResetClearsTheRetainedQueryAndSessionAlongsideTheConnection()
    {
        var service = new PlayerItemClientService(); service.BeginSession(new() { ConnectionGeneration = 1 });
        using var page = new InventoryPage(service);
        var controller = new PlayerMenuController(page);
        page.ChangeQuery(new(Search: "old world", Offset: 64, EquipmentOffset: 64), 0);
        controller.Enter(PlayerMenuEntryPoint.CharacterMenu);
        controller.Session.Update(controller.Session.Current with { Focus = "search", SelectedEntry = "old-entry", InspectorScroll = 40 });

        controller.ResetForWorldChange();

        Assert.Equal(new InventoryQueryState(), page.Query);
        Assert.Equal(0, service.ConnectionGeneration);
        Assert.False(controller.Session.IsOpen);
        Assert.Equal(new PlayerMenuViewState(), controller.Session.Current);
    }
    [Fact]
    public void CategoryCopiesCannotMutateContractAndGalleryKeepsExactQuantities()
    {
        string[] categories = TypedTabStrip.Categories; categories[0] = "wrong";
        Assert.Equal(PlayerItemCategories.Gear, TypedTabStrip.Categories[0]);
        var gallery = new PlayerMenuGallery();
        Assert.Equal(long.MaxValue, gallery.Entries(PlayerItemCategories.Materials).First().Quantity);
        for (int i = 0; i < 7; i++) gallery.CycleState();
        Assert.Equal(MenuControlState.Pending, gallery.State);
        gallery.Activate(); Assert.Equal(MenuControlState.Pending, gallery.State);
    }
}
