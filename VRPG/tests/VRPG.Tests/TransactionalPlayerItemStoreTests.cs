using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VRPG.Inventory;
using Xunit;

namespace VRPG.Tests;

public sealed class TransactionalPlayerItemStoreTests
{
    [Fact]
    public void ReplayingInsertRequestReturnsOriginalResultWithoutDuplicatingQuantity()
    {
        var store = Store("material");

        PlayerItemMutationResult first = store.Insert("request-1", Material(), 25, 0);
        PlayerItemMutationResult replay = store.Insert("request-1", Material(), 25, 0);

        Assert.True(first.Success);
        Assert.Equal(first, replay);
        Assert.Equal(25, store.Query(Query()).Entries.Single().Quantity);
        Assert.Equal(1, store.Revision);
    }

    [Fact]
    public void ReusingRequestIdForDifferentMutationIsRejected()
    {
        var store = Store("material");
        store.Insert("same-id", Material(), 25, 0);

        PlayerItemMutationResult conflict = store.Insert("same-id", Material(), 26, 1);

        Assert.False(conflict.Success);
        Assert.Equal(PlayerItemMutationRejection.RequestIdConflict, conflict.Rejection);
        Assert.Equal(25, store.Query(Query()).Entries.Single().Quantity);
    }

    [Fact]
    public void ReservationCommitsMultipleAmountsAsOneRevision()
    {
        var ids = new Queue<string>(new[] { "copper", "iron" });
        var reservations = new Queue<string>(new[] { "reservation-1" });
        var store = new InMemoryPlayerItemStore(ids.Dequeue, reservations.Dequeue);
        long revision = store.Insert(Material("game:copper"), 30, 0).Revision;
        revision = store.Insert(Material("game:iron"), 20, revision).Revision;

        PlayerItemReservationResult reserved = store.Reserve(
            "reserve-request",
            new[] { new PlayerItemAmount("copper", 7), new PlayerItemAmount("iron", 5) },
            revision);
        Assert.True(reserved.Success);
        PlayerItemReservationSnapshot snapshot = store.Reservation(reserved.ReservationId);
        Assert.True(snapshot.Exists);
        Assert.Equal(2, snapshot.Entries.Length);
        Assert.Contains(snapshot.Entries, entry => entry.EntryId == "copper" && entry.Quantity == 7);

        PlayerItemOperationResult committed = store.CommitReservation("commit-request", reserved.ReservationId);

        Assert.True(committed.Success);
        Assert.Equal(23, Entry(store, "copper").Quantity);
        Assert.Equal(15, Entry(store, "iron").Quantity);
        Assert.All(store.Query(Query()).Entries, entry => Assert.Equal(0, entry.ReservedQuantity));
        Assert.Equal(revision + 2, store.Revision);
    }

    [Fact]
    public void ReserveAndCommitRequestsAreIndependentlyIdempotent()
    {
        var store = Store("material", reservationId: "reservation-1");
        long revision = store.Insert(Material(), 10, 0).Revision;
        PlayerItemAmount[] amounts = { new("material", 3) };

        PlayerItemReservationResult firstReserve = store.Reserve("reserve-request", amounts, revision);
        PlayerItemReservationResult replayedReserve = store.Reserve("reserve-request", amounts, revision);
        PlayerItemOperationResult firstCommit = store.CommitReservation(
            "commit-request",
            firstReserve.ReservationId);
        PlayerItemOperationResult replayedCommit = store.CommitReservation(
            "commit-request",
            firstReserve.ReservationId);

        Assert.Equal(firstReserve, replayedReserve);
        Assert.Equal(firstCommit, replayedCommit);
        Assert.Equal(7, Entry(store, "material").Quantity);
        Assert.Equal(3, store.Revision);
    }

    [Fact]
    public void FailedReservationDoesNotPartiallyReserveEarlierEntries()
    {
        var store = Store("copper", reservationId: "reservation-1");
        long revision = store.Insert(Material(), 10, 0).Revision;

        PlayerItemReservationResult result = store.Reserve(
            "reserve-request",
            new[] { new PlayerItemAmount("copper", 5), new PlayerItemAmount("missing", 1) },
            revision);

        Assert.False(result.Success);
        Assert.Equal(PlayerItemOperationRejection.EntryMissing, result.Rejection);
        Assert.Equal(revision, store.Revision);
        Assert.Equal(0, Entry(store, "copper").ReservedQuantity);
    }

