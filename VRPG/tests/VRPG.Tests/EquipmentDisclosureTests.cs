using System;
using System.Linq;
using VRPG.Client.PlayerMenu;
using VRPG.Client.PlayerMenu.Inventory;
using VRPG.Inventory;
using VRPG.Inventory.Queries;
using VRPG.Network;
using Xunit;

namespace VRPG.Tests;

[Collection("Inventory Cairo")]
public sealed class EquipmentDisclosureTests
{
    [Fact]
    public void CharacterDisclosureUsesBoundedAuthoritativeEquipmentWithoutChangingStoredBrowse()
    {
        var store = new InMemoryPlayerItemStore();
        string worn = store.Insert(InventoryViewModelTests.Item("Worn sword", "gear") with { Kind = PlayerItemStorageKind.Unique }, 1, 0).EntryId;
        string stored = store.Insert(InventoryViewModelTests.Item("Stored sword", "gear") with { Kind = PlayerItemStorageKind.Unique }, 1, store.Revision).EntryId;
        Assert.True(store.Equip("wear", worn, PlayerEquipmentRoles.MainWeapon, store.Revision).Success);
        using var page = InventoryPage.CreateReadOnlyTestStore(store); var controller = new PlayerMenuController(page);
        controller.Enter(PlayerMenuEntryPoint.CharacterMenu); page.Open(0); page.Pump(200);
        Assert.True(controller.Session.Current.EquipmentExpanded);
        Assert.Equal("gear", page.Query.Category);
        Assert.Equal(stored, Assert.Single(page.View.Entries).EntryId);
        Assert.Equal(worn, Assert.Single(page.View.Equipment.Entries).EntryId);
        Assert.Equal(1, page.View.Equipment.Total);
        Assert.False(controller.Items.CanWrite);
        Assert.Equal(worn, Assert.Single(store.Equipment().Entries).EntryId);
        using var scope = new InventoryCompositionTestScope();
        var surface = scope.Draw(page, controller.Session, 400, 350);
        Assert.Contains(surface.Controls, control => control.Id == "equipment" && control.Button.CanActivate);
        Assert.Contains("Equipped: 1", scope.Text);
        controller.Session.Update(controller.Session.Current with { EquipmentExpanded = false });
        scope.Draw(page, controller.Session, 400, 350);
        Assert.DoesNotContain("Equipped: 1", scope.Text);
    }

    [Fact]
    public void EquipmentPagesBeyondSixtyFourKeepStoredSelectionAndCharacterReturnsToGear()
    {
        var store = new InMemoryPlayerItemStore(capacity: new(256, 512, long.MaxValue));
        for (int i = 0; i < 130; i++)
        {
            string id = store.Insert(InventoryViewModelTests.Item("Worn " + i.ToString("D3"), "gear") with { Kind = PlayerItemStorageKind.Unique }, 1, store.Revision).EntryId;
            Assert.True(store.Equip("equip" + i, id, "addon-role-" + i, store.Revision).Success);
        }
        string stored = store.Insert(InventoryViewModelTests.Item("Stored", "gear") with { Kind = PlayerItemStorageKind.Unique }, 1, store.Revision).EntryId;
        using var page = InventoryPage.CreateReadOnlyTestStore(store); var controller = new PlayerMenuController(page);
        controller.Enter(PlayerMenuEntryPoint.InventoryMenu); page.Open(0); page.Pump(200); page.Select(stored);
        Assert.Equal(64, page.View.Equipment.Entries.Count); Assert.Equal(130, page.View.Equipment.Total);
        controller.ChangeInventoryQuery(page.Query with { EquipmentOffset = 128 }, 300); page.Pump(500);
        Assert.Equal(2, page.View.Equipment.Entries.Count); Assert.Equal(stored, page.View.SelectedId);
        controller.ChangeInventoryQuery(page.Query with { Category = "materials" }, 600); controller.Close();
        controller.Enter(PlayerMenuEntryPoint.CharacterMenu); page.Open(700); page.Pump(900);
        Assert.Equal("gear", page.Query.Category); Assert.True(controller.Session.Current.EquipmentExpanded);
    }

    [Fact]
    public void EquipmentPacketPageHasIndependentBoundsAndRejectsMalformedClientRows()
    {
        var query = new PlayerItemQueryRequest { ConnectionGeneration = 1, QueryId = 1, EquipmentLimit = 129 };
        Assert.Equal("request-limit", PlayerItemQueryService.Validate(query));
        var client = new PlayerItemClientService(); client.BeginSession(new() { ConnectionGeneration = 1 });
        long id = client.QueueQuery(new(), 0);
        Assert.False(client.Receive(new PlayerItemQueryResult { ConnectionGeneration = 1, QueryId = id,
            EquipmentEntries = Enumerable.Range(0, 129).Select(i => new PlayerItemEntryPacket
                { EntryId = i.ToString(), Quantity = 1, Location = PlayerItemLocation.Equipped }).ToArray(), EquipmentTotal = 129 }));
    }
}
