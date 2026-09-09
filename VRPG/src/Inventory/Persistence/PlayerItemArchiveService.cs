using System;
using System.Collections.Generic;
using System.Linq;
using VRPG.Inventory.Migration;
using Newtonsoft.Json.Linq;
using VRPG.Inventory.Bridges;

namespace VRPG.Inventory.Persistence;

/// <summary>Loads all stores before use and snapshots only stores whose durable state changed.</summary>
public sealed class PlayerItemArchiveService
{
    private readonly object sync = new();
    private readonly PlayerItemArchiveRepository repository;
    private PlayerItemArchive archive = new();
    private readonly HashSet<string> forcedPlayers = new(StringComparer.OrdinalIgnoreCase);
    private bool metadataDirty;
    private Dictionary<string, PlayerItemMigrationStatus> confirmedMigrationPhases = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> pendingMigrationChanges = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> uncertainMigrationWrites = new(StringComparer.OrdinalIgnoreCase);

    public PlayerItemArchiveService(
        PlayerItemArchiveRepository repository,
        PlayerItemStoreDirectory? directory = null)
    {
        this.repository = repository ?? throw new ArgumentNullException(nameof(repository));
        Directory = directory ?? new PlayerItemStoreDirectory();
    }

    public PlayerItemStoreDirectory Directory { get; }
    public bool IsLoaded { get; private set; }
    public bool IsCrashDurable => repository.IsCrashDurable;
    internal object TransactionSync => sync;
    public long Generation { get; private set; }

    public PlayerItemArchiveLoadResult Load()
    {
        lock (sync)
        {
            IsLoaded = false;
            PlayerItemArchiveLoadResult loaded = repository.Load();
            if (!loaded.Success) return loaded;
            if (repository.IsCrashDurable && loaded.RecoveredFromOlderGeneration)
                return PlayerItemArchiveLoadResult.Failed("A newer durable archive is corrupt. External effects require explicit reconciliation; older custody cannot be activated.");

            var stores = new List<KeyValuePair<string, IPlayerItemStore>>(loaded.Archive.Players.Count);
            foreach ((string playerUid, PlayerItemStoreState state) in loaded.Archive.Players)
            {
                if (!InMemoryPlayerItemStore.TryRestore(state, out InMemoryPlayerItemStore? store, out string error))
                {
                    return PlayerItemArchiveLoadResult.Failed(
                        $"Player '{playerUid}' could not be restored: {error}");
                }
                stores.Add(new KeyValuePair<string, IPlayerItemStore>(playerUid, store!));
            }
            foreach ((string playerUid, PlayerItemMigrationJournalState journal) in loaded.Archive.Migrations)
            {
                if (!ValidateMigrationJournal(journal, out string error))
                {
                    return PlayerItemArchiveLoadResult.Failed(
                        $"Player '{playerUid}' has an invalid migration journal: {error}");
                }
            }
            foreach (var receipt in loaded.Archive.Bridges.Values)
            {
                loaded.Archive.Players.TryGetValue(receipt.Request.PlayerUid, out var store);
                if (!PlayerItemBridgeJournal.MatchesCustody(receipt, store))
                    return PlayerItemArchiveLoadResult.Failed("A bridge receipt does not match its canonical reservation or committed result.");
            }

            Directory.ReplaceAll(stores);
            archive = loaded.Archive;
            Generation = loaded.Generation;
            forcedPlayers.Clear();
            metadataDirty = false;
            ConfirmMigrationPhases(archive);
            IsLoaded = true;
            return loaded;
        }
    }

