using System;
using System.Linq;
using VRPG.Client.PlayerMenu;
using VRPG.Client.PlayerMenu.Inventory;
using VRPG.Inventory;
using VRPG.Inventory.Queries;
using Xunit;

namespace VRPG.Tests;

public sealed class InventoryViewModelTests
{
    [Fact]
    public void CatalogOnlyPagingPreservesActualControllerInspectionAndExplicitSelection()
    {
        var store = new InMemoryPlayerItemStore();
        string id = store.Insert(Item("Copper", "materials"), 20, 0).EntryId;
        using var page = Page(store); var controller = new PlayerMenuController(page);
        controller.Enter(PlayerMenuEntryPoint.InventoryMenu);
        controller.ChangeInventoryQuery(new(Category: "materials"), 0); page.Pump(200); page.Select(id);
        page.View.Scroll = 3;
        Assert.True(page.View.Selection.Set(page.View.Selected!, 7));
        controller.Session.Update(controller.Session.Current with { SelectedEntry = id, ScrollAnchor = 3, InspectorScroll = 72 });
        var detail = page.View.Detail;
        controller.ChangeInventoryQuery(page.Query with { CategoryOffset = 64 }, 300);
        Assert.Equal(id, page.View.SelectedId); Assert.Same(detail, page.View.Detail);
        Assert.Equal(3, page.View.Scroll); Assert.Equal(3, controller.Session.Current.ScrollAnchor);
        Assert.Equal(72, controller.Session.Current.InspectorScroll); Assert.Equal(id, controller.Session.Current.SelectedEntry);
        page.Pump(500);
        Assert.Same(detail, page.View.Detail); Assert.Equal(7, page.View.Selection.Amounts[id]);
        controller.ChangeInventoryQuery(page.Query with { Search = "nothing" }, 600);
        Assert.Equal("", controller.Session.Current.SelectedEntry); Assert.Null(page.View.Detail);
    }
    [Fact]
    public void NewerDetailRevisionRefreshesOldSummaryBeforePresentingItAsCurrent()
    {
        var store = new InMemoryPlayerItemStore();
        var prototype = Item("Copper", "materials");
        string id = store.Insert(prototype, 10, 0).EntryId;
        using var page = Page(store);
        page.ChangeQuery(new(Category: "materials"), 0); page.Pump(200);
        store.Insert(prototype, 5, store.Revision);
        page.Select(id);
        Assert.True(page.View.Stale);
        Assert.True(page.View.Loading);
        page.Pump(400);
        Assert.Equal(15, page.View.Selected!.Quantity);
        Assert.False(page.View.Stale);
    }
    [Fact]
    public void ControllerUsesSameReadOnlyOwnerAndRealGearDetailsWithoutEquipping()
    {
        var f = new GearReadinessValidatorTests.Fixture();
        string id = f.Insert(VRPG.Inventory.Gear.GearItemStateCodec.Encode(GearReadinessValidatorTests.Instance()));
        using var page = InventoryPage.CreateReadOnlyTestStore(f.Store, f.Data);
        var controller = new PlayerMenuController(page);
        Assert.Same(page.Items, controller.Items);
        page.Open(0); page.Pump(200); page.Select(id);
        Assert.NotEmpty(page.View.Detail!.Comparison);
        Assert.Equal(20, page.View.Detail.RequiredLevel);
        Assert.False(controller.Items.CanWrite);
        Assert.Null(controller.Items.Submit(new() { EntryId = id, Quantity = 1, Action = VRPG.Network.PlayerItemAction.Equip,
            RoleCode = PlayerEquipmentRoles.MainWeapon }));
        Assert.Empty(f.Store.Equipment().Entries);
    }

    [Fact]
    public void FullOrdinaryCapacityAndFilteredCoreCountsComeFromStore()
    {
        var store = new InMemoryPlayerItemStore();
        for (int i = 0; i < 120; i++) store.Insert(Item("Gear " + i, "gear") with { Kind = PlayerItemStorageKind.Unique }, 1, store.Revision);
        for (int i = 0; i < 512; i++) store.Insert(Item("Material " + i, "materials"), long.MaxValue, store.Revision);
        using var page = Page(store);
        page.ChangeQuery(new(Category: "", Search: "no match"), 0); page.Pump(200);
        Assert.Empty(page.View.Entries);
        Assert.Equal(120, page.View.Capacity!.UniqueEntriesUsed);
        Assert.Equal(512, page.View.Capacity.AggregateEntriesUsed);
        Assert.Equal(120, page.View.Categories.Single(x => x.Code == "gear").Count);
        Assert.Equal(512, page.View.Categories.Single(x => x.Code == "materials").Count);
    }
    [Fact]
    public void OccupiedAddonCatalogIsLocationScopedAndPagedIndependentlyOfFilteredEntries()
    {
        var registry = new PlayerItemCategoryRegistry(Enumerable.Range(0, 140)
            .Select(i => new PlayerItemCategoryDefinition("addon" + i.ToString("D3"), "Addon " + i)));
        var store = new InMemoryPlayerItemStore(categories: registry);
        for (int i = 0; i < 139; i++) store.Insert(Item("Item " + i, "addon" + i.ToString("D3")), 1, store.Revision);
        using var page = Page(store);
        page.ChangeQuery(new(Category: "", Search: "nothing"), 0); page.Pump(200);
        Assert.Empty(page.View.Entries);
        Assert.Equal(139, page.View.CategoryTotal);
        Assert.Equal(69, page.View.Categories.Count);
        Assert.DoesNotContain(page.View.Categories, x => x.Code == "addon139");
        page.ChangeQuery(page.Query with { CategoryOffset = 128 }, 300); page.Pump(500);
        Assert.Contains(page.View.Categories, x => x.Code == "addon138");
        Assert.Equal(16, page.View.Categories.Count);
        page.ChangeQuery(new(Category: "", Search: "Item 138"), 600); page.Pump(800);
        Assert.Equal("Addon 138", Assert.Single(page.View.Entries).CategoryName);
    }

