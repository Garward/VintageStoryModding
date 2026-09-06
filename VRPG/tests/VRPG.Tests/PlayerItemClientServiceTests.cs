using VRPG.Inventory;
using VRPG.Client.PlayerMenu.Inventory;
using VRPG.Network;
using Xunit;

namespace VRPG.Tests;

public sealed class PlayerItemClientServiceTests
{
    [Fact]
    public void ConnectionServiceExistsWithoutGuiDependency()
    {
        Assert.NotNull(typeof(IPlayerItemStore).Assembly.GetType("VRPG.Client.PlayerMenu.Inventory.PlayerItemClientService"));
    }

    [Fact]
    public void DebounceCancelsOldRequestsAndNewestQueryWins()
    {
        var client = Connected();
        long first = client.QueueQuery(new() { Search = "a" }, 0);
        long second = client.QueueQuery(new() { Search = "ab" }, 100);
        Assert.Null(client.Pump(299));
        Assert.Equal(second, client.Pump(300)!.QueryId);
        Assert.Null(client.Pump(500));
        Assert.False(client.Receive(Page(first)));
        Assert.True(client.Receive(Page(second)));
        Assert.Equal(second, client.CurrentPage!.QueryId);
    }

    [Fact]
    public void ChangedBuildInvalidatesComparisonAtSameStoreRevision()
    {
        var client = Connected();
        var selection = client.Select("a", PlayerEquipmentRoles.MainWeapon);
        var first = Detail(selection, 1);
        Assert.True(client.Receive(first));
        Assert.True(client.Receive(new PlayerItemInvalidationPacket { ConnectionGeneration = 1,
            StoreRevision = 1, BuildRevision = 2, Scopes = new[] { "build" } }));
        Assert.Null(client.CurrentDetail);
        Assert.False(client.Receive(first));
        Assert.False(client.CanWrite);
    }

    [Fact]
    public void SelectionIdentityAndRoleCannotBeReplacedByOldDetail()
    {
        var client = Connected();
        var old = client.Select("a", PlayerEquipmentRoles.Head);
        var current = client.Select("b", PlayerEquipmentRoles.Head);
        Assert.False(client.Receive(Detail(old, 1)));
        var result = Detail(current, 1);
        result.RoleCode = PlayerEquipmentRoles.Feet;
        Assert.False(client.Receive(result));
        result.RoleCode = current.RoleCode;
        Assert.True(client.Receive(result));
    }

    [Fact]
    public void DiscoveryAcceptsOnlyBoundedServerRoleForCurrentEntryAndQuery()
    {
        var client = Connected();
        var selection = client.Select("a", "");
        var result = Detail(selection, 1);
        result.RoleCode = PlayerEquipmentRoles.MainWeapon;
        Assert.True(client.Receive(result));
        Assert.Equal(PlayerEquipmentRoles.MainWeapon, client.CurrentDetail!.RoleCode);
        var next = client.Select("b", "");
        Assert.False(client.Receive(result));
        var oversized = Detail(next, 1);
        oversized.RoleCode = new('x', 129);
        Assert.False(client.Receive(oversized));
    }

    [Fact]
    public void MenuClosureDoesNotCancelPendingMutation()
    {
        var client = Ready();
        var request = client.Submit(new() { Action = PlayerItemAction.Destroy, EntryId = "a", Quantity = 9_007_199_254_740_993L });
        Assert.NotNull(request);
        client.CloseMenu();
        Assert.Equal(1, client.PendingMutations);
        bool completed = false;
        client.ActionCompleted += _ => completed = true;
        Assert.True(client.Receive(new PlayerItemActionResult { ConnectionGeneration = 1, RequestId = request!.RequestId,
            StoreRevision = 1, BuildRevision = 1, RejectionCode = "unsupported-action" }));
        Assert.True(completed);
        Assert.Equal(0, client.PendingMutations);
    }

