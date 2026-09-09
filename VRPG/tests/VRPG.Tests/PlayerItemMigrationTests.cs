using System;
using System.Collections.Generic;
using System.Linq;
using VRPG.Inventory;
using VRPG.Inventory.Migration;
using VRPG.Inventory.Persistence;
using Xunit;

namespace VRPG.Tests;

public sealed class PlayerItemMigrationTests
{
    [Fact]
    public void DryRunReportsAllSourcesWithoutChangingEitherOwner()
    {
        TestContext context = Context(Slot("hotbar", 0, 5), Slot("mouse", 0, 1));
        long revision = context.Store.Revision;

        PlayerItemMigrationPlan plan = context.Coordinator.DryRun(context.Source);

        Assert.True(plan.Report.Ready);
        Assert.Equal(2, plan.Report.StackCount);
        Assert.Equal(6, plan.Report.ItemCount);
        Assert.Equal(revision, context.Store.Revision);
        Assert.Equal(2, context.Source.Inspect().Slots.Length);
        Assert.Contains("Ready:", plan.Report.Summary());
    }

    [Fact]
    public void BlockingInspectionRefusesMigrationBeforeAnyWrite()
    {
        TestContext context = Context(Slot("hotbar", 0, 5));
        context.Source.BlockingReasons = new[] { "Close the open container and try again." };

        PlayerItemMigrationResult result = context.Coordinator.Commit(context.Source);

        Assert.False(result.Success);
        Assert.Contains("Close the open container", result.Message);
        Assert.Equal(0, context.Storage.WriteCount);
        Assert.Single(context.Source.Inspect().Slots);
    }

    [Fact]
    public void CommitPersistsPreparedTargetBeforeClearingLegacySlots()
    {
        TestContext context = Context(Slot("hotbar", 0, 5), Slot("backpack", 4, 7));
        context.Source.BeforeClear = () =>
        {
            PlayerItemArchiveLoadResult durable = new PlayerItemArchiveRepository(context.Storage).Load();
            Assert.Equal(PlayerItemMigrationStatus.Prepared, durable.Archive.Migrations[PlayerUid].Status);
            Assert.Equal(12, durable.Archive.Players[PlayerUid].Entries.Sum(entry => entry.Quantity));
        };

        PlayerItemMigrationResult result = context.Coordinator.Commit(context.Source);

        Assert.True(result.Success);
        Assert.Equal(LegacyInventoryState.Empty, context.Source.CompareTo(context.Original));
        PlayerItemMigrationJournalState journal = context.Service.Migration(PlayerUid)!;
        Assert.Equal(PlayerItemMigrationStatus.Committed, journal.Status);
        Assert.Equal(2, journal.SourceStackCount);
        Assert.Equal(12, journal.SourceItemCount);
    }

    [Fact]
    public void FailedPreparationWriteLeavesLegacyInventoryUntouchedButNeedsConfirmation()
    {
        TestContext context = Context(Slot("hotbar", 0, 5));
        context.Storage.FailOnWriteNumber = 1;

        PlayerItemMigrationResult result = context.Coordinator.Commit(context.Source);

        Assert.False(result.Success);
        Assert.True(result.RecoveryRequired);
        Assert.Equal(LegacyInventoryState.MatchesSnapshot, context.Source.CompareTo(context.Original));
        Assert.Empty(context.Service.Directory.ForPlayer(PlayerUid).Query(Query()).Entries);
    }

    [Fact]
    public void FailedFinalConfirmationResumesSafelyAfterRestart()
    {
        TestContext context = Context(Slot("hotbar", 0, 5));
        context.Storage.FailOnWriteNumber = 2;

        PlayerItemMigrationResult interrupted = context.Coordinator.Commit(context.Source);

        Assert.False(interrupted.Success);
        Assert.True(interrupted.RecoveryRequired);
        Assert.Equal(LegacyInventoryState.Empty, context.Source.CompareTo(context.Original));
        PlayerItemArchiveService restarted = Service(context.Storage);
        Assert.True(restarted.Load().Success);

        PlayerItemMigrationResult resumed = new PlayerItemMigrationCoordinator(restarted).Resume(context.Source);

        Assert.True(resumed.Success);
        Assert.Equal(PlayerItemMigrationStatus.Committed, restarted.Migration(PlayerUid)!.Status);
        Assert.Equal(5, restarted.Directory.ForPlayer(PlayerUid).Query(Query()).Entries.Single().Quantity);
    }