    [Fact]
    public void RollbackReleasesQuantityAndIsIdempotent()
    {
        var store = Store("copper", reservationId: "reservation-1");
        long revision = store.Insert(Material(), 10, 0).Revision;
        PlayerItemReservationResult reserved = store.Reserve(
            "reserve-request",
            new[] { new PlayerItemAmount("copper", 8) },
            revision);

        PlayerItemOperationResult first = store.RollbackReservation("rollback-request", reserved.ReservationId);
        PlayerItemOperationResult replay = store.RollbackReservation("rollback-request", reserved.ReservationId);

        Assert.True(first.Success);
        Assert.Equal(first, replay);
        Assert.Equal(10, Entry(store, "copper").Quantity);
        Assert.Equal(0, Entry(store, "copper").ReservedQuantity);
    }

    [Fact]
    public void ReservedQuantityCannotBeExtractedOrEquipped()
    {
        var store = Store("gear", reservationId: "reservation-1");
        long revision = store.Insert(Gear(), 1, 0).Revision;
        PlayerItemReservationResult reserved = store.Reserve(
            "reserve-request",
            new[] { new PlayerItemAmount("gear", 1) },
            revision);

        PlayerItemMutationResult extract = store.Extract("extract-request", "gear", 1, reserved.Revision);
        PlayerItemOperationResult equip = store.Equip(
            "equip-request",
            "gear",
            PlayerEquipmentRoles.MainWeapon,
            reserved.Revision);

        Assert.Equal(PlayerItemMutationRejection.EntryReserved, extract.Rejection);
        Assert.Equal(PlayerItemOperationRejection.EntryReserved, equip.Rejection);
    }

    [Fact]
    public void EquippingNewRoleOccupantReturnsPriorItemToStorage()
    {
        var ids = new Queue<string>(new[] { "sword-a", "sword-b" });
        var store = new InMemoryPlayerItemStore(ids.Dequeue);
        long revision = store.Insert(Gear("game:sword-a"), 1, 0).Revision;
        revision = store.Insert(Gear("game:sword-b"), 1, revision).Revision;
        revision = store.Equip("equip-a", "sword-a", PlayerEquipmentRoles.MainWeapon, revision).Revision;

        PlayerItemOperationResult result = store.Equip(
            "equip-b",
            "sword-b",
            PlayerEquipmentRoles.MainWeapon,
            revision);

        Assert.True(result.Success);
        Assert.Equal("sword-b", store.Equipment().Entries.Single().EntryId);
        Assert.Contains(store.Query(Query()).Entries, entry => entry.EntryId == "sword-a");
        Assert.DoesNotContain(store.Query(Query()).Entries, entry => entry.EntryId == "sword-b");
        Assert.Contains(
            store.Query(Query(PlayerItemLocation.Equipped)).Entries,
            entry => entry.EntryId == "sword-b");
    }

    [Fact]
    public void CompletedEquipPreflightReturnsReplayConflictOrNewWithoutMutation()
    {
        IPlayerItemStore store = Store("gear");
        long revision = store.Insert("insert-preflight", Gear(), 1, 0).Revision;
        PlayerItemOperationResult accepted = store.Equip(
            "equip-preflight",
            "gear",
            PlayerEquipmentRoles.MainWeapon,
            revision);
        PlayerItemOperationResult moved = store.MoveToRecovery(
            "move-after-preflight",
            "gear",
            PlayerItemLocation.Unresolved,
            PlayerItemRecoveryReason.UnsupportedContent,
            "test",
            accepted.Revision);

        bool replayed = store.TryGetCompletedEquip(
            "equip-preflight",
            "gear",
            PlayerEquipmentRoles.MainWeapon,
            revision,
            out PlayerItemOperationResult? replay);
        bool conflicted = store.TryGetCompletedEquip(
            "equip-preflight",
            "missing",
            PlayerEquipmentRoles.Head,
            moved.Revision,
            out PlayerItemOperationResult? conflict);
        bool isNew = store.TryGetCompletedEquip(
            "new-equip",
            "gear",
            PlayerEquipmentRoles.MainWeapon,
            moved.Revision,
            out PlayerItemOperationResult? newResult);

        Assert.True(replayed);
        Assert.Equal(accepted, replay);
        Assert.True(conflicted);
        Assert.Equal(PlayerItemOperationRejection.RequestIdConflict, conflict!.Rejection);
        Assert.False(isNew);
        Assert.Null(newResult);
        Assert.Equal(moved.Revision, store.Revision);
    }