    [Fact]
    public void ReconnectDiscardsOldGenerationAndRequiresFreshQueryBeforeWrites()
    {
        var client = Ready();
        var mutation = client.Submit(new() { Action = PlayerItemAction.Destroy, EntryId = "a", Quantity = 1 })!;
        client.BeginSession(new() { ConnectionGeneration = 2 });
        Assert.False(client.CanWrite);
        Assert.Null(client.Submit(new() { Action = PlayerItemAction.Destroy, EntryId = "a", Quantity = 1 }));
        Assert.False(client.Receive(new PlayerItemActionResult { ConnectionGeneration = 1, RequestId = mutation.RequestId }));
        long query = client.QueueQuery(new(), 0);
        client.Pump(200);
        Assert.False(client.Receive(Page(query)));
        var page = Page(query); page.ConnectionGeneration = 2;
        Assert.True(client.Receive(page));
        Assert.True(client.CanWrite);
    }

    [Fact]
    public void PageAndDetailCachesEvictAtFixedBoundsAndOwnCopies()
    {
        var client = Connected();
        for (int i = 0; i < 40; i++)
        {
            long query = client.QueueQuery(new(), 0);
            client.Pump(200);
            Assert.True(client.Receive(Page(query)));
            var selected = client.Select("a", PlayerEquipmentRoles.Head);
            Assert.True(client.Receive(Detail(selected, 1)));
        }
        Assert.Equal(PlayerItemClientService.PageCacheCapacity, client.CachedPages);
        Assert.Equal(PlayerItemClientService.DetailCacheCapacity, client.CachedDetails);
        var exposed = client.CurrentPage!;
        exposed.Total = 9000;
        Assert.NotEqual(9000, client.CurrentPage!.Total);
    }

    [Fact]
    public void OlderActionCompletionDoesNotRegressNewerRevisions()
    {
        var client = Ready();
        var request = client.Submit(new() { Action = PlayerItemAction.Destroy, EntryId = "a", Quantity = 1 })!;
        long query = client.QueueQuery(new(), 0);
        client.Pump(200);
        var newer = Page(query); newer.StoreRevision = 3; newer.BuildRevision = 4;
        Assert.True(client.Receive(newer));
        Assert.True(client.Receive(new PlayerItemActionResult { ConnectionGeneration = 1, RequestId = request.RequestId,
            StoreRevision = 1, BuildRevision = 1 }));
        Assert.Equal(3, client.StoreRevision);
        Assert.Equal(4, client.BuildRevision);
    }

    [Fact]
    public void OversizedResponseIsRejectedWithoutCacheGrowth()
    {
        var client = Connected();
        long query = client.QueueQuery(new(), 0);
        client.Pump(200);
        var response = Page(query); response.Entries = new PlayerItemEntryPacket[129];
        Assert.False(client.Receive(response));
        Assert.Equal(0, client.CachedPages);
    }

    [Fact]
    public void OversizedMetadataAndNonFiniteComparisonsCannotEnterCache()
    {
        var client = Connected();
        long query = client.QueueQuery(new(), 0);
        client.Pump(200);
        var response = Page(query);
        response.Entries = new[] { new PlayerItemEntryPacket { EntryId = "a", DisplayName = new('x', 257), Quantity = 1 } };
        Assert.False(client.Receive(response));
        var selected = client.Select("a", PlayerEquipmentRoles.Head);
        var detail = Detail(selected, 1);
        detail.Comparison = new[] { new PlayerItemComparisonPacket { Difference = double.NaN } };
        Assert.False(client.Receive(detail));
    }

    private static PlayerItemClientService Connected()
    {
        var service = new PlayerItemClientService();
        service.BeginSession(new() { ConnectionGeneration = 1 });
        return service;
    }

    private static PlayerItemClientService Ready()
    {
        var client = Connected();
        long query = client.QueueQuery(new(), 0);
        client.Pump(200);
        Assert.True(client.Receive(Page(query)));
        return client;
    }

    private static PlayerItemQueryResult Page(long query) => new() { ConnectionGeneration = 1,
        QueryId = query, StoreRevision = 1, BuildRevision = 1 };
    private static PlayerItemDetailResult Detail(PlayerItemDetailRequest request, long build) => new()
    {
        ConnectionGeneration = request.ConnectionGeneration, QueryId = request.QueryId,
        EntryId = request.EntryId, RoleCode = request.RoleCode, StoreRevision = 1, BuildRevision = build
    };
}
