using VRPG.Client.PlayerMenu.Inventory;
using VRPG.Inventory;
using VRPG.Network;
using Xunit;

namespace VRPG.Tests;

public sealed class InventoryActionPageTests
{
    [Fact]
    public void EquipUsesTheServerDetailActionOnceThenRefreshesTheCurrentSelection()
    {
        var items = new PlayerItemClientService();
        items.BeginSession(new PlayerItemSessionPacket { ConnectionGeneration = 1 });
        using var page = new InventoryPage(items);
        page.Open(0);

        long query = items.QueueQuery(new PlayerItemQueryRequest(), 0);
        page.Pump(200);
        Assert.True(items.Receive(Page(query, "gear-1")));
        page.Select("gear-1");
        var selected = items.Select("gear-1", PlayerEquipmentRoles.MainWeapon);
        Assert.True(items.Receive(new PlayerItemDetailResult
        {
            ConnectionGeneration = 1,
            QueryId = selected.QueryId,
            EntryId = "gear-1",
            RoleCode = PlayerEquipmentRoles.MainWeapon,
            StoreRevision = 1,
            BuildRevision = 1,
            Actions = new[] { new PlayerItemActionAvailability { Action = PlayerItemAction.Equip, Enabled = true } }
        }));

        Assert.True(page.TrySubmitAction(PlayerItemAction.Equip, 250));
        Assert.True(page.ActionPending);
        Assert.False(page.TrySubmitAction(PlayerItemAction.Equip, 251));
        Assert.Equal(1, items.PendingMutations);

        Assert.NotNull(page.PendingActionRequestId);
        string requestId = page.PendingActionRequestId!;
        Assert.True(items.Receive(new PlayerItemActionResult
        {
            ConnectionGeneration = 1,
            RequestId = requestId,
            Success = true,
            StoreRevision = 2,
            BuildRevision = 2,
            InvalidatedScopes = new[] { "inventory", "equipment", "character" }
        }));

        Assert.False(page.ActionPending);
        Assert.Equal("equipment-updated", page.ActionFeedback);
        Assert.True(page.View.Loading);
        Assert.Equal("gear-1", page.View.SelectedId);
    }

    [Fact]
    public void LostActionResultTimesOutAndKeepsFeedbackWithItsInitiatingEntry()
    {
        var items = new PlayerItemClientService(); items.BeginSession(new PlayerItemSessionPacket { ConnectionGeneration = 1 });
        using var page = new InventoryPage(items); page.Open(0);
        long query = items.QueueQuery(new PlayerItemQueryRequest(), 0); page.Pump(200);
        Assert.True(items.Receive(Page(query, "gear-1")));
        page.Select("gear-1"); var selected = items.Select("gear-1", PlayerEquipmentRoles.MainWeapon);
        Assert.True(items.Receive(new PlayerItemDetailResult { ConnectionGeneration = 1, QueryId = selected.QueryId, EntryId = "gear-1",
            RoleCode = PlayerEquipmentRoles.MainWeapon, StoreRevision = 1, BuildRevision = 1,
            Actions = new[] { new PlayerItemActionAvailability { Action = PlayerItemAction.Equip, Enabled = true } } }));
        Assert.True(page.TrySubmitAction(PlayerItemAction.Equip, 250));
        page.Pump(250 + InventoryPage.ActionTimeoutMilliseconds);
        Assert.False(page.ActionPending); Assert.Equal(0, items.PendingMutations);
        Assert.True(page.HasActionFeedback("gear-1", PlayerItemAction.Equip));
        Assert.Equal("action-timeout", page.ActionFeedback);
    }

    private static PlayerItemQueryResult Page(long query, string entryId) => new()
    {
        ConnectionGeneration = 1,
        QueryId = query,
        StoreRevision = 1,
        BuildRevision = 1,
        Total = 1,
        Entries = new[] { new PlayerItemEntryPacket
        {
            EntryId = entryId,
            DisplayName = "Test sword",
            Category = PlayerItemCategories.Gear,
            Location = PlayerItemLocation.Stored,
            Quantity = 1
        } }
    };
}
