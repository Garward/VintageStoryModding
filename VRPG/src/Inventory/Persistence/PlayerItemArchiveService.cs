using System;
using System.Collections.Generic;
using System.Linq;
using VRPG.Inventory.Migration;
using Newtonsoft.Json.Linq;

namespace VRPG.Inventory.Persistence;

/// <summary>Loads all stores before use and snapshots only stores whose durable state changed.</summary>
public sealed class PlayerItemArchiveService
{
    private readonly object sync = new();
    private readonly PlayerItemArchiveRepository repository;
    private PlayerItemArchive archive = new();
    private readonly HashSet<string> forcedPlayers = new(StringComparer.OrdinalIgnoreCase);
    private bool metadataDirty;

    public PlayerItemArchiveService(
        PlayerItemArchiveRepository repository,
        PlayerItemStoreDirectory? directory = null)
    {
        this.repository = repository ?? throw new ArgumentNullException(nameof(repository));
        Directory = directory ?? new PlayerItemStoreDirectory();
    }

    public PlayerItemStoreDirectory Directory { get; }
    public bool IsLoaded { get; private set; }
    public long Generation { get; private set; }

    public PlayerItemArchiveLoadResult Load()
    {
        lock (sync)
        {
            IsLoaded = false;
            PlayerItemArchiveLoadResult loaded = repository.Load();
            if (!loaded.Success) return loaded;

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

            Directory.ReplaceAll(stores);
            archive = loaded.Archive;
            Generation = loaded.Generation;
            forcedPlayers.Clear();
            metadataDirty = false;
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

            if (captures.Count == 0 && !metadataDirty && Generation > 0)
                return new PlayerItemArchiveSaveResult(true, Generation, "");

            var nextArchive = new PlayerItemArchive
            {
                SchemaVersion = PlayerItemPersistenceSchema.CurrentVersion,
                Players = nextPlayers,
                Migrations = new Dictionary<string, PlayerItemMigrationJournalState>(
                    archive.Migrations,
                    StringComparer.OrdinalIgnoreCase),
                ExtensionData = CloneExtensions(archive.ExtensionData)
            };
            PlayerItemArchiveSaveResult saved = repository.Save(nextArchive);
            if (!saved.Success) return saved;

            archive = nextArchive;
            Generation = saved.Generation;
            foreach ((InMemoryPlayerItemStore store, PlayerItemStoreCapture capture) in captures)
                store.MarkPersisted(capture.PersistenceStamp);
            forcedPlayers.Clear();
            metadataDirty = false;
            return saved;
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