    [Fact]
    public void FailedClearRestoresOriginalSourceAndPriorStore()
    {
        TestContext context = Context(Slot("hotbar", 0, 5), Slot("backpack", 4, 7));
        context.Store.Insert("existing", Material("game:existing"), 3, 0);
        context.Source.FailClearAfterSlots = 1;

        PlayerItemMigrationResult result = context.Coordinator.Commit(context.Source);

        Assert.False(result.Success);
        Assert.False(result.RecoveryRequired);
        Assert.Equal(LegacyInventoryState.MatchesSnapshot, context.Source.CompareTo(context.Original));
        var restoredStore = Assert.IsType<InMemoryPlayerItemStore>(context.Service.Directory.ForPlayer(PlayerUid));
        Assert.Single(restoredStore.Query(Query()).Entries);
        Assert.Equal("game:existing", restoredStore.Query(Query()).Entries[0].AssetCode);
        Assert.Equal(PlayerItemMigrationStatus.Restored, context.Service.Migration(PlayerUid)!.Status);
    }

    [Fact]
    public void PreparedMigrationWithEmptySourceFinishesAfterRestart()
    {
        TestContext context = Context(Slot("hotbar", 0, 5));
        PlayerItemMigrationPlan plan = context.Coordinator.DryRun(context.Source);
        context.Service.StageMigration(PlayerUid, plan.StagedStore!, plan.Journal!);
        Assert.True(context.Service.SaveDirty().Success);
        Assert.True(context.Source.Clear(context.Original).Success);

        PlayerItemArchiveService restarted = Service(context.Storage);
        Assert.True(restarted.Load().Success);
        var coordinator = new PlayerItemMigrationCoordinator(restarted);
        PlayerItemMigrationResult result = coordinator.Resume(context.Source);

        Assert.True(result.Success);
        Assert.Equal(PlayerItemMigrationStatus.Committed, restarted.Migration(PlayerUid)!.Status);
    }

    [Fact]
    public void PartialSourceAfterRestartRollsBackInsteadOfCreatingMixedOwnership()
    {
        TestContext context = Context(Slot("hotbar", 0, 5), Slot("backpack", 4, 7));
        PlayerItemMigrationPlan plan = context.Coordinator.DryRun(context.Source);
        context.Service.StageMigration(PlayerUid, plan.StagedStore!, plan.Journal!);
        Assert.True(context.Service.SaveDirty().Success);
        context.Source.RemoveFirstSlot();

        PlayerItemArchiveService restarted = Service(context.Storage);
        Assert.True(restarted.Load().Success);
        PlayerItemMigrationResult result = new PlayerItemMigrationCoordinator(restarted).Resume(context.Source);

        Assert.False(result.Success);
        Assert.False(result.RecoveryRequired);
        Assert.Equal(LegacyInventoryState.MatchesSnapshot, context.Source.CompareTo(context.Original));
        Assert.Empty(restarted.Directory.ForPlayer(PlayerUid).Query(Query()).Entries);
    }

    [Fact]
    public void FailedRollbackRemainsRecoverableAndCanResume()
    {
        TestContext context = Context(Slot("hotbar", 0, 5), Slot("backpack", 4, 7));
        context.Source.FailClearAfterSlots = 1;
        context.Source.FailRestore = true;

        PlayerItemMigrationResult interrupted = context.Coordinator.Commit(context.Source);

        Assert.False(interrupted.Success);
        Assert.True(interrupted.RecoveryRequired);
        Assert.Equal(PlayerItemMigrationStatus.RestorePending, context.Service.Migration(PlayerUid)!.Status);
        context.Source.FailRestore = false;

        PlayerItemMigrationResult resumed = context.Coordinator.Resume(context.Source);

        Assert.True(resumed.Success);
        Assert.Equal(LegacyInventoryState.MatchesSnapshot, context.Source.CompareTo(context.Original));
        Assert.Equal(PlayerItemMigrationStatus.Restored, context.Service.Migration(PlayerUid)!.Status);
    }

    [Fact]
    public void CompletedMigrationIsIdempotent()
    {
        TestContext context = Context(Slot("hotbar", 0, 5));
        Assert.True(context.Coordinator.Commit(context.Source).Success);
        long generation = context.Service.Generation;

        PlayerItemMigrationResult replay = context.Coordinator.Commit(context.Source);

        Assert.True(replay.Success);
        Assert.Contains("already migrated", replay.Message);
        Assert.Equal(generation, context.Service.Generation);
        Assert.Equal(5, context.Service.Directory.ForPlayer(PlayerUid).Query(Query()).Entries.Single().Quantity);
    }

