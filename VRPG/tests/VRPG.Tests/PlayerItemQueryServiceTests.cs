using System;
using System.Linq;
using VRPG.Inventory;
using VRPG.Inventory.Queries;
using VRPG.Network;
using Xunit;

namespace VRPG.Tests;

public sealed class PlayerItemQueryServiceTests
{
    [Fact]
    public void TypedFiltersApplyBeforeTotalAndPaging()
    {
        var store = new InMemoryPlayerItemStore();
        var a = store.Insert(Item("a"), 10000, store.Revision);
        store.Insert(Item("b"), 1, store.Revision);
        store.SetFlags("favorite", a.EntryId, PlayerItemFlags.Favorite, store.Revision);
        var page = store.Query(new("", "", 0, 128, Filters: PlayerItemFilters.Favorite));
        Assert.Equal(1, page.Total);
        Assert.Equal(10000, Assert.Single(page.Entries).Quantity);
    }

    [Fact]
    public void AllCategorySearchAndTiesAreDeterministic()
    {
        int next = 0;
        var store = new InMemoryPlayerItemStore(() => (++next).ToString("D3"));
        store.Insert(Item("b") with { Category = PlayerItemCategories.Consumables }, 1, 0);
        store.Insert(Item("a"), 1, store.Revision);
        var page = store.Query(new("", "same", 0, 128));
        Assert.Equal(new[] { "001", "002" }, page.Entries.Select(x => x.EntryId));
    }

    [Fact]
    public void MaximumPageContains128AndRecoveryIsPaged()
    {
        var store = new InMemoryPlayerItemStore();
        for (int i = 0; i < 140; i++)
            store.ImportRecovery("recover-" + i, Item(i.ToString()), 1, PlayerItemLocation.Unresolved,
                PlayerItemRecoveryReason.UnsupportedContent, "fixture", store.Revision);
        var first = store.Query(new("", "", 0, 128, PlayerItemLocation.Unresolved));
        Assert.Equal(140, first.Total);
        Assert.Equal(128, first.Entries.Length);
        Assert.Equal(12, store.Query(new("", "", 128, 128, PlayerItemLocation.Unresolved)).Entries.Length);
        Assert.Empty(store.Query(new("", "", 0, 128)).Entries);
    }

    internal static PlayerItemPrototype Item(string code) => new(PlayerItemStorageKind.Aggregate,
        "game:" + code, PlayerItemCategories.Materials, "Same", Array.Empty<byte>());

    [Theory]
    [InlineData(0)]
    [InlineData(129)]
    [InlineData(int.MaxValue)]
    public void RequestLimitsRejectBeforeQuerying(int limit)
    {
        var service = new PlayerItemQueryService();
        var result = service.Query(new InMemoryPlayerItemStore(), new() { ConnectionGeneration = 1, QueryId = 1, Limit = limit });
        Assert.Equal("request-limit", result.RejectionCode);
        Assert.Empty(result.Entries);
    }

    [Theory]
    [InlineData("bad\nsearch")]
    [InlineData("bad\u0000search")]
    public void MalformedSearchRejects(string search) => Assert.Equal("invalid-search", PlayerItemQueryService.Validate(
        new() { ConnectionGeneration = 1, QueryId = 1, Search = search }));

    [Fact]
    public void OversizedAndInvalidUnicodeSearchRejectBeforeAllocation()
    {
        Assert.Equal("invalid-search", PlayerItemQueryService.Validate(new() { ConnectionGeneration = 1, QueryId = 1, Search = new('x', 129) }));
        Assert.Equal("invalid-search", PlayerItemQueryService.Validate(new() { ConnectionGeneration = 1, QueryId = 1, Search = new string((char)0xd800, 1) }));
    }

    [Fact]
    public void UndefinedEnumsAndFiltersAreNotSilentlyNormalized()
    {
        var request = new PlayerItemQueryRequest { ConnectionGeneration = 1, QueryId = 1, Location = (PlayerItemLocation)99 };
        Assert.Equal("invalid-filter", PlayerItemQueryService.Validate(request));
        request.Location = PlayerItemLocation.Stored; request.Sort = (PlayerItemSortOrder)99;
        Assert.Equal("invalid-filter", PlayerItemQueryService.Validate(request));
        request.Sort = PlayerItemSortOrder.Name; request.Filters = (PlayerItemFilters)128;
        Assert.Equal("invalid-filter", PlayerItemQueryService.Validate(request));
    }

    [Fact]
    public void SummaryReturnsExactMaximumQuantityAndMetrics()
    {
        var service = new PlayerItemQueryService();
        var store = new InMemoryPlayerItemStore();
        store.Insert(Item("maximum"), long.MaxValue, 0);
        var result = service.Query(store, new() { ConnectionGeneration = 1, QueryId = 1 });
        Assert.Equal(long.MaxValue, Assert.Single(result.Entries).Quantity);
        Assert.Equal(long.MaxValue, result.CapacitySummary.AggregateQuantityPerEntry);
        Assert.InRange(service.LastResponseBytes, 1, PlayerItemQueryService.MaximumResponseBytes);
        Assert.True(service.LastQueryTime >= TimeSpan.Zero);
    }

    [Fact]
    public void ReservedAndAvailableFiltersUseRemainingQuantity()
    {
        var store = new InMemoryPlayerItemStore();
        var a = store.Insert(Item("a"), 10, 0);
        store.Reserve("reserve", new[] { new PlayerItemAmount(a.EntryId, 10) }, store.Revision);
        Assert.Empty(store.Query(new("", "", 0, 128, Filters: PlayerItemFilters.Available)).Entries);
        var entry = Assert.Single(store.Query(new("", "", 0, 128, Filters: PlayerItemFilters.Reserved)).Entries);
        Assert.Equal(10, entry.ReservedQuantity);
    }

    [Fact]
    public void OverlongIdentityIsRejectedInsteadOfTruncatedIntoAnotherActionTarget()
    {
        var store = new InMemoryPlayerItemStore(() => new string('a', 129));
        store.Insert(Item("a"), 1, 0);
        var result = new PlayerItemQueryService().Query(store, new() { ConnectionGeneration = 1, QueryId = 1 });
        Assert.Equal("metadata-limit", result.RejectionCode);
        Assert.Empty(result.Entries);
    }
}
