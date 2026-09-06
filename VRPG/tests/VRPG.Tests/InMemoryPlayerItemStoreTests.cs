using System.Collections.Generic;
using VRPG.Inventory;
using Xunit;

namespace VRPG.Tests;

public sealed class InMemoryPlayerItemStoreTests
{
    [Fact]
    public void AggregatesLargeExactQuantitiesIntoOneEntry()
    {
        var ids = new Queue<string>(new[] { "material-1" });
        var store = new InMemoryPlayerItemStore(ids.Dequeue);
        PlayerItemPrototype material = Material(new byte[] { 1, 2, 3 });

        PlayerItemMutationResult first = store.Insert(material, 6_000, 0);
        PlayerItemMutationResult second = store.Insert(material, 4_000, first.Revision);
        PlayerItemPage page = store.Query(new PlayerItemQuery("Materials", "", 0, 20));

        Assert.True(second.Success);
        Assert.Single(page.Entries);
        Assert.Equal(10_000, page.Entries[0].Quantity);
    }

    [Fact]
    public void KeepsAttributeDifferentEntriesSeparate()
    {
        var ids = new Queue<string>(new[] { "first", "second" });
        var store = new InMemoryPlayerItemStore(ids.Dequeue);

        PlayerItemMutationResult first = store.Insert(Material(new byte[] { 1 }), 10, 0);
        store.Insert(Material(new byte[] { 2 }), 10, first.Revision);

        Assert.Equal(2, store.Query(new PlayerItemQuery("Materials", "", 0, 20)).Total);
    }

    [Fact]
    public void NeverMergesUniqueGear()
    {
        var ids = new Queue<string>(new[] { "gear-a", "gear-b" });
        var store = new InMemoryPlayerItemStore(ids.Dequeue);
        var gear = new PlayerItemPrototype(
            PlayerItemStorageKind.Unique,
            "game:sword",
            "Gear",
            "Sword",
            new byte[] { 7 });

        PlayerItemMutationResult first = store.Insert(gear, 1, 0);
        store.Insert(gear, 1, first.Revision);

        Assert.Equal(2, store.Query(new PlayerItemQuery("Gear", "", 0, 20)).Total);
    }

    [Fact]
    public void RejectsStaleMutationWithoutChangingState()
    {
        var store = new InMemoryPlayerItemStore(() => "material");
        store.Insert(Material(new byte[] { 1 }), 10, 0);

        PlayerItemMutationResult stale = store.Extract("material", 2, 0);

        Assert.False(stale.Success);
        Assert.Equal(PlayerItemMutationRejection.StaleRevision, stale.Rejection);
        Assert.Equal(10, store.Query(new PlayerItemQuery("", "", 0, 20)).Entries[0].Quantity);
    }

    [Fact]
    public void BoundsQueryPageSize()
    {
        int entryCount = InMemoryPlayerItemStore.MaximumPageSize + 20;
        var ids = new Queue<string>();
        for (int i = 0; i < entryCount; i++) ids.Enqueue("entry-" + i);
        var store = new InMemoryPlayerItemStore(ids.Dequeue,
            capacity: PlayerItemCapacity.Default with { UniqueEntryLimit = entryCount });
        long revision = 0;
        for (int i = 0; i < entryCount; i++)
        {
            var unique = new PlayerItemPrototype(
                PlayerItemStorageKind.Unique,
                "game:gear-" + i,
                "Gear",
                "Gear " + i,
                new byte[] { (byte)i });
            revision = store.Insert(unique, 1, revision).Revision;
        }

        PlayerItemPage page = store.Query(new PlayerItemQuery("Gear", "", 0, 10_000));

        Assert.Equal(entryCount, page.Total);
        Assert.Equal(InMemoryPlayerItemStore.MaximumPageSize, page.Entries.Length);
    }

    private static PlayerItemPrototype Material(byte[] state) => new(
        PlayerItemStorageKind.Aggregate,
        "game:metalbit-copper",
        "Materials",
        "Copper bits",
        state);
}