    [Fact]
    public void LostReplyTimesOutWithRetryAndDisposalUnsubscribes()
    {
        var items = new PlayerItemClientService(); items.BeginSession(new() { ConnectionGeneration = 1 });
        var page = new InventoryPage(items); page.Open(0); page.Pump(10200);
        Assert.False(page.View.Loading); Assert.Equal("timeout", page.View.Error);
        page.Refresh(11000); Assert.True(page.View.Loading);
        page.Dispose();
        items.BeginSession(new() { ConnectionGeneration = 2 });
        Assert.False(page.View.Loading);
    }
    [Fact]
    public void EmptyStoreKeepsFiveCoreTabsWithoutFabricatingLoadingCapacity()
    {
        using var page = Page(new());
        Assert.Null(page.View.Capacity);
        Assert.Equal(5, page.View.Categories.Count);
        page.Open(0); page.Pump(199);
        Assert.True(page.View.Loading);
        Assert.Null(page.View.Capacity);
        page.Pump(200);
        Assert.False(page.View.Loading);
        Assert.Empty(page.View.Entries);
        Assert.Equal(120, page.View.Capacity!.UniqueEntryLimit);
        Assert.Equal(512, page.View.Capacity.AggregateEntryLimit);
    }

    [Fact]
    public void AllTypesUsesRealQueryAndNoMatchClearsInspector()
    {
        var store = new InMemoryPlayerItemStore();
        store.Insert(Item("Copper", PlayerItemCategories.Materials), 10000000, 0);
        store.Insert(Item("Unknown", "removed-addon"), 1, store.Revision);
        using var page = Page(store);
        page.ChangeQuery(new(Category: ""), 0); page.Pump(200);
        Assert.Equal(2, page.View.Entries.Count);
        Assert.Equal(new[] { "materials", "miscellaneous" }, page.View.Entries.Select(x => x.Category));
        page.Select(page.View.Entries[0].EntryId);
        Assert.NotNull(page.View.Detail);
        page.ChangeQuery(new(Category: "", Search: "no match"), 300); page.Pump(500);
        Assert.Empty(page.View.Entries);
        Assert.Null(page.View.Selected);
        Assert.Null(page.View.Detail);
    }

    [Fact]
    public void RefreshPreservesIdentityScrollAndMarksCachedRowsStale()
    {
        var store = new InMemoryPlayerItemStore();
        var inserted = store.Insert(Item("Copper", "materials"), 10, 0);
        using var page = Page(store);
        page.ChangeQuery(new(Category: "materials"), 0); page.Pump(200);
        page.Select(inserted.EntryId); page.View.Scroll = 4;
        page.Refresh(300);
        Assert.True(page.View.Stale);
        Assert.Equal(10, page.View.Selected!.Quantity);
        page.Pump(500);
        Assert.Equal(inserted.EntryId, page.View.Selected!.EntryId);
        Assert.Equal(4, page.View.Scroll);
        store.Extract("remove", inserted.EntryId, 10, store.Revision);
        page.Refresh(600); page.Pump(800);
        Assert.Null(page.View.Selected);
        Assert.Null(page.View.Detail);
    }

    [Fact]
    public void RecoverySeparatesLocationsAndKeepsUnavailableRecordsInspectable()
    {
        var store = new InMemoryPlayerItemStore();
        store.ImportRecovery("missing", Item("Missing", "materials"), long.MaxValue,
            PlayerItemLocation.Unresolved, PlayerItemRecoveryReason.MissingAsset, "test", 0);
        using var page = Page(store);
        page.ChangeQuery(new(Category: "", Location: PlayerItemLocation.Unresolved), 0); page.Pump(200);
        var row = Assert.Single(page.View.Entries);
        Assert.False(InventoryItemRow.CanSelectQuantity(row));
        page.Select(row.EntryId);
        Assert.Equal(long.MaxValue, page.View.Selected!.Quantity);
        Assert.Equal(1, page.View.RecoveryCount);
        Assert.Equal("recovery-unresolved", RecoveryView.ReasonKey(row));
    }

    internal static PlayerItemPrototype Item(string name, string category) => new(PlayerItemStorageKind.Aggregate,
        "game:ingot-copper", category, name, System.Text.Encoding.UTF8.GetBytes(name));
    internal static InventoryPage Page(InMemoryPlayerItemStore store) => InventoryPage.CreateReadOnlyTestStore(store);
}