    [Fact]
    public void UnequipReturnsCanonicalEntryToStorage()
    {
        var store = Store("gear");
        long revision = store.Insert(Gear(), 1, 0).Revision;
        revision = store.Equip("equip", "gear", PlayerEquipmentRoles.MainWeapon, revision).Revision;

        PlayerItemOperationResult result = store.Unequip(
            "unequip",
            PlayerEquipmentRoles.MainWeapon,
            revision);

        Assert.True(result.Success);
        Assert.Empty(store.Equipment().Entries);
        Assert.Equal(PlayerItemLocation.Stored, Entry(store, "gear").Location);
    }

    [Fact]
    public void ProtectionBlocksReservationExtractionAndDestruction()
    {
        var store = Store("material", reservationId: "reservation-1");
        long revision = store.Insert(Material(), 10, 0).Revision;
        revision = store.SetFlags(
            "protect",
            "material",
            PlayerItemFlags.Favorite | PlayerItemFlags.Protected,
            revision).Revision;

        PlayerItemReservationResult reserve = store.Reserve(
            "reserve",
            new[] { new PlayerItemAmount("material", 1) },
            revision);
        PlayerItemMutationResult extract = store.Extract("extract", "material", 1, revision);
        PlayerItemOperationResult destroy = store.Destroy("destroy", "material", 1, true, revision);

        Assert.Equal(PlayerItemOperationRejection.EntryProtected, reserve.Rejection);
        Assert.Equal(PlayerItemMutationRejection.EntryProtected, extract.Rejection);
        Assert.Equal(PlayerItemOperationRejection.EntryProtected, destroy.Rejection);
        Assert.Equal(10, Entry(store, "material").Quantity);
    }

    [Fact]
    public void FlagsAreVisibleAndCannotChangeDuringAReservation()
    {
        var store = Store("material", reservationId: "reservation-1");
        long revision = store.Insert(Material(), 10, 0).Revision;
        PlayerItemOperationResult favorited = store.SetFlags(
            "favorite",
            "material",
            PlayerItemFlags.Favorite,
            revision);
        PlayerItemReservationResult reserved = store.Reserve(
            "reserve",
            new[] { new PlayerItemAmount("material", 2) },
            favorited.Revision);

        PlayerItemOperationResult changed = store.SetFlags(
            "change-flags",
            "material",
            PlayerItemFlags.Junk,
            reserved.Revision);

        Assert.Equal(PlayerItemOperationRejection.EntryReserved, changed.Rejection);
        Assert.Equal(PlayerItemFlags.Favorite, Entry(store, "material").Flags);
    }

    [Fact]
    public void DestructionRequiresExplicitConfirmation()
    {
        var store = Store("material");
        long revision = store.Insert(Material(), 10, 0).Revision;

        PlayerItemOperationResult rejected = store.Destroy(
            "destroy-preview",
            "material",
            3,
            false,
            revision);
        PlayerItemOperationResult accepted = store.Destroy(
            "destroy-confirmed",
            "material",
            3,
            true,
            revision);

        Assert.Equal(PlayerItemOperationRejection.ConfirmationRequired, rejected.Rejection);
        Assert.True(accepted.Success);
        Assert.Equal(7, Entry(store, "material").Quantity);
    }