    [Fact]
    public void CapacityOverflowIsRecoverableAndVisibleInDryRun()
    {
        TestContext context = Context(
            Slot("hotbar", 0, 1, Gear("game:first")),
            Slot("hotbar", 1, 1, Gear("game:second")));
        context.Service.Directory.Set(
            PlayerUid,
            new InMemoryPlayerItemStore(capacity: new PlayerItemCapacity(1, 10, 100)));

        PlayerItemMigrationPlan plan = context.Coordinator.DryRun(context.Source);

        Assert.True(plan.Report.Ready);
        Assert.Equal(1, plan.Report.OverflowEntries);
        Assert.Contains("1 stack(s) will enter Recovery", plan.Report.Summary());
        Assert.Single(plan.StagedStore!.Recovery().Entries);
    }

    [Fact]
    public void LegacyEquipmentKeepsItsStableRole()
    {
        LegacyItemSlotState equipped = Slot("character", 12, 1, Gear("game:helmet"));
        equipped.Area = LegacyInventoryArea.Character;
        equipped.EquipmentRole = PlayerEquipmentRoles.Head;
        TestContext context = Context(equipped);

        PlayerItemMigrationPlan plan = context.Coordinator.DryRun(context.Source);

        Assert.True(plan.Report.Ready);
        EquippedPlayerItemRecord restored = plan.StagedStore!.Equipment().Entries.Single();
        Assert.Equal(PlayerEquipmentRoles.Head, restored.RoleCode);
    }

    [Fact]
    public void AdminRestoreReturnsBothOwnersToTheirPreMigrationState()
    {
        TestContext context = Context(Slot("hotbar", 0, 5));
        Assert.True(context.Coordinator.Commit(context.Source).Success);

        PlayerItemMigrationResult restored = context.Coordinator.RestorePrior(context.Source);

        Assert.True(restored.Success);
        Assert.Equal(LegacyInventoryState.MatchesSnapshot, context.Source.CompareTo(context.Original));
        Assert.Empty(context.Service.Directory.ForPlayer(PlayerUid).Query(Query()).Entries);
        Assert.Equal(PlayerItemMigrationStatus.Restored, context.Service.Migration(PlayerUid)!.Status);
    }

    private const string PlayerUid = "player";

    private static TestContext Context(params LegacyItemSlotState[] slots)
    {
        var storage = new MemoryStorage();
        PlayerItemArchiveService service = Service(storage);
        Assert.True(service.Load().Success);
        var store = Assert.IsType<InMemoryPlayerItemStore>(service.Directory.ForPlayer(PlayerUid));
        var source = new FakeLegacySource(PlayerUid, slots);
        return new TestContext(
            storage,
            service,
            store,
            source,
            new PlayerItemMigrationCoordinator(service),
            Clone(slots));
    }

    private static PlayerItemArchiveService Service(MemoryStorage storage) =>
        new(new PlayerItemArchiveRepository(storage));

    private static LegacyItemSlotState Slot(
        string inventory,
        int index,
        int quantity,
        PlayerItemPrototype? prototype = null) => new()
    {
        Area = inventory == "mouse" ? LegacyInventoryArea.Mouse : LegacyInventoryArea.Hotbar,
        InventoryClass = inventory,
        SlotIndex = index,
        Prototype = prototype ?? Material(),
        Quantity = quantity
    };

    private static PlayerItemPrototype Material(string code = "game:copper") => new(
        PlayerItemStorageKind.Aggregate,
        code,
        PlayerItemCategories.Materials,
        "Copper",
        new byte[] { 1 });

    private static PlayerItemPrototype Gear(string code) => new(
        PlayerItemStorageKind.Unique,
        code,
        PlayerItemCategories.Gear,
        "Gear",
        new byte[] { 2 });

    private static PlayerItemQuery Query() => new("", "", 0, 100);

    private static LegacyItemSlotState[] Clone(IEnumerable<LegacyItemSlotState> slots) => slots
        .Select(slot => new LegacyItemSlotState
        {
            Area = slot.Area,
            InventoryClass = slot.InventoryClass,
            SlotIndex = slot.SlotIndex,
            ParentSlotIndex = slot.ParentSlotIndex,
            Prototype = slot.Prototype with { ExactState = (byte[])slot.Prototype.ExactState.Clone() },
            Quantity = slot.Quantity,
            EquipmentRole = slot.EquipmentRole
        }).ToArray();

