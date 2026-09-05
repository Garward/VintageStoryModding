using VRPG.Client.PlayerMenu;
using Xunit;

namespace VRPG.Tests;

public sealed class PlayerMenuNavigationTests
{
    [Fact]
    public void InventoryMenuActionOpensInventory()
    {
        PlayerMenuNavigationAction action = PlayerMenuNavigation.Resolve(
            PlayerMenuEntryPoint.InventoryMenu,
            menuIsOpen: false);

        Assert.True(action.ShouldOpen);
        Assert.False(action.ShouldClose);
        Assert.Equal(PlayerMenuPage.Inventory, action.Page);
        Assert.False(action.ExpandEquipment);
    }

    [Fact]
    public void CharacterMenuActionOpensInventoryWithEquipmentExpanded()
    {
        PlayerMenuNavigationAction action = PlayerMenuNavigation.Resolve(
            PlayerMenuEntryPoint.CharacterMenu,
            menuIsOpen: false);

        Assert.True(action.ShouldOpen);
        Assert.Equal(PlayerMenuPage.Inventory, action.Page);
        Assert.True(action.ExpandEquipment);
    }

    [Fact]
    public void LegacyHubMenuActionOpensStatsDuringMigration()
    {
        PlayerMenuNavigationAction action = PlayerMenuNavigation.Resolve(
            PlayerMenuEntryPoint.LegacyHubMenu,
            menuIsOpen: false);

        Assert.True(action.ShouldOpen);
        Assert.Equal(PlayerMenuPage.Stats, action.Page);
    }

    [Theory]
    [InlineData(PlayerMenuEntryPoint.InventoryMenu)]
    [InlineData(PlayerMenuEntryPoint.CharacterMenu)]
    [InlineData(PlayerMenuEntryPoint.LegacyHubMenu)]
    public void AnyMenuActionClosesAnOpenPlayerMenu(PlayerMenuEntryPoint entryPoint)
    {
        PlayerMenuNavigationAction action = PlayerMenuNavigation.Resolve(entryPoint, menuIsOpen: true);

        Assert.False(action.ShouldOpen);
        Assert.True(action.ShouldClose);
        Assert.False(action.ExpandEquipment);
    }

    [Fact]
    public void ReplacementTargetsSemanticVanillaMenuActions()
    {
        Assert.Equal("inventorydialog", VanillaMenuActions.Inventory);
        Assert.Equal("characterdialog", VanillaMenuActions.Character);
    }
}