    [Theory]
    [InlineData(PlayerItemLocation.Overflow, PlayerItemRecoveryReason.CapacityExceeded)]
    [InlineData(PlayerItemLocation.Unresolved, PlayerItemRecoveryReason.MissingAsset)]
    public void RecoveryLocationsRemainQueryableAndRecoverable(
        PlayerItemLocation destination,
        PlayerItemRecoveryReason reason)
    {
        var store = Store("item");
        long revision = store.Insert(Material(), 10, 0).Revision;
        PlayerItemOperationResult moved = store.MoveToRecovery(
            "move",
            "item",
            destination,
            reason,
            "test",
            revision);

        PlayerRecoverySummary recovery = store.Recovery().Entries.Single();
        PlayerItemOperationResult restored = store.Recover("recover", "item", moved.Revision);

        Assert.Equal(destination, recovery.Location);
        Assert.Equal(reason, recovery.Reason);
        Assert.True(restored.Success);
        Assert.Equal(PlayerItemLocation.Stored, Entry(store, "item").Location);
        Assert.Empty(store.Recovery().Entries);
    }

    [Fact]
    public void RecoveringAggregateMergesWithExistingExactEntry()
    {
        var ids = new Queue<string>(new[] { "recovery", "stored" });
        var store = new InMemoryPlayerItemStore(ids.Dequeue);
        long revision = store.Insert(Material(), 5, 0).Revision;
        revision = store.MoveToRecovery(
            "move", "recovery", PlayerItemLocation.Overflow,
            PlayerItemRecoveryReason.CapacityExceeded, "test", revision).Revision;
        revision = store.Insert(Material(), 7, revision).Revision;

        PlayerItemOperationResult recovered = store.Recover("recover", "recovery", revision);

        Assert.True(recovered.Success);
        Assert.Single(store.Query(Query()).Entries);
        Assert.Equal(12, store.Query(Query()).Entries.Single().Quantity);
    }

    [Fact]
    public void CapacitySeparatelyLimitsUniqueAndAggregateEntries()
    {
        var ids = new Queue<string>(new[] { "unique", "aggregate" });
        var capacity = new PlayerItemCapacity(1, 1, 100);
        var store = new InMemoryPlayerItemStore(ids.Dequeue, capacity: capacity);
        long revision = store.Insert(Gear("game:first"), 1, 0).Revision;
        PlayerItemMutationResult uniqueFull = store.Insert(Gear("game:second"), 1, revision);
        PlayerItemMutationResult aggregate = store.Insert(Material(), 100, revision);
        PlayerItemMutationResult quantityFull = store.Insert(Material(), 1, aggregate.Revision);

        Assert.Equal(PlayerItemMutationRejection.CapacityExceeded, uniqueFull.Rejection);
        Assert.True(aggregate.Success);
        Assert.Equal(PlayerItemMutationRejection.QuantityOverflow, quantityFull.Rejection);
    }

    [Fact]
    public void CheckedQuantityArithmeticRejectsLongOverflowWithoutMutation()
    {
        var store = Store("material");
        PlayerItemMutationResult initial = store.Insert(Material(), long.MaxValue, 0);

        PlayerItemMutationResult overflow = store.Insert(Material(), 1, initial.Revision);

        Assert.Equal(PlayerItemMutationRejection.QuantityOverflow, overflow.Rejection);
        Assert.Equal(long.MaxValue, Entry(store, "material").Quantity);
        Assert.Equal(initial.Revision, store.Revision);
    }

    [Fact]
    public void QueriesHaveStableExplicitSortOrders()
    {
        var ids = new Queue<string>(new[] { "z", "a", "m" });
        var store = new InMemoryPlayerItemStore(ids.Dequeue);
        long revision = store.Insert(Material("game:zinc", "Zinc"), 2, 0).Revision;
        revision = store.Insert(Material("game:alum", "Alum"), 7, revision).Revision;
        store.Insert(Material("game:magnetite", "Magnetite"), 4, revision);

        string[] byName = store.Query(Query(sort: PlayerItemSortOrder.Name)).Entries
            .Select(entry => entry.DisplayName).ToArray();
        long[] byQuantity = store.Query(Query(sort: PlayerItemSortOrder.QuantityDescending)).Entries
            .Select(entry => entry.Quantity).ToArray();

        Assert.Equal(new[] { "Alum", "Magnetite", "Zinc" }, byName);
        Assert.Equal(new long[] { 7, 4, 2 }, byQuantity);
    }

