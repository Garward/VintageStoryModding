using System;
using System.Collections.Generic;
using System.Linq;
using VRPG.Inventory.Persistence;

namespace VRPG.Inventory.Migration;

/// <summary>Stages target custody durably before clearing any legacy slot.</summary>
public sealed class PlayerItemMigrationCoordinator
{
    private readonly PlayerItemArchiveService persistence;

    public PlayerItemMigrationCoordinator(PlayerItemArchiveService persistence)
    {
        this.persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
    }

    public PlayerItemMigrationPlan DryRun(ILegacyPlayerInventorySource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        PlayerItemMigrationJournalState? existing = persistence.Migration(source.PlayerUid);
        if (existing?.MigrationVersion >= PlayerItemMigrationSchema.CurrentVersion
            && existing.Status == PlayerItemMigrationStatus.Committed)
        {
            return new PlayerItemMigrationPlan(
                new PlayerItemMigrationReport(true, true, 0, 0, 0, 0, 0, Array.Empty<string>()),
                null,
                existing);
        }
        if (existing != null && existing.Status is PlayerItemMigrationStatus.Prepared
            or PlayerItemMigrationStatus.RestorePending)
        {
            return Blocked("A previous migration needs recovery before another can start.");
        }

        LegacyInventoryInspection inspection = source.Inspect();
        if (!string.Equals(inspection.PlayerUid, source.PlayerUid, StringComparison.OrdinalIgnoreCase))
            return Blocked("The inventory owner could not be verified.");
        if (inspection.BlockingReasons.Length != 0)
            return Blocked(inspection.BlockingReasons);

        var current = persistence.Directory.ForPlayer(source.PlayerUid) as InMemoryPlayerItemStore;
        if (current == null) return Blocked("The destination inventory is unavailable.");
        PlayerItemStoreCapture prior = current.Capture();
        if (!InMemoryPlayerItemStore.TryRestore(prior.State, out InMemoryPlayerItemStore? staged, out string error))
            return Blocked("The destination inventory could not be staged: " + error);

        int overflow = 0;
        int unresolved = 0;
        var blocking = new List<string>();
        long itemCount = 0;
        foreach ((LegacyItemSlotState slot, int index) in inspection.Slots.Select((slot, index) => (slot, index)))
        {
            if (!TryValidateSlot(slot, out error))
            {
                blocking.Add($"Slot {slot.InventoryClass} {slot.SlotIndex} cannot be preserved: {error}");
                continue;
            }
            try
            {
                itemCount = checked(itemCount + slot.Quantity);
            }
            catch (OverflowException)
            {
                return Blocked("The source inventory quantity is too large to verify.");
            }
            string request = $"migration:{PlayerItemMigrationSchema.CurrentVersion}:{index}";
            bool enteredRecovery = false;
            PlayerItemMutationResult inserted = staged!.Insert(request, slot.Prototype, slot.Quantity, staged.Revision);
            if (!inserted.Success && inserted.Rejection == PlayerItemMutationRejection.CapacityExceeded)
            {
                inserted = staged.ImportRecovery(
                    request + ":overflow",
                    slot.Prototype,
                    slot.Quantity,
                    PlayerItemLocation.Overflow,
                    PlayerItemRecoveryReason.CapacityExceeded,
                    slot.InventoryClass,
                    staged.Revision);
                if (inserted.Success)
                {
                    overflow++;
                    enteredRecovery = true;
                }
            }
            if (!inserted.Success)
            {
                blocking.Add($"Slot {slot.InventoryClass} {slot.SlotIndex} was rejected by destination storage.");
                continue;
            }
            if (slot.EquipmentRole.Length != 0 && !enteredRecovery)
            {
                if (slot.Prototype.Kind == PlayerItemStorageKind.Unique)
                {
                    PlayerItemOperationResult equipped = staged.Equip(
                        request + ":equip",
                        inserted.EntryId,
                        slot.EquipmentRole,
                        staged.Revision);
                    if (!equipped.Success)
                        blocking.Add($"Equipment from {slot.InventoryClass} {slot.SlotIndex} could not be restored.");
                }
            }
        }

        if (blocking.Count != 0) return Blocked(blocking.ToArray());
        PlayerItemStoreState stagedState = staged!.Capture().State;
        if (!ImportedDeltaMatches(prior.State, stagedState, inspection.Slots))
            return Blocked("The staged inventory did not match the source exactly.");
        var journal = new PlayerItemMigrationJournalState
        {
            MigrationVersion = PlayerItemMigrationSchema.CurrentVersion,
            Status = PlayerItemMigrationStatus.Prepared,
            PriorStore = prior.State,
            SourceSlots = CloneSlots(inspection.Slots),
            SourceStackCount = inspection.Slots.Length,
            SourceItemCount = itemCount,
            ImportedEntryCount = stagedState.Entries.Length - prior.State.Entries.Length,
            OverflowEntryCount = overflow,
            UnresolvedEntryCount = unresolved
        };
        var report = new PlayerItemMigrationReport(
            true,
            false,
            inspection.Slots.Length,
            itemCount,
            stagedState.Entries.Count(entry => entry.Location is PlayerItemLocation.Stored or PlayerItemLocation.Equipped),
            overflow,
            unresolved,
            Array.Empty<string>());
        return new PlayerItemMigrationPlan(report, staged, journal);
    }

