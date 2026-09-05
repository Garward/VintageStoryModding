using VRPG.Client.PlayerMenu;
using Xunit;

namespace VRPG.Tests;

public sealed class PlayerMenuDialogReplacementPlanTests
{
    [Fact]
    public void FindsBothDialogOwnersBySemanticMenuAction()
    {
        PlayerMenuDialogCandidate[] candidates =
        {
            new("unrelated", "handbook"),
            new("vanilla-inventory", VanillaMenuActions.Inventory),
            new("vanilla-character", VanillaMenuActions.Character)
        };

        bool ready = PlayerMenuDialogReplacementPlan.TryCreate(candidates, out PlayerMenuDialogReplacementPlan? plan);

        Assert.True(ready);
        Assert.NotNull(plan);
        Assert.Equal("vanilla-inventory", plan.InventoryDialogId);
        Assert.Equal("vanilla-character", plan.CharacterDialogId);
    }

    [Fact]
    public void MissingCharacterDialogRejectsEntireReplacement()
    {
        PlayerMenuDialogCandidate[] candidates =
        {
            new("vanilla-inventory", VanillaMenuActions.Inventory)
        };

        bool ready = PlayerMenuDialogReplacementPlan.TryCreate(candidates, out PlayerMenuDialogReplacementPlan? plan);

        Assert.False(ready);
        Assert.Null(plan);
    }

    [Fact]
    public void DuplicateOwnerRejectsAmbiguousReplacement()
    {
        PlayerMenuDialogCandidate[] candidates =
        {
            new("inventory-a", VanillaMenuActions.Inventory),
            new("inventory-b", VanillaMenuActions.Inventory),
            new("character", VanillaMenuActions.Character)
        };

        bool ready = PlayerMenuDialogReplacementPlan.TryCreate(candidates, out PlayerMenuDialogReplacementPlan? plan);

        Assert.False(ready);
        Assert.Null(plan);
    }

    [Fact]
    public void PhysicalKeyInformationIsNotPartOfReplacementPlanning()
    {
        PlayerMenuDialogCandidate[] candidates =
        {
            new("inventory", "INVENTORYDIALOG"),
            new("character", "CHARACTERDIALOG")
        };

        Assert.True(PlayerMenuDialogReplacementPlan.TryCreate(candidates, out _));
    }
}
