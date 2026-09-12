using System.Linq;
using VRPG.Client.PlayerMenu;
using VRPG.Client.PlayerMenu.Components;
using VRPG.Client.PlayerMenu.Inventory;
using VRPG.Inventory;
using VRPG.Network;
using Vintagestory.API.Config;
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

        using var scope = new InventoryCompositionTestScope();
        Assert.Equal("Equipment update timed out. Try again.", Lang.Get("vrpg:menu-action-action-timeout"));
    }

    [Fact]
    public void LostActionResultTimesOutWhileThePageIsClosed()
    {
        var items = new PlayerItemClientService(); items.BeginSession(new PlayerItemSessionPacket { ConnectionGeneration = 1 });
        using var page = new InventoryPage(items); page.Open(0);
        long query = items.QueueQuery(new PlayerItemQueryRequest(), 0); page.Pump(200);
        Assert.True(items.Receive(Page(query, "gear-1")));
        page.Select("gear-1"); var selected = items.Select("gear-1", PlayerEquipmentRoles.MainWeapon);
        Assert.True(items.Receive(Detail(selected, "gear-1")));
        Assert.True(page.TrySubmitAction(PlayerItemAction.Equip, 250));

        page.Close();
        page.Pump(250 + InventoryPage.ActionTimeoutMilliseconds);

        Assert.False(page.ActionPending); Assert.Equal(0, items.PendingMutations);
        Assert.True(page.HasActionFeedback("gear-1", PlayerItemAction.Equip));
    }

    [Fact]
    public void OtherEquipmentActionsRenderDisabledWhileTheInitiatingActionIsPending()
    {
        var items = new PlayerItemClientService(); items.BeginSession(new PlayerItemSessionPacket { ConnectionGeneration = 1 });
        using var page = new InventoryPage(items); page.Open(0);
        long query = items.QueueQuery(new PlayerItemQueryRequest(), 0); page.Pump(200);
        Assert.True(items.Receive(Page(query, "gear-1", "gear-2")));
        page.Select("gear-1"); var first = items.Select("gear-1", PlayerEquipmentRoles.MainWeapon);
        Assert.True(items.Receive(Detail(first, "gear-1")));
        Assert.True(page.TrySubmitAction(PlayerItemAction.Equip, 250));

        page.Select("gear-2"); var second = items.Select("gear-2", PlayerEquipmentRoles.MainWeapon);
        Assert.True(items.Receive(Detail(second, "gear-2")));

        using var scope = new InventoryCompositionTestScope();
        var session = new PlayerMenuSessionState(); session.Enter(PlayerMenuEntryPoint.InventoryMenu);
        var draw = scope.Draw(page, session, 1120, 680);
        Assert.Equal(MenuControlState.Disabled, Assert.Single(draw.Controls.Where(control => control.Id == "equip")).Button.State);
        Assert.False(page.IsActionPending("gear-2", PlayerItemAction.Equip));
        Assert.False(page.HasActionFeedback("gear-2", PlayerItemAction.Equip));
        Assert.False(page.TrySubmitAction(PlayerItemAction.Equip, 251));
        Assert.Equal(1, items.PendingMutations);
    }

    private static PlayerItemDetailResult Detail(PlayerItemDetailRequest request, string entryId) => new()
    {
        ConnectionGeneration = 1,
        QueryId = request.QueryId,
        EntryId = entryId,
        RoleCode = PlayerEquipmentRoles.MainWeapon,
        StoreRevision = 1,
        BuildRevision = 1,
        Actions = new[] { new PlayerItemActionAvailability { Action = PlayerItemAction.Equip, Enabled = true } }
    };

    private static PlayerItemQueryResult Page(long query, params string[] entryIds) => new()
    {
        ConnectionGeneration = 1,
        QueryId = query,
        StoreRevision = 1,
        BuildRevision = 1,
        Total = entryIds.Length,
        Entries = entryIds.Select(entryId => new PlayerItemEntryPacket
        {
            EntryId = entryId,
            DisplayName = "Test sword",
            Category = PlayerItemCategories.Gear,
            Location = PlayerItemLocation.Stored,
            Quantity = 1
        }).ToArray()
    };
}
