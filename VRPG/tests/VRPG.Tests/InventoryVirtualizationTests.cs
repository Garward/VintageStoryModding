using VRPG.Client.PlayerMenu.Inventory;
using VRPG.Inventory;
using Xunit;
using System.Linq;
using VRPG.Client.PlayerMenu;

namespace VRPG.Tests;

public sealed class InventoryVirtualizationTests
{
    [Theory]
    [InlineData("fraction", .2, 20, "percent")]
    [InlineData("fraction", .8, 80, "percent")]
    [InlineData("percentage-points", 20, 20, "percentage-points")]
    [InlineData("points-per-second", 3, 3, "points-per-second")]
    public void FractionsBecomePercentWithoutChangingOtherUnits(string unit, double raw, double expected, string expectedUnit)
    {
        var display = InventoryItemRow.DisplayValue(raw, unit);
        Assert.Equal(expected, display.Value); Assert.Equal(expectedUnit, display.Unit);
    }
    [Fact]
    public void GridKeyboardEndUsesRowAnchorsWithoutLosingFocusedCell()
    {
        var moved = InventoryItemGrid.MoveFocus(new(), Enumerable.Range(0, 64).Select(x => x.ToString()).ToArray(), PlayerMenuBrowseKey.End, 4, true);
        Assert.Equal("entry:63", moved.Focus);
        Assert.Equal(28, moved.ScrollAnchor);
    }
    [Theory]
    [InlineData(1120, 680)]
    [InlineData(800, 520)]
    [InlineData(620, 520)]
    [InlineData(400, 350)]
    public void InventoryChromeLeavesCompleteRowsAndBothOverscanRowsAreBounded(int width, int height)
    {
        var layout = VRPG.Client.PlayerMenu.Shell.PlayerMenuLayout.Resolve(width, height);
        Assert.True(layout.InventoryVisibleRows > 0);
        Assert.True(layout.InventoryRow(layout.InventoryVisibleRows - 1).Bottom <= layout.InventoryContent.Bottom);
        Assert.True(layout.InventorySearch.Bottom <= layout.InventoryContent.Y);
    }
    [Fact]
    public void TenThousandRecoveryEntriesStayPagedAndOnlyVisibleRowsPlusTwoAreMaterialized()
    {
        var store = new InMemoryPlayerItemStore();
        for (int i = 0; i < 10000; i++) store.ImportRecovery("r" + i,
            InventoryViewModelTests.Item("Recovery " + i.ToString("D5"), "materials"), 10000000,
            PlayerItemLocation.Overflow, PlayerItemRecoveryReason.CapacityExceeded, "test", store.Revision);
        using var page = InventoryViewModelTests.Page(store);
        page.ChangeQuery(new(Category: "", Location: PlayerItemLocation.Overflow), 0); page.Pump(200);
        Assert.Equal(10000, page.View.Total);
        Assert.Equal(64, page.View.Entries.Count);
        Assert.Equal(8, InventoryItemGrid.Visible(page.View.Entries, 0, 6, false).Count);
        Assert.Equal(16, InventoryItemGrid.Visible(page.View.Entries, 0, 6, true).Count);
        page.NextPage(300); page.Pump(500);
        Assert.Equal(64, page.Query.Offset);
        Assert.Equal(10000000, page.View.Entries[0].Quantity);
    }

    [Fact]
    public void OverlayAbbreviationDoesNotRoundExactInspectorOrSelectionQuantity()
    {
        Assert.Equal("10M", InventoryItemRow.QuantityOverlay(10000000));
        Assert.Equal("9223372036854775807", InventoryItemRow.ExactQuantity(long.MaxValue));
    }
}
