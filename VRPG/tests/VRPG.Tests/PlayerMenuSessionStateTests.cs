using VRPG.Client.PlayerMenu;
using VRPG.Inventory;
using Xunit;

namespace VRPG.Tests;

public sealed class PlayerMenuSessionStateTests
{
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
        state.Back();
        Assert.True(state.IsOpen);
        Assert.False(state.Current.InspectorOnly);
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
        state.Back(); Assert.Equal("", state.Current.Focus); Assert.True(state.Current.InspectorOnly);
        state.Back(); Assert.False(state.Current.InspectorOnly);
        state.Back(); Assert.Equal(PlayerMenuPage.Inventory, state.Page);
        state.Back(); Assert.False(state.IsOpen);
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
