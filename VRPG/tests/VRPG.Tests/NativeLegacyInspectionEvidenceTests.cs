using System;
using System.Collections.Generic;
using System.Linq;
using VRPG.Inventory;
using VRPG.Inventory.Migration;
using VRPG.Inventory.Persistence;
using Xunit;
using Xunit.Abstractions;

namespace VRPG.Tests;

[Collection("EngineItemBirth")]
public sealed class NativeLegacyInspectionEvidenceTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> UnavailableCases()
    {
        foreach (string reason in new[] { "missing-one", "missing-all", "paused", "external", "throwing" })
        foreach (bool empty in new[] { false, true })
            yield return new object[] { reason, empty };
    }

    public static IEnumerable<object[]> PendingCases()
    {
        foreach (object[] entry in UnavailableCases())
        foreach (PlayerItemMigrationStatus phase in new[] { PlayerItemMigrationStatus.Prepared,
            PlayerItemMigrationStatus.RestorePending, PlayerItemMigrationStatus.Committed })
            yield return new[] { entry[0], entry[1], phase };
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompleteNativeInspectionProvidesPositiveEvidence(bool empty)
    {
        var fixture = new NativeLegacyInventoryFixture();
        var snapshot = fixture.Source.Inspect().Slots;
        if (empty) Assert.True(fixture.Source.Clear(snapshot).Success);
        Assert.Empty(fixture.Source.Inspect().BlockingReasons);
        Assert.Equal(empty ? LegacyInventoryState.Empty : LegacyInventoryState.MatchesSnapshot,
            fixture.Source.CompareTo(snapshot));
    }

    [Theory]
    [MemberData(nameof(UnavailableCases))]
    public void IncompleteNativeInspectionCannotProveSourceContents(string reason, bool empty)
    {
        var fixture = new NativeLegacyInventoryFixture();
        var snapshot = fixture.Source.Inspect().Slots;
        if (empty) Assert.True(fixture.Source.Clear(snapshot).Success);
        fixture.MakeUnavailable(reason);
        var inspection = fixture.Source.Inspect();
        var comparison = fixture.Source.CompareTo(snapshot);
        output.WriteLine($"{reason}, empty={empty}: blockers=[{string.Join("; ", inspection.BlockingReasons)}], visible={inspection.Slots.Length}, comparison={comparison}");
        Assert.NotEmpty(inspection.BlockingReasons);
        Assert.Equal(LegacyInventoryState.Unavailable, comparison);
        Assert.DoesNotContain(inspection.BlockingReasons, text => text.Contains("private native inspection detail"));
        if (reason == "throwing") Assert.Contains(fixture.Diagnostics, text => text.Contains("private native inspection detail"));
        Assert.Equal(empty ? 0 : 10, fixture.Quantity);
    }

    [Theory]
    [MemberData(nameof(PendingCases))]
    public void UnknownSourceLeavesPhaseCustodyAndBothStoresUntouchedThenResumes(
        string reason, bool empty, PlayerItemMigrationStatus phase)
    {
        using var guards = new NativeMutationScope("recovery");
        var fixture = new NativeLegacyInventoryFixture();
        var storage = new Storage();
        var service = new PlayerItemArchiveService(new(storage));
        Assert.True(service.Load().Success);
        var coordinator = new PlayerItemMigrationCoordinator(service);
        var plan = coordinator.DryRun(fixture.Source);
        Assert.True(plan.Report.Ready, plan.Report.Summary());
        var snapshot = plan.Journal!.SourceSlots;
        plan.Journal.Status = phase;
        InMemoryPlayerItemStore store = plan.StagedStore!;
        if (phase == PlayerItemMigrationStatus.RestorePending)
        {
            Assert.True(InMemoryPlayerItemStore.TryRestore(plan.Journal.PriorStore, out var prior, out _));
            store = prior!;
        }
        service.StageMigration("player", store, plan.Journal);
        Assert.True(service.SaveDirty().Success);
        if (empty) Assert.True(fixture.Source.Clear(snapshot).Success);
        fixture.MakeUnavailable(reason);
        var source = new CountingSource(fixture.Source);
        var confirmationBefore = service.MigrationConfirmation("player");
        var custodyBefore = Custody(service);
        int writes = storage.Writes;
        var result = phase == PlayerItemMigrationStatus.Committed
            ? coordinator.RestorePrior(source) : coordinator.Resume(source);
        output.WriteLine($"{phase}, {reason}, empty={empty}: success={result.Success}, recovery={result.RecoveryRequired}, live={service.Migration("player")!.Status}, confirmed={service.MigrationConfirmation("player")}, clear={source.Clears}, restore={source.Restores}");
        Assert.False(result.Success);
        Assert.True(result.RecoveryRequired);
        Assert.Equal(phase, service.Migration("player")!.Status);
        Assert.Equal(confirmationBefore, service.MigrationConfirmation("player"));
        Assert.Equal(custodyBefore, Custody(service));
        Assert.Equal(0, source.Clears);
        Assert.Equal(0, source.Restores);
        Assert.Equal(writes, storage.Writes);
        Assert.Equal(empty ? 0 : 10, fixture.Quantity);
        Assert.Equal(phase == PlayerItemMigrationStatus.RestorePending ? 0 : 10, CanonicalQuantity(service));
        var durable = new PlayerItemArchiveRepository(storage).Load();
        Assert.True(durable.Success);
        Assert.Equal(phase, durable.Archive.Migrations["player"].Status);
        Assert.Equal(phase == PlayerItemMigrationStatus.RestorePending ? 0 : 10,
            durable.Archive.Players["player"].Entries.Sum(entry => entry.Quantity));

        fixture.MakeAvailable();
        Assert.Empty(fixture.Source.Inspect().BlockingReasons);
        result = phase == PlayerItemMigrationStatus.Committed
            ? coordinator.RestorePrior(source) : coordinator.Resume(source);
        Assert.True(result.Success, result.Message);
        bool committed = phase == PlayerItemMigrationStatus.Prepared;
        Assert.Equal(committed ? PlayerItemMigrationStatus.Committed : PlayerItemMigrationStatus.Restored,
            service.MigrationConfirmation("player").Phase);
        Assert.True(service.MigrationConfirmation("player").IsConfirmed);
        Assert.Equal(committed ? 10 : 0, CanonicalQuantity(service));
        Assert.Equal(committed ? 0 : 10, fixture.Quantity);
        Assert.Equal(committed && !empty ? 1 : 0, source.Clears);
        Assert.Equal(!committed && (empty || phase == PlayerItemMigrationStatus.Committed) ? 1 : 0, source.Restores);
        int clears = source.Clears, restores = source.Restores;
        Assert.True(coordinator.Resume(source).Success);
        Assert.Equal(clears, source.Clears);
        Assert.Equal(restores, source.Restores);
    }

    [Fact]
    public void RestoreCannotConfirmMatchingSlotsWhenItsFinalNativeInspectionIsPaused()
    {
        var fixture = new NativeLegacyInventoryFixture();
        var storage = new Storage();
        var service = new PlayerItemArchiveService(new(storage));
        Assert.True(service.Load().Success);
        var coordinator = new PlayerItemMigrationCoordinator(service);
        var source = new CountingSource(fixture.Source);
        Assert.True(coordinator.Commit(source).Success);
        fixture.Inventories["hotbar"].AfterModification = () => fixture.Inventories["hotbar"].Paused = true;

        var result = coordinator.RestorePrior(source);

        Assert.False(result.Success);
        Assert.True(result.RecoveryRequired);
        Assert.Equal(PlayerItemMigrationStatus.RestorePending, service.MigrationConfirmation("player").Phase);
        Assert.Equal(PlayerItemCustodyOwner.Recovery, Custody(service).Owner);
        Assert.Equal(10, fixture.Quantity);
        Assert.Equal(0, CanonicalQuantity(service));
        Assert.Equal(1, source.Restores);
        Assert.Equal(PlayerItemMigrationStatus.RestorePending,
            new PlayerItemArchiveRepository(storage).Load().Archive.Migrations["player"].Status);
        fixture.MakeAvailable();
        Assert.True(coordinator.Resume(source).Success);
        Assert.Equal(PlayerItemMigrationStatus.Restored, service.MigrationConfirmation("player").Phase);
        Assert.Equal(1, source.Restores);
        Assert.Equal(10, fixture.Quantity);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void FailedFinalWriteRetainsKnownMutationWithoutDuplicateNativeClearOrRestore(
        bool restore, bool reload, bool writeThenThrow)
    {
        var fixture = new NativeLegacyInventoryFixture();
        var storage = new Storage();
        var service = new PlayerItemArchiveService(new(storage));
        Assert.True(service.Load().Success);
        var coordinator = new PlayerItemMigrationCoordinator(service);
        var source = new CountingSource(fixture.Source);
        if (restore) Assert.True(coordinator.Commit(source).Success);
        storage.FailOnWrite = storage.Writes + 2;
        storage.WriteThenThrow = writeThenThrow;
        var result = restore ? coordinator.RestorePrior(source) : coordinator.Commit(source);
        Assert.False(result.Success);
        Assert.True(result.RecoveryRequired);
        Assert.False(service.MigrationConfirmation("player").IsConfirmed);
        fixture.MakeUnavailable("paused");
        if (reload) Assert.True(service.Load().Success);
        storage.FailOnWrite = -1;
        result = coordinator.Resume(source);
        if (reload && !writeThenThrow)
        {
            // Reload retained only the preceding journal phase, so source evidence is needed.
            Assert.False(result.Success);
            Assert.Equal(restore ? PlayerItemMigrationStatus.RestorePending : PlayerItemMigrationStatus.Prepared,
                service.MigrationConfirmation("player").Phase);
            fixture.MakeAvailable();
            result = coordinator.Resume(source);
        }
        Assert.True(result.Success, result.Message);
        Assert.True(service.MigrationConfirmation("player").IsConfirmed);
        Assert.Equal(restore ? PlayerItemMigrationStatus.Restored : PlayerItemMigrationStatus.Committed,
            service.MigrationConfirmation("player").Phase);
        Assert.Equal(restore ? 10 : 0, fixture.Quantity);
        Assert.Equal(restore ? 0 : 10, CanonicalQuantity(service));
        Assert.Equal(1, source.Clears);
        Assert.Equal(restore ? 1 : 0, source.Restores);
    }

    [Fact]
    public void AvailablePartialNativeSourceStillRollsBackAndPreservesEveryItem()
    {
        var fixture = new NativeLegacyInventoryFixture();
        var service = new PlayerItemArchiveService(new(new Storage()));
        Assert.True(service.Load().Success);
        var coordinator = new PlayerItemMigrationCoordinator(service);
        var plan = coordinator.DryRun(fixture.Source);
        Assert.True(plan.Report.Ready);
        service.StageMigration("player", plan.StagedStore!, plan.Journal!);
        Assert.True(service.SaveDirty().Success);
        fixture.Inventories["hotbar"][0].Itemstack = null;
        Assert.Equal(LegacyInventoryState.Changed, fixture.Source.CompareTo(plan.Journal!.SourceSlots));
        var source = new CountingSource(fixture.Source);
        var result = coordinator.Resume(source);
        Assert.False(result.Success);
        Assert.False(result.RecoveryRequired);
        Assert.Equal(PlayerItemMigrationStatus.Restored, service.MigrationConfirmation("player").Phase);
        Assert.Equal(10, fixture.Quantity);
        Assert.Equal(0, CanonicalQuantity(service));
        Assert.Equal(0, source.Clears);
        Assert.Equal(1, source.Restores);
    }

    private static long CanonicalQuantity(PlayerItemArchiveService service) =>
        ((InMemoryPlayerItemStore)service.Directory.ForPlayer("player")).Capture().State.Entries.Sum(entry => entry.Quantity);

    private static PlayerItemCustodySnapshot Custody(PlayerItemArchiveService service)
    {
        var confirmation = service.MigrationConfirmation("player");
        return new PlayerItemCustodyPolicy().Observe("player", confirmation.IsConfirmed, confirmation.Phase, true, true);
    }

    private sealed class CountingSource(VintageStoryLegacyInventorySource actual) : ILegacyPlayerInventorySource
    {
        public int Clears, Restores;
        public string PlayerUid => actual.PlayerUid;
        public LegacyInventoryInspection Inspect() => actual.Inspect();
        public LegacyInventoryState CompareTo(LegacyItemSlotState[] snapshot) => actual.CompareTo(snapshot);
        public LegacyInventoryMutationResult Clear(LegacyItemSlotState[] expected) { Clears++; return actual.Clear(expected); }
        public LegacyInventoryMutationResult Restore(LegacyItemSlotState[] snapshot) { Restores++; return actual.Restore(snapshot); }
    }

    private sealed class Storage : IPlayerItemArchiveStorage
    {
        private readonly Dictionary<string, byte[]> values = new();
        public int Writes;
        public int FailOnWrite = -1;
        public bool WriteThenThrow;
        public byte[]? Read(string key) => values.GetValueOrDefault(key);
        public void Write(string key, byte[] data)
        {
            Writes++;
            if (Writes == FailOnWrite && !WriteThenThrow) throw new InvalidOperationException("write failed");
            values[key] = (byte[])data.Clone();
            if (Writes == FailOnWrite) throw new InvalidOperationException("write acknowledged ambiguously");
        }
    }
}