    public PlayerItemMigrationResult Commit(ILegacyPlayerInventorySource source)
    {
        PlayerItemMigrationPlan plan = DryRun(source);
        if (plan.Report.AlreadyCompleted)
            return persistence.MigrationConfirmation(source.PlayerUid).IsConfirmed
                ? new PlayerItemMigrationResult(true, false, plan.Report.Summary()) : FinishPrepared(source.PlayerUid);
        if (!plan.Report.Ready || plan.StagedStore == null || plan.Journal == null)
            return new PlayerItemMigrationResult(false, false, plan.Report.Summary());

        PlayerItemMigrationJournalState? priorJournal = persistence.Migration(source.PlayerUid);
        InMemoryPlayerItemStore priorStore = RestoreStore(plan.Journal.PriorStore);
        persistence.StageMigration(source.PlayerUid, plan.StagedStore, plan.Journal);
        PlayerItemArchiveSaveResult prepared = persistence.SaveDirty();
        if (!prepared.Success)
        {
            persistence.AbortStagedMigration(source.PlayerUid, priorStore, priorJournal);
            bool recovery = !persistence.MigrationConfirmation(source.PlayerUid).IsConfirmed;
            return new PlayerItemMigrationResult(false, recovery, recovery
                ? "Migration stopped before any items were moved. Run the resume command."
                : "Migration stopped before any items were moved.");
        }

        LegacyInventoryMutationResult cleared = source.Clear(plan.Journal.SourceSlots);
        if (!cleared.Success)
            return RollBack(source, plan.Journal, "The original inventory changed while migration was running.");

        persistence.SetMigrationStatus(source.PlayerUid, PlayerItemMigrationStatus.Committed);
        PlayerItemArchiveSaveResult committed = persistence.SaveDirty();
        if (!committed.Success)
        {
            return new PlayerItemMigrationResult(
                false,
                true,
                "Items moved, but final confirmation could not be saved. Recovery will resume next time.");
        }
        return new PlayerItemMigrationResult(true, false, "Inventory migration complete.");
    }

    public PlayerItemMigrationResult Resume(ILegacyPlayerInventorySource source)
    {
        PlayerItemMigrationJournalState? journal = persistence.Migration(source.PlayerUid);
        if (journal == null)
        {
            var confirmation = persistence.MigrationConfirmation(source.PlayerUid);
            // Abort restored the prior no-journal state without touching the source, but a
            // failed prepare Write may still be on disk. Confirm that rollback, not a new clear.
            if (persistence.IsLoaded && !confirmation.IsConfirmed && confirmation.Phase == null)
                return persistence.SaveDirty().Success
                    ? new PlayerItemMigrationResult(true, false, "Recovery complete. No items were moved.")
                    : new PlayerItemMigrationResult(false, true, "Recovery confirmation could not be saved. Try again.");
            return new PlayerItemMigrationResult(false, false, "No migration recovery is pending.");
        }
        if (journal.Status == PlayerItemMigrationStatus.Committed)
            return persistence.MigrationConfirmation(source.PlayerUid).IsConfirmed
                ? new PlayerItemMigrationResult(true, false, "This player's inventory is already migrated.") : FinishPrepared(source.PlayerUid);
        if (journal.Status == PlayerItemMigrationStatus.Restored)
            return persistence.MigrationConfirmation(source.PlayerUid).IsConfirmed
                ? new PlayerItemMigrationResult(true, false, "The original inventory has already been restored.") : ConfirmRestored(source.PlayerUid);
        if (journal.Status == PlayerItemMigrationStatus.RestorePending)
            return RestorePrior(source, journal);

        return source.CompareTo(journal.SourceSlots) switch
        {
            LegacyInventoryState.Empty => FinishPrepared(source.PlayerUid),
            LegacyInventoryState.MatchesSnapshot => FinishClear(source, journal),
            _ => RollBack(source, journal, "A partial migration was detected.")
        };
    }