    public PlayerItemMigrationJournalState? Migration(string playerUid)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(playerUid);
        lock (sync)
        {
            return archive.Migrations.TryGetValue(playerUid.Trim(), out PlayerItemMigrationJournalState? state)
                ? state
                : null;
        }
    }

    /// <summary>Detached phase evidence, confirmed only by a validated load or successful save.</summary>
    public PlayerItemMigrationConfirmation MigrationConfirmation(string playerUid)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(playerUid);
        lock (sync)
        {
            string uid = playerUid.Trim();
            PlayerItemMigrationStatus? phase = confirmedMigrationPhases.TryGetValue(uid, out var confirmed) ? confirmed : null;
            bool matches = (archive.Migrations.TryGetValue(uid, out var live) ? live.Status : (PlayerItemMigrationStatus?)null) == phase;
            return new(IsLoaded && matches && !pendingMigrationChanges.Contains(uid) && !uncertainMigrationWrites.Contains(uid), phase);
        }
    }

    public void StageMigration(
        string playerUid,
        InMemoryPlayerItemStore stagedStore,
        PlayerItemMigrationJournalState journal)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(playerUid);
        ArgumentNullException.ThrowIfNull(stagedStore);
        ArgumentNullException.ThrowIfNull(journal);
        lock (sync)
        {
            if (!IsLoaded) throw new InvalidOperationException("Player-item persistence has not loaded.");
            string normalized = playerUid.Trim();
            Directory.Set(normalized, stagedStore);
            archive.Migrations[normalized] = journal;
            forcedPlayers.Add(normalized);
            pendingMigrationChanges.Add(normalized);
            metadataDirty = true;
        }
    }

    public void SetMigrationStatus(string playerUid, PlayerItemMigrationStatus status)
    {
        lock (sync)
        {
            if (!archive.Migrations.TryGetValue(playerUid.Trim(), out PlayerItemMigrationJournalState? journal))
                throw new InvalidOperationException("No migration journal exists for this player.");
            journal.Status = status;
            pendingMigrationChanges.Add(playerUid.Trim());
            metadataDirty = true;
        }
    }

    public void AbortStagedMigration(
        string playerUid,
        InMemoryPlayerItemStore fallbackStore,
        PlayerItemMigrationJournalState? priorJournal)
    {
        lock (sync)
        {
            string normalized = playerUid.Trim();
            Directory.Set(normalized, fallbackStore);
            if (priorJournal == null) archive.Migrations.Remove(normalized);
            else archive.Migrations[normalized] = priorJournal;
            forcedPlayers.Add(normalized);
            metadataDirty = priorJournal != null;
            // Only a known pre-storage failure can restore prior permission without another
            // authoritative save/load. A failed Write may already contain the staged owner.
            PlayerItemMigrationStatus? confirmed = confirmedMigrationPhases.TryGetValue(normalized, out var phase) ? phase : null;
            if (!uncertainMigrationWrites.Contains(normalized) && priorJournal?.Status == confirmed)
                pendingMigrationChanges.Remove(normalized);
        }
    }

    public PlayerItemArchiveSaveResult SaveDirty()
    {
        lock (sync)
        {
            if (!IsLoaded)
                return PlayerItemArchiveSaveResult.Failed(Generation, "Player-item persistence has not loaded.");

            var captures = new List<(InMemoryPlayerItemStore Store, PlayerItemStoreCapture Capture)>();
            var nextPlayers = new Dictionary<string, PlayerItemStoreState>(
                archive.Players,
                StringComparer.OrdinalIgnoreCase);
            foreach ((string playerUid, IPlayerItemStore itemStore) in Directory.Snapshot())
            {
                if (itemStore is not InMemoryPlayerItemStore store)
                {
                    return PlayerItemArchiveSaveResult.Failed(
                        Generation,
                        $"Player '{playerUid}' uses a store that cannot be persisted.");
                }
                if (!store.IsDirty && !forcedPlayers.Contains(playerUid) && nextPlayers.ContainsKey(playerUid)) continue;

                PlayerItemStoreCapture capture = store.Capture();
                if (nextPlayers.TryGetValue(playerUid, out PlayerItemStoreState? prior))
                    PreserveExtensions(prior, capture.State);
                nextPlayers[playerUid] = capture.State;
                captures.Add((store, capture));
            }

            string[] changedMigrations = pendingMigrationChanges.Concat(uncertainMigrationWrites)
                .Concat(archive.Migrations.Keys.Concat(confirmedMigrationPhases.Keys).Where(uid =>
                    (archive.Migrations.TryGetValue(uid, out var live) ? live.Status : (PlayerItemMigrationStatus?)null)
                    != (confirmedMigrationPhases.TryGetValue(uid, out var confirmed) ? confirmed : (PlayerItemMigrationStatus?)null)))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (captures.Count == 0 && !metadataDirty && changedMigrations.Length == 0 && Generation > 0)
                return new PlayerItemArchiveSaveResult(true, Generation, "");

            PlayerItemArchive nextArchive;
            try
            {
                nextArchive = new PlayerItemArchive
                {
                    SchemaVersion = PlayerItemPersistenceSchema.CurrentVersion,
                    Players = nextPlayers,
                    // Journal objects are mutable and externally observable. Use the archive's
                    // serialization contract to detach the candidate without narrowing its data.
                    Migrations = archive.Migrations.ToDictionary(pair => pair.Key,
                        pair => repository.CloneMigrationJournal(pair.Value),
                        StringComparer.OrdinalIgnoreCase),
                    Bridges = new Dictionary<string, PlayerItemBridgeReceipt>(archive.Bridges, StringComparer.Ordinal),
                    ExtensionData = CloneExtensions(archive.ExtensionData)
                };
            }
            catch (Exception exception)
            {
                // Preparation has not reached storage. Keep all pending/uncertain evidence
                // and dirty stores intact so this failure cannot confirm a live phase.
                return PlayerItemArchiveSaveResult.Failed(Generation,
                    $"The inventory archive snapshot could not be prepared: {exception.Message}");
            }
            PlayerItemArchiveSaveResult saved = repository.Save(nextArchive);
            if (!saved.Success)
            {
                if (saved.StorageWriteAttempted) uncertainMigrationWrites.UnionWith(changedMigrations);
                return saved;
            }

            archive = nextArchive;
            Generation = saved.Generation;
            foreach ((InMemoryPlayerItemStore store, PlayerItemStoreCapture capture) in captures)
                store.MarkPersisted(capture.PersistenceStamp);
            forcedPlayers.Clear();
            metadataDirty = false;
            ConfirmMigrationPhases(nextArchive);
            return saved;
        }
    }

    private void ConfirmMigrationPhases(PlayerItemArchive confirmed)
    {
        confirmedMigrationPhases = confirmed.Migrations.ToDictionary(pair => pair.Key, pair => pair.Value.Status,
            StringComparer.OrdinalIgnoreCase);
        pendingMigrationChanges.Clear();
        uncertainMigrationWrites.Clear();
    }

    internal PlayerItemBridgeReceipt[] BridgeSnapshot()
    {
        lock (sync) return archive.Bridges.Values.Select(receipt => receipt.Clone()).ToArray();
    }

    internal void StageBridge(PlayerItemBridgeReceipt receipt)
    {
        lock (sync)
        {
            if (!IsLoaded) throw new InvalidOperationException("Player item persistence has not loaded.");
            archive.Bridges[receipt.Request.OperationId] = receipt.Clone();
            metadataDirty = true;
        }
    }

    private static IDictionary<string, JToken>? CloneExtensions(IDictionary<string, JToken>? source)
    {
        if (source == null) return null;
        var clone = new Dictionary<string, JToken>(StringComparer.Ordinal);
        foreach ((string key, JToken value) in source)
            clone[key] = value.DeepClone();
        return clone;
    }

    private static void PreserveExtensions(PlayerItemStoreState prior, PlayerItemStoreState current)
    {
        current.ExtensionData = CloneExtensions(prior.ExtensionData);
        var entries = new Dictionary<string, PlayerItemEntryState>(StringComparer.OrdinalIgnoreCase);
        foreach (PlayerItemEntryState entry in prior.Entries)
            entries[entry.EntryId] = entry;
        foreach (PlayerItemEntryState entry in current.Entries)
        {
            if (entries.TryGetValue(entry.EntryId, out PlayerItemEntryState? oldEntry))
                entry.ExtensionData = CloneExtensions(oldEntry.ExtensionData);
        }

        var reservations = new Dictionary<string, PlayerItemReservationState>(StringComparer.OrdinalIgnoreCase);
        foreach (PlayerItemReservationState reservation in prior.Reservations)
            reservations[reservation.ReservationId] = reservation;
        foreach (PlayerItemReservationState reservation in current.Reservations)
        {
            if (reservations.TryGetValue(reservation.ReservationId, out PlayerItemReservationState? oldReservation))
                reservation.ExtensionData = CloneExtensions(oldReservation.ExtensionData);
        }

        var requests = new Dictionary<string, PlayerItemCompletedRequestState>(StringComparer.Ordinal);
        foreach (PlayerItemCompletedRequestState request in prior.CompletedRequests)
            requests[request.RequestId] = request;
        foreach (PlayerItemCompletedRequestState request in current.CompletedRequests)
        {
            if (requests.TryGetValue(request.RequestId, out PlayerItemCompletedRequestState? oldRequest))
                request.ExtensionData = CloneExtensions(oldRequest.ExtensionData);
        }
    }

    private static bool ValidateMigrationJournal(
        PlayerItemMigrationJournalState? journal,
        out string error)
    {
        error = "";
        if (journal == null
            || journal.MigrationVersion <= 0
            || journal.MigrationVersion > PlayerItemMigrationSchema.CurrentVersion
            || !Enum.IsDefined(journal.Status)
            || journal.SourceSlots == null
            || journal.SourceStackCount != journal.SourceSlots.Length
            || journal.SourceStackCount < 0
            || journal.SourceItemCount < 0
            || journal.ImportedEntryCount < 0
            || journal.OverflowEntryCount < 0
            || journal.UnresolvedEntryCount < 0)
        {
            error = "The journal header is invalid.";
            return false;
        }
        long itemCount;
        try
        {
            itemCount = journal.SourceSlots.Aggregate(
                0L,
                (total, slot) => checked(total + (slot?.Quantity ?? 0)));
        }
        catch (OverflowException)
        {
            error = "The source item count is invalid.";
            return false;
        }
        if (itemCount != journal.SourceItemCount
            || journal.SourceSlots.Any(slot => slot == null
                || slot.Quantity <= 0
                || slot.SlotIndex < 0
                || string.IsNullOrWhiteSpace(slot.InventoryClass)
                || slot.Prototype == null
                || slot.Prototype.ExactState == null))
        {
            error = "The source snapshot is inconsistent.";
            return false;
        }
        if (!InMemoryPlayerItemStore.TryRestore(journal.PriorStore, out _, out error))
        {
            error = "The prior inventory snapshot is invalid: " + error;
            return false;
        }
        return true;
    }
}
