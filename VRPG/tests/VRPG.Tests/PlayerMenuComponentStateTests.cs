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