    public PlayerItemMigrationResult RestorePrior(ILegacyPlayerInventorySource source)
    {
        PlayerItemMigrationJournalState? journal = persistence.Migration(source.PlayerUid);
        return journal == null
            ? new PlayerItemMigrationResult(false, false, "No migration snapshot is available for this player.")
            : RestorePrior(source, journal);
    }

    private PlayerItemMigrationResult RestorePrior(
        ILegacyPlayerInventorySource source,
        PlayerItemMigrationJournalState journal)
    {
        if (!InMemoryPlayerItemStore.TryRestore(journal.PriorStore, out InMemoryPlayerItemStore? prior, out _))
            return new PlayerItemMigrationResult(false, true, "The recovery snapshot could not be read. Check the server log.");
        var current = persistence.Directory.ForPlayer(source.PlayerUid) as InMemoryPlayerItemStore;
        if (current == null)
            return new PlayerItemMigrationResult(false, true, "The current inventory could not be read. Check the server log.");
        var confirmation = persistence.MigrationConfirmation(source.PlayerUid);
        if ((journal.Status == PlayerItemMigrationStatus.Restored && !confirmation.IsConfirmed)
            || (journal.Status == PlayerItemMigrationStatus.RestorePending && confirmation.IsConfirmed
                && confirmation.Phase == PlayerItemMigrationStatus.RestorePending
                && source.CompareTo(journal.SourceSlots) == LegacyInventoryState.MatchesSnapshot))
            return ConfirmRestored(source.PlayerUid);
        PlayerItemMigrationStatus previousStatus = journal.Status;
        journal.Status = PlayerItemMigrationStatus.RestorePending;
        persistence.StageMigration(source.PlayerUid, prior!, journal);
        if (!persistence.SaveDirty().Success)
        {
            journal.Status = previousStatus;
            persistence.AbortStagedMigration(source.PlayerUid, current, journal);
            return new PlayerItemMigrationResult(false, true, "Recovery stopped before the original inventory was changed.");
        }
        LegacyInventoryMutationResult restored = source.Restore(journal.SourceSlots);
        if (!restored.Success)
            return new PlayerItemMigrationResult(false, true, "Recovery is incomplete. Free the affected inventory slots and try again.");
        return ConfirmRestored(source.PlayerUid);
    }

    private PlayerItemMigrationResult ConfirmRestored(string playerUid)
    {
        persistence.SetMigrationStatus(playerUid, PlayerItemMigrationStatus.Restored);
        return persistence.SaveDirty().Success
            ? new PlayerItemMigrationResult(true, false, "The original inventory has been restored.")
            : new PlayerItemMigrationResult(false, true, "Items were restored, but recovery confirmation was not saved. Try again.");
    }

    private PlayerItemMigrationResult FinishPrepared(string playerUid)
    {
        persistence.SetMigrationStatus(playerUid, PlayerItemMigrationStatus.Committed);
        return persistence.SaveDirty().Success
            ? new PlayerItemMigrationResult(true, false, "Inventory migration complete.")
            : new PlayerItemMigrationResult(false, true, "Final confirmation could not be saved. Recovery remains pending.");
    }

    private PlayerItemMigrationResult FinishClear(
        ILegacyPlayerInventorySource source,
        PlayerItemMigrationJournalState journal)
    {
        LegacyInventoryMutationResult cleared = source.Clear(journal.SourceSlots);
        return cleared.Success
            ? FinishPrepared(source.PlayerUid)
            : RollBack(source, journal, "Migration could not finish clearing the original inventory.");
    }