    private sealed record TestContext(
        MemoryStorage Storage,
        PlayerItemArchiveService Service,
        InMemoryPlayerItemStore Store,
        FakeLegacySource Source,
        PlayerItemMigrationCoordinator Coordinator,
        LegacyItemSlotState[] Original);

    private sealed class FakeLegacySource : ILegacyPlayerInventorySource
    {
        private readonly Dictionary<(string Inventory, int Slot), LegacyItemSlotState> slots;

        public FakeLegacySource(string playerUid, IEnumerable<LegacyItemSlotState> source)
        {
            PlayerUid = playerUid;
            slots = Clone(source).ToDictionary(slot => (slot.InventoryClass, slot.SlotIndex));
        }

        public string PlayerUid { get; }
        public string[] BlockingReasons { get; set; } = Array.Empty<string>();
        public int FailClearAfterSlots { get; set; } = -1;
        public bool FailRestore { get; set; }
        public Action? BeforeClear { get; set; }

        public LegacyInventoryInspection Inspect() => new(
            PlayerUid,
            Clone(slots.Values.OrderBy(slot => slot.InventoryClass).ThenBy(slot => slot.SlotIndex)),
            BlockingReasons);

        public LegacyInventoryState CompareTo(LegacyItemSlotState[] snapshot)
        {
            if (slots.Count == 0) return LegacyInventoryState.Empty;
            LegacyItemSlotState[] current = Inspect().Slots;
            return Equal(current, snapshot) ? LegacyInventoryState.MatchesSnapshot : LegacyInventoryState.Changed;
        }

        public LegacyInventoryMutationResult Clear(LegacyItemSlotState[] expected)
        {
            BeforeClear?.Invoke();
            BeforeClear = null;
            if (CompareTo(expected) != LegacyInventoryState.MatchesSnapshot)
                return LegacyInventoryMutationResult.Rejected("Source changed.");
            int cleared = 0;
            foreach (LegacyItemSlotState slot in expected)
            {
                slots.Remove((slot.InventoryClass, slot.SlotIndex));
                cleared++;
                if (FailClearAfterSlots == cleared)
                    return LegacyInventoryMutationResult.Rejected("Simulated clear failure.");
            }
            return LegacyInventoryMutationResult.Accepted();
        }

        public LegacyInventoryMutationResult Restore(LegacyItemSlotState[] snapshot)
        {
            if (FailRestore) return LegacyInventoryMutationResult.Rejected("Simulated restore failure.");
            slots.Clear();
            foreach (LegacyItemSlotState slot in Clone(snapshot))
                slots[(slot.InventoryClass, slot.SlotIndex)] = slot;
            FailClearAfterSlots = -1;
            return LegacyInventoryMutationResult.Accepted();
        }

        public void RemoveFirstSlot() => slots.Remove(slots.Keys.First());

        private static bool Equal(LegacyItemSlotState[] left, LegacyItemSlotState[] right)
        {
            LegacyItemSlotState[] a = left.OrderBy(slot => slot.InventoryClass).ThenBy(slot => slot.SlotIndex).ToArray();
            LegacyItemSlotState[] b = right.OrderBy(slot => slot.InventoryClass).ThenBy(slot => slot.SlotIndex).ToArray();
            return a.Length == b.Length && a.Zip(b).All(pair =>
                pair.First.InventoryClass == pair.Second.InventoryClass
                && pair.First.SlotIndex == pair.Second.SlotIndex
                && pair.First.Quantity == pair.Second.Quantity
                && pair.First.Prototype.AssetCode == pair.Second.Prototype.AssetCode
                && pair.First.Prototype.ExactState.SequenceEqual(pair.Second.Prototype.ExactState));
        }
    }

    private sealed class MemoryStorage : IPlayerItemArchiveStorage
    {
        private readonly Dictionary<string, byte[]> data = new(StringComparer.Ordinal);
        public int WriteCount { get; private set; }
        public int FailOnWriteNumber { get; set; } = -1;
        public byte[]? Read(string key) => data.TryGetValue(key, out byte[]? value) ? (byte[])value.Clone() : null;
        public void Write(string key, byte[] value)
        {
            if (WriteCount + 1 == FailOnWriteNumber)
            {
                WriteCount++;
                throw new InvalidOperationException("Simulated write failure.");
            }
            data[key] = (byte[])value.Clone();
            WriteCount++;
        }
    }
}
