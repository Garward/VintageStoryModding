using VRPG.Client.PlayerMenu;
using VRPG.Client.PlayerMenu.Inventory;
using VRPG.Client.PlayerMenu.Shell;
using VRPG.Inventory;
using Xunit;

namespace VRPG.Tests;

public sealed class PlayerMenuSessionStateTests
{
    [Fact]
    public void WideCharacterBackCollapsesVisibleEquipmentWithoutAnInvisibleExtraStep()
    {
        var state = new PlayerMenuSessionState();
        state.Enter(PlayerMenuEntryPoint.CharacterMenu);
        state.Back();
        Assert.False(state.Current.EquipmentExpanded, "Wide Back must change the visible Equipment disclosure on the first press.");
        Assert.False(state.Current.InspectorOnly);
        Assert.True(state.IsOpen);
        state.Back();
        Assert.False(state.IsOpen);
    }
    [Fact]
    public void CharacterShowsEquipmentInAlternatePaneAndBackReturnsToBrowse()
    {
        var state = new PlayerMenuSessionState();
        state.Enter(PlayerMenuEntryPoint.InventoryMenu);
        state.Update(state.Current with { SelectedEntry = "preview-1", InspectorScroll = 200 });
        state.Close();
        state.Enter(PlayerMenuEntryPoint.CharacterMenu);
        Assert.True(state.Current.InspectorOnly, "Character must reveal the equipment-bearing pane when only one pane fits.");
        Assert.True(state.Current.EquipmentExpanded);
        Assert.Equal("", state.Current.SelectedEntry);
        Assert.Equal(0, state.Current.InspectorScroll);
        state.Back(alternatePanes: true);
        Assert.True(state.IsOpen);
        Assert.False(state.Current.InspectorOnly);
        Assert.True(state.Current.EquipmentExpanded);
        state.Back(alternatePanes: true);
        Assert.False(state.IsOpen, "Back from alternate browse must not stop to collapse Equipment in the hidden inspector.");
    }
    [Fact]
    public void BackAfterWideningUsesVisibleDisclosureAndDoesNotResurrectInspectionWhenNarrowed()
    {
        var controller = new PlayerMenuController(new PlayerItemClientService());
        var narrow = PlayerMenuLayout.Resolve(400, 350);
        var wide = PlayerMenuLayout.Resolve(1120, 680);
        controller.Enter(PlayerMenuEntryPoint.CharacterMenu);
        Assert.True(narrow.AlternatePanes && controller.Session.Current.InspectorOnly);
        controller.Back(wide.AlternatePanes);
        Assert.True(controller.Session.IsOpen);
        Assert.False(controller.Session.Current.EquipmentExpanded);
        Assert.False(controller.Session.Current.InspectorOnly);
        controller.Back(narrow.AlternatePanes);
        Assert.False(controller.Session.IsOpen);
    }
    [Fact]
    public void NarrowingRetainsCharacterInspectorPreferenceUntilVisibleBackIsUsed()
    {
        var controller = new PlayerMenuController(new PlayerItemClientService());
        var wide = PlayerMenuLayout.Resolve(1120, 680);
        var narrow = PlayerMenuLayout.Resolve(400, 350);
        controller.Enter(PlayerMenuEntryPoint.CharacterMenu);
        Assert.False(wide.AlternatePanes);
        Assert.True(controller.Session.Current.InspectorOnly);
        controller.Back(narrow.AlternatePanes);
        Assert.True(controller.Session.IsOpen);
        Assert.False(controller.Session.Current.InspectorOnly);
        Assert.True(controller.Session.Current.EquipmentExpanded);
    }
    [Fact]
    public void RestoresCategorySelectionAndScrollWhileCharacterForcesGear()
    {
        var state = new PlayerMenuSessionState();
        state.Enter(PlayerMenuEntryPoint.InventoryMenu);
        state.Update(state.Current with { Category = PlayerItemCategories.Materials, SelectedEntry = "ore", ScrollAnchor = 31 });
        state.Navigate(PlayerMenuPage.Skills);
        state.Navigate(PlayerMenuPage.Inventory);
        Assert.Equal("ore", state.Current.SelectedEntry);
        Assert.Equal(31, state.Current.ScrollAnchor);
        state.Close();
        state.Enter(PlayerMenuEntryPoint.CharacterMenu);
        Assert.Equal(PlayerItemCategories.Gear, state.Current.Category);
        Assert.True(state.Current.EquipmentExpanded);
    }

    [Fact]
    public void EscapeDismissesInnerFocusThenInspectorThenLibraryThenShell()
    {
        var state = new PlayerMenuSessionState();
        state.Enter(PlayerMenuEntryPoint.InventoryMenu);
        state.Navigate(PlayerMenuPage.Library);
        state.Update(state.Current with { InspectorOnly = true, Focus = "search" });
        state.Back(alternatePanes: true); Assert.Equal("", state.Current.Focus); Assert.True(state.Current.InspectorOnly);
        state.Back(alternatePanes: true); Assert.False(state.Current.InspectorOnly);
        state.Back(alternatePanes: true); Assert.Equal(PlayerMenuPage.Inventory, state.Page);
        state.Back(alternatePanes: true); Assert.False(state.IsOpen);
    }

    [Fact]
    public void WorldIdentityChangeClearsAllPageState()
    {
        var state = new PlayerMenuSessionState();
        state.SetWorld("one"); state.Enter(PlayerMenuEntryPoint.CharacterMenu);
        state.Update(state.Current with { Search = "iron" });
        state.SetWorld("two");
        Assert.False(state.IsOpen); Assert.Equal("", state.Current.Search);
        Assert.False(state.Current.EquipmentExpanded);
    }
}