    private PlayerItemMigrationResult RollBack(
        ILegacyPlayerInventorySource source,
        PlayerItemMigrationJournalState journal,
        string reason)
    {
        PlayerItemMigrationResult restored = RestorePrior(source, journal);
        return restored.Success
            ? new PlayerItemMigrationResult(false, false, reason + " The original inventory was restored.")
            : new PlayerItemMigrationResult(false, true, reason + " Recovery is required.");
    }

    private static PlayerItemMigrationPlan Blocked(params string[] reasons) => new(
        new PlayerItemMigrationReport(false, false, 0, 0, 0, 0, 0, reasons),
        null,
        null);

    private static bool TryValidateSlot(LegacyItemSlotState? slot, out string error)
    {
        error = "";
        if (slot == null || string.IsNullOrWhiteSpace(slot.InventoryClass)
            || slot.SlotIndex < 0 || slot.Quantity <= 0 || slot.Prototype == null)
        {
            error = "invalid source data";
            return false;
        }
        return true;
    }

    private static InMemoryPlayerItemStore RestoreStore(PlayerItemStoreState state)
    {
        if (!InMemoryPlayerItemStore.TryRestore(state, out InMemoryPlayerItemStore? store, out string error))
            throw new InvalidOperationException("A validated migration store could not be restored: " + error);
        return store!;
    }

    private static bool ImportedDeltaMatches(
        PlayerItemStoreState prior,
        PlayerItemStoreState staged,
        LegacyItemSlotState[] source)
    {
        try
        {
            Dictionary<string, long> expected = Totals(source.Select(slot =>
                (slot.Prototype, (long)slot.Quantity)));
            Dictionary<string, long> before = Totals(prior.Entries.Select(entry =>
                (entry.Prototype, entry.Quantity)));
            Dictionary<string, long> after = Totals(staged.Entries.Select(entry =>
                (entry.Prototype, entry.Quantity)));
            var keys = new HashSet<string>(before.Keys, StringComparer.Ordinal);
            keys.UnionWith(after.Keys);
            foreach (string key in keys)
            {
                long delta = checked(
                    (after.TryGetValue(key, out long afterQuantity) ? afterQuantity : 0)
                    - (before.TryGetValue(key, out long beforeQuantity) ? beforeQuantity : 0));
                long expectedQuantity = expected.TryGetValue(key, out long quantity) ? quantity : 0;
                if (delta != expectedQuantity) return false;
            }
            return expected.Keys.All(keys.Contains);
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static Dictionary<string, long> Totals(
        IEnumerable<(PlayerItemPrototype Prototype, long Quantity)> entries)
    {
        var totals = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach ((PlayerItemPrototype prototype, long quantity) in entries)
        {
            string key = Identity(prototype);
            totals[key] = checked((totals.TryGetValue(key, out long current) ? current : 0) + quantity);
        }
        return totals;
    }

    private static string Identity(PlayerItemPrototype prototype)
    {
        string references = string.Join(',',
            (prototype.EmbeddedReferences ?? Array.Empty<CanonicalCollectibleReference>())
                .Select(reference => $"{reference.AssetKind}:{reference.SourceRegistryId}:{reference.AssetCode.ToLowerInvariant()}")
                .OrderBy(value => value, StringComparer.Ordinal));
        return string.Join('|',
            prototype.Kind,
            prototype.AssetKind,
            prototype.AssetCode.ToLowerInvariant(),
            prototype.Category.ToLowerInvariant(),
            Convert.ToBase64String(prototype.ExactState),
            references);
    }

    private static LegacyItemSlotState[] CloneSlots(LegacyItemSlotState[] slots) => slots
        .Select(slot => new LegacyItemSlotState
        {
            Area = slot.Area,
            InventoryClass = slot.InventoryClass,
            SlotIndex = slot.SlotIndex,
            ParentSlotIndex = slot.ParentSlotIndex,
            Prototype = slot.Prototype with
            {
                ExactState = (byte[])slot.Prototype.ExactState.Clone(),
                EmbeddedReferences = slot.Prototype.EmbeddedReferences == null
                    ? null
                    : (CanonicalCollectibleReference[])slot.Prototype.EmbeddedReferences.Clone()
            },
            Quantity = slot.Quantity,
            EquipmentRole = slot.EquipmentRole
        })
        .ToArray();
}