    [Fact]
    public void QueryPagesAreImmutableRevisionSnapshotsWithIndexedFiltering()
    {
        var ids = new Queue<string>(new[] { "ore", "gear" });
        var store = new InMemoryPlayerItemStore(ids.Dequeue);
        long revision = store.Insert(Material("game:copperore", "Copper Ore"), 2, 0).Revision;
        PlayerItemPage firstPage = store.Query(new PlayerItemQuery(
            PlayerItemCategories.Materials,
            "copper",
            0,
            1));

        store.Insert(Gear(), 1, revision);
        PlayerItemPage currentPage = store.Query(new PlayerItemQuery(
            PlayerItemCategories.Materials,
            "copper",
            0,
            1));

        Assert.Equal(revision, firstPage.Revision);
        Assert.Single(firstPage.Entries);
        Assert.Equal("ore", firstPage.Entries[0].EntryId);
        Assert.Equal(store.Revision, currentPage.Revision);
        Assert.Equal(firstPage.Entries, currentPage.Entries);
    }

    [Fact]
    public async Task ConcurrentReplayOfSameRequestCommitsOnlyOnce()
    {
        var store = Store("material");
        var results = new ConcurrentBag<PlayerItemMutationResult>();
        Task[] tasks = Enumerable.Range(0, 32).Select(_ => Task.Run(() =>
            results.Add(store.Insert("one-request", Material(), 5, 0)))).ToArray();

        await Task.WhenAll(tasks);

        Assert.Equal(32, results.Count);
        Assert.All(results, result => Assert.True(result.Success));
        Assert.All(results, result => Assert.Equal(results.First(), result));
        Assert.Equal(5, Entry(store, "material").Quantity);
        Assert.Equal(1, store.Revision);
    }

    [Fact]
    public void ExactStateComparisonSeparatesForcedHashCollision()
    {
        var ids = new Queue<string>(new[] { "first", "second" });
        var store = new InMemoryPlayerItemStore(ids.Dequeue, createStateHash: _ => "forced-collision");
        long revision = store.Insert(Material() with { ExactState = new byte[] { 1 } }, 3, 0).Revision;
        store.Insert(Material() with { ExactState = new byte[] { 2 } }, 4, revision);

        PlayerItemSummary[] entries = store.Query(Query()).Entries;

        Assert.Equal(2, entries.Length);
        Assert.Equal(new long[] { 3, 4 }, entries.Select(entry => entry.Quantity).OrderBy(value => value));
    }

    [Fact]
    public void EmbeddedAssetReferencesParticipateInExactIdentity()
    {
        var ids = new Queue<string>(new[] { "first", "second" });
        var store = new InMemoryPlayerItemStore(ids.Dequeue);
        PlayerItemPrototype first = Material() with
        {
            EmbeddedReferences = new[]
            {
                new CanonicalCollectibleReference(PlayerItemAssetKind.Item, 1, "game:copper")
            }
        };
        PlayerItemPrototype second = first with
        {
            EmbeddedReferences = new[]
            {
                new CanonicalCollectibleReference(PlayerItemAssetKind.Item, 1, "game:iron")
            }
        };
        long revision = store.Insert(first, 1, 0).Revision;
        store.Insert(second, 1, revision);

        Assert.Equal(2, store.Query(Query()).Total);
    }

    [Fact]
    public void EmbeddedAssetReferenceOrderDoesNotChangeExactIdentity()
    {
        var store = Store("material");
        CanonicalCollectibleReference copper = new(
            PlayerItemAssetKind.Item,
            1,
            "game:copper");
        CanonicalCollectibleReference vessel = new(
            PlayerItemAssetKind.Block,
            2,
            "game:vessel");
        PlayerItemPrototype first = Material() with
        {
            EmbeddedReferences = new[] { copper, vessel }
        };
        PlayerItemPrototype reordered = first with
        {
            EmbeddedReferences = new[] { vessel, copper }
        };
        long revision = store.Insert(first, 2, 0).Revision;

        store.Insert(reordered, 3, revision);

        Assert.Single(store.Query(Query()).Entries);
        Assert.Equal(5, Entry(store, "material").Quantity);
    }

