using VRPG.Client.PlayerMenu.Inventory;
using VRPG.Inventory;
using VRPG.Network;
using Xunit;

namespace VRPG.Tests;

public sealed class InventorySelectionStateTests
{
    [Fact]
    public void ExplicitQuantitiesRemainExactAndNeverTransferToReplacementRows()
    {
        var state = new InventorySelectionState();
        var a = new PlayerItemEntryPacket { EntryId = "a", Quantity = long.MaxValue };
        Assert.True(state.Set(a, 9007199254740993));
        state.Reconcile(new[] { new PlayerItemEntryPacket { EntryId = "b", Quantity = 8 } });
        Assert.Equal(9007199254740993, state.Amounts["a"]);
        Assert.DoesNotContain("b", state.Amounts.Keys);
        Assert.False(state.TryResolve(new[] { a }, out _));
        Assert.True(state.Set(a, long.MaxValue));
        Assert.True(state.TryResolve(new[] { a }, out var amounts));
        Assert.Equal(long.MaxValue, Assert.Single(amounts).Quantity);
    }

    [Fact]
    public void SelectPageEnumeratesOnlyReturnedAvailableRowsAndClearRemovesHiddenSelections()
    {
        var state = new InventorySelectionState();
        state.SelectPage(new[] { new PlayerItemEntryPacket { EntryId = "a", Quantity = 10, ReservedQuantity = 4 },
            new PlayerItemEntryPacket { EntryId = "b", Quantity = 99, Location = PlayerItemLocation.Unresolved } });
        Assert.Equal(6, state.Amounts["a"]);
        Assert.Single(state.Amounts);
        state.Reconcile(System.Array.Empty<PlayerItemEntryPacket>());
        Assert.Equal(1, state.HiddenCount(System.Array.Empty<PlayerItemEntryPacket>()));
        state.Clear(); Assert.Empty(state.Amounts);
    }
}