    [Fact]
    public void MalformedEmbeddedAssetReferencesAreRejectedWithoutThrowing()
    {
        var store = Store("material");
        PlayerItemPrototype malformed = Material() with
        {
            EmbeddedReferences = new CanonicalCollectibleReference[] { null! }
        };

        PlayerItemMutationResult result = store.Insert("insert", malformed, 1, 0);

        Assert.Equal(PlayerItemMutationRejection.InvalidRequest, result.Rejection);
        Assert.Equal(0, store.Revision);
    }

    [Fact]
    public void ReservedPrototypeSnapshotOwnsItsExactStateBytes()
    {
        var store = Store("material", reservationId: "reservation-1");
        PlayerItemPrototype material = Material() with
        {
            EmbeddedReferences = new[]
            {
                new CanonicalCollectibleReference(PlayerItemAssetKind.Item, 1, "game:nested")
            }
        };
        long revision = store.Insert(material, 10, 0).Revision;
        PlayerItemReservationResult reserved = store.Reserve(
            "reserve",
            new[] { new PlayerItemAmount("material", 2) },
            revision);

        PlayerItemReservationSnapshot first = store.Reservation(reserved.ReservationId);
        first.Entries[0].Prototype.ExactState[0] = 99;
        first.Entries[0].Prototype.EmbeddedReferences![0] =
            new CanonicalCollectibleReference(PlayerItemAssetKind.Item, 1, "changed:item");
        PlayerItemReservationSnapshot second = store.Reservation(reserved.ReservationId);

        Assert.Equal(1, second.Entries[0].Prototype.ExactState[0]);
        Assert.Equal("game:nested", second.Entries[0].Prototype.EmbeddedReferences![0].AssetCode);
    }

    [Fact]
    public async Task ConcurrentDistinctMutationsAtSameRevisionAllowOnlyOneCommit()
    {
        var ids = new Queue<string>(new[] { "a", "b" });
        var store = new InMemoryPlayerItemStore(ids.Dequeue);
        Task<PlayerItemMutationResult> first = Task.Run(() =>
            store.Insert("request-a", Material("game:a"), 1, 0));
        Task<PlayerItemMutationResult> second = Task.Run(() =>
            store.Insert("request-b", Material("game:b"), 1, 0));

        PlayerItemMutationResult[] results = await Task.WhenAll(first, second);

        Assert.Single(results.Where(result => result.Success));
        Assert.Single(results.Where(result => result.Rejection == PlayerItemMutationRejection.StaleRevision));
        Assert.Equal(1, store.Revision);
    }

    [Fact]
    public void DirectoryReturnsOneIsolatedStorePerPlayerUid()
    {
        var directory = new PlayerItemStoreDirectory();

        IPlayerItemStore playerA = directory.ForPlayer("player-a");
        IPlayerItemStore samePlayerA = directory.ForPlayer("PLAYER-A");
        IPlayerItemStore playerB = directory.ForPlayer("player-b");

        Assert.Same(playerA, samePlayerA);
        Assert.NotSame(playerA, playerB);
    }

    private static InMemoryPlayerItemStore Store(string entryId, string reservationId = "reservation") =>
        new(() => entryId, () => reservationId);

    private static PlayerItemSummary Entry(InMemoryPlayerItemStore store, string entryId)
    {
        foreach (PlayerItemLocation location in Enum.GetValues<PlayerItemLocation>())
        {
            PlayerItemSummary? entry = store.Query(Query(location)).Entries
                .FirstOrDefault(candidate => candidate.EntryId == entryId);
            if (entry != null) return entry;
        }
        throw new InvalidOperationException("Entry not found: " + entryId);
    }

    private static PlayerItemQuery Query(
        PlayerItemLocation location = PlayerItemLocation.Stored,
        PlayerItemSortOrder sort = PlayerItemSortOrder.Name) =>
        new("", "", 0, 100, location, sort);

    private static PlayerItemPrototype Material(
        string code = "game:copper",
        string name = "Copper") =>
        new(PlayerItemStorageKind.Aggregate, code, PlayerItemCategories.Materials, name, new byte[] { 1 });

    private static PlayerItemPrototype Gear(string code = "game:sword") =>
        new(PlayerItemStorageKind.Unique, code, PlayerItemCategories.Gear, "Sword", new byte[] { 2 });
}
