using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;

namespace VRPG.Inventory;

/// <summary>Revision-safe storage core. Persistence and ItemStack materialization are separate adapters.</summary>
public sealed partial class InMemoryPlayerItemStore : IPlayerItemStore
{
    public const int MaximumPageSize = 100;
    private readonly object sync = new();
    private readonly Func<string> createEntryId;
    private readonly Func<string> createReservationId;
    private readonly Func<byte[], string> createStateHash;
    private readonly PlayerItemCapacity capacity;
    private readonly Dictionary<string, StoredEntry> byId = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<CandidateKey, List<StoredEntry>> collisionBuckets = new();
    private readonly Dictionary<QueryIndexKey, HashSet<string>> queryIndex = new();
    private readonly Dictionary<string, CachedRequest> requestCache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PendingReservation> reservations = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> reservedByEntry = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> equipmentByRole = new(StringComparer.Ordinal);
    private long revision;
    private long persistenceStamp;
    private long persistedStamp;

    public InMemoryPlayerItemStore(
        Func<string>? createEntryId = null,
        Func<string>? createReservationId = null,
        PlayerItemCapacity? capacity = null,
        Func<byte[], string>? createStateHash = null)
    {
        this.createEntryId = createEntryId ?? (() => Guid.NewGuid().ToString("N"));
        this.createReservationId = createReservationId ?? (() => Guid.NewGuid().ToString("N"));
        this.createStateHash = createStateHash ?? (state => Convert.ToHexString(SHA256.HashData(state)));
        this.capacity = capacity ?? PlayerItemCapacity.Default;
        if (!this.capacity.IsValid) throw new ArgumentException("Inventory capacity must be positive.", nameof(capacity));
    }

    public long Revision
    {
        get
        {
            lock (sync) return revision;
        }
    }

    public PlayerItemPage Query(PlayerItemQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        int offset = Math.Max(0, query.Offset);
        int limit = Math.Clamp(query.Limit, 1, MaximumPageSize);
        string category = query.Category?.Trim() ?? "";
        string search = query.Search?.Trim() ?? "";

        lock (sync)
        {
            QueryIndexKey indexKey = new(query.Location, category.ToLowerInvariant());
            IEnumerable<StoredEntry> matching = queryIndex.TryGetValue(indexKey, out HashSet<string>? indexedIds)
                ? indexedIds.Select(entryId => byId[entryId])
                : Enumerable.Empty<StoredEntry>();
            if (!string.IsNullOrWhiteSpace(search))
            {
                matching = matching.Where(entry =>
                    entry.Prototype.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase)
                    || entry.Prototype.AssetCode.Contains(search, StringComparison.OrdinalIgnoreCase));
            }

            IOrderedEnumerable<StoredEntry> sorted = query.Sort switch
            {
                PlayerItemSortOrder.QuantityDescending => matching
                    .OrderByDescending(entry => entry.Quantity)
                    .ThenBy(entry => entry.Prototype.DisplayName, StringComparer.OrdinalIgnoreCase),
                PlayerItemSortOrder.AssetCode => matching
                    .OrderBy(entry => entry.Prototype.AssetCode, StringComparer.OrdinalIgnoreCase),
                _ => matching.OrderBy(
                    entry => entry.Prototype.DisplayName,
                    StringComparer.OrdinalIgnoreCase)
            };
            StoredEntry[] ordered = sorted
                .ThenBy(entry => entry.EntryId, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            PlayerItemSummary[] page = ordered
                .Skip(offset)
                .Take(limit)
                .Select(Summary)
                .ToArray();
            return new PlayerItemPage(revision, ordered.Length, page);
        }
    }

    public bool TryGet(string entryId, out PlayerItemDetail? detail)
    {
        lock (sync)
        {
            if (string.IsNullOrWhiteSpace(entryId)
                || !byId.TryGetValue(entryId, out StoredEntry? entry))
            {
                detail = null;
                return false;
            }

            detail = DetailClone(entry);
            return true;
        }
    }

    public PlayerItemMutationResult Insert(
        PlayerItemPrototype prototype,
        long quantity,
        long expectedRevision)
    {
        if (!IsValid(prototype, quantity))
        {
            return PlayerItemMutationResult.Rejected(Revision, PlayerItemMutationRejection.InvalidRequest);
        }

        lock (sync)
        {
            if (expectedRevision != revision)
            {
                return PlayerItemMutationResult.Rejected(revision, PlayerItemMutationRejection.StaleRevision);
            }

            if (prototype.Kind == PlayerItemStorageKind.Unique)
            {
                if (ActiveCount(PlayerItemStorageKind.Unique) >= capacity.UniqueEntryLimit)
                    return PlayerItemMutationResult.Rejected(revision, PlayerItemMutationRejection.CapacityExceeded);
                StoredEntry unique = AddNew(prototype, 1);
                AdvanceRevision();
                return Accepted(unique);
            }

            CandidateKey key = KeyFor(prototype);
            StoredEntry? existing = ExactMatch(key, prototype);
            if (existing == null)
            {
                if (ActiveAggregateCount() >= capacity.AggregateEntryLimit)
                    return PlayerItemMutationResult.Rejected(revision, PlayerItemMutationRejection.CapacityExceeded);
                if (quantity > capacity.AggregateQuantityPerEntry)
                    return PlayerItemMutationResult.Rejected(revision, PlayerItemMutationRejection.QuantityOverflow);
                existing = AddNew(prototype, quantity, key);
            }
            else
            {
                try
                {
                    long newQuantity = checked(existing.Quantity + quantity);
                    if (newQuantity > capacity.AggregateQuantityPerEntry)
                        return PlayerItemMutationResult.Rejected(revision, PlayerItemMutationRejection.QuantityOverflow);
                    existing.Quantity = newQuantity;
                }
                catch (OverflowException)
                {
                    return PlayerItemMutationResult.Rejected(
                        revision,
                        PlayerItemMutationRejection.QuantityOverflow);
                }
            }

            AdvanceRevision();
            return Accepted(existing);
        }
    }

    public PlayerItemMutationResult Insert(
        string requestId,
        PlayerItemPrototype prototype,
        long quantity,
        long expectedRevision)
    {
        string fingerprint = $"insert|{expectedRevision}|{quantity}|{PrototypeFingerprint(prototype)}";
        lock (sync)
        {
            if (!TryBeginRequest(requestId, fingerprint, out PlayerItemMutationResult? replay, out bool conflict))
                return conflict
                    ? PlayerItemMutationResult.Rejected(revision, PlayerItemMutationRejection.RequestIdConflict, requestId)
                    : PlayerItemMutationResult.Rejected(revision, PlayerItemMutationRejection.InvalidRequest, requestId);
            if (replay != null) return replay;

            PlayerItemMutationResult result = Insert(prototype, quantity, expectedRevision) with { RequestId = requestId };
            RememberRequest(requestId, fingerprint, result);
            return result;
        }
    }

    public PlayerItemMutationResult Extract(string entryId, long quantity, long expectedRevision)
    {
        if (string.IsNullOrWhiteSpace(entryId) || quantity <= 0)
        {
            return PlayerItemMutationResult.Rejected(Revision, PlayerItemMutationRejection.InvalidRequest);
        }

        lock (sync)
        {
            if (expectedRevision != revision)
            {
                return PlayerItemMutationResult.Rejected(revision, PlayerItemMutationRejection.StaleRevision);
            }
            if (!byId.TryGetValue(entryId, out StoredEntry? entry))
            {
                return PlayerItemMutationResult.Rejected(revision, PlayerItemMutationRejection.EntryMissing);
            }
            if (entry.Location != PlayerItemLocation.Stored)
                return PlayerItemMutationResult.Rejected(revision, PlayerItemMutationRejection.EntryUnavailable);
            if (entry.Flags.HasFlag(PlayerItemFlags.Protected))
                return PlayerItemMutationResult.Rejected(revision, PlayerItemMutationRejection.EntryProtected);
            long reserved = ReservedQuantity(entry.EntryId);
            if (quantity > entry.Quantity - reserved)
                return PlayerItemMutationResult.Rejected(revision, PlayerItemMutationRejection.EntryReserved);
            if (quantity > entry.Quantity || entry.Prototype.Kind == PlayerItemStorageKind.Unique && quantity != 1)
            {
                return PlayerItemMutationResult.Rejected(
                    revision,
                    PlayerItemMutationRejection.InsufficientQuantity);
            }

            entry.Quantity -= quantity;
            long remaining = entry.Quantity;
            if (remaining == 0)
            {
                Remove(entry);
            }
            AdvanceRevision();
            return new PlayerItemMutationResult(
                true,
                revision,
                entry.EntryId,
                remaining,
                PlayerItemMutationRejection.None);
        }
    }

    public PlayerItemMutationResult Extract(
        string requestId,
        string entryId,
        long quantity,
        long expectedRevision)
    {
        string fingerprint = $"extract|{expectedRevision}|{entryId}|{quantity}";
        lock (sync)
        {
            if (!TryBeginRequest(requestId, fingerprint, out PlayerItemMutationResult? replay, out bool conflict))
                return conflict
                    ? PlayerItemMutationResult.Rejected(revision, PlayerItemMutationRejection.RequestIdConflict, requestId)
                    : PlayerItemMutationResult.Rejected(revision, PlayerItemMutationRejection.InvalidRequest, requestId);
            if (replay != null) return replay;

            PlayerItemMutationResult result = Extract(entryId, quantity, expectedRevision) with { RequestId = requestId };
            RememberRequest(requestId, fingerprint, result);
            return result;
        }
    }

    private StoredEntry AddNew(PlayerItemPrototype prototype, long quantity, CandidateKey? knownKey = null)
    {
        string id = createEntryId();
        if (string.IsNullOrWhiteSpace(id) || byId.ContainsKey(id))
        {
            throw new InvalidOperationException("The item-store entry ID source returned an invalid or duplicate ID.");
        }

        PlayerItemPrototype owned = prototype with
        {
            ExactState = (byte[])prototype.ExactState.Clone(),
            EmbeddedReferences = CloneReferences(prototype.EmbeddedReferences)
        };
        CandidateKey key = knownKey ?? KeyFor(owned);
        var entry = new StoredEntry(id, owned, quantity, key);
        byId.Add(id, entry);
        AddToQueryIndex(entry);
        if (!collisionBuckets.TryGetValue(key, out List<StoredEntry>? bucket))
        {
            bucket = new List<StoredEntry>();
            collisionBuckets.Add(key, bucket);
        }
        bucket.Add(entry);
        return entry;
    }

    private StoredEntry? ExactMatch(CandidateKey key, PlayerItemPrototype prototype)
    {
        if (!collisionBuckets.TryGetValue(key, out List<StoredEntry>? bucket)) return null;
        foreach (StoredEntry entry in bucket)
        {
            if (entry.Location == PlayerItemLocation.Stored
                && entry.Prototype.Kind == prototype.Kind
                && entry.Prototype.AssetKind == prototype.AssetKind
                && string.Equals(entry.Prototype.AssetCode, prototype.AssetCode, StringComparison.OrdinalIgnoreCase)
                && string.Equals(entry.Prototype.Category, prototype.Category, StringComparison.OrdinalIgnoreCase)
                && entry.Prototype.ExactState.AsSpan().SequenceEqual(prototype.ExactState)
                && ReferencesEqual(entry.Prototype.EmbeddedReferences, prototype.EmbeddedReferences))
            {
                return entry;
            }
        }
        return null;
    }

    private void Remove(StoredEntry entry)
    {
        foreach (string role in equipmentByRole
            .Where(pair => string.Equals(pair.Value, entry.EntryId, StringComparison.OrdinalIgnoreCase))
            .Select(pair => pair.Key)
            .ToArray())
        {
            equipmentByRole.Remove(role);
        }
        reservedByEntry.Remove(entry.EntryId);
        RemoveFromQueryIndex(entry);
        byId.Remove(entry.EntryId);
        if (!collisionBuckets.TryGetValue(entry.Key, out List<StoredEntry>? bucket)) return;
        bucket.Remove(entry);
        if (bucket.Count == 0) collisionBuckets.Remove(entry.Key);
    }

    private PlayerItemMutationResult Accepted(StoredEntry entry) => new(
        true,
        revision,
        entry.EntryId,
        entry.Quantity,
        PlayerItemMutationRejection.None);

    private PlayerItemSummary Summary(StoredEntry entry) => new(
        entry.EntryId,
        entry.Prototype.Kind,
        entry.Prototype.AssetCode,
        entry.Prototype.Category,
        entry.Prototype.DisplayName,
        entry.Quantity,
        entry.Prototype.AssetKind,
        entry.Location,
        entry.Flags,
        ReservedQuantity(entry.EntryId));

    private static PlayerItemDetail DetailClone(StoredEntry entry) => new(
        entry.EntryId,
        entry.Prototype with
        {
            ExactState = (byte[])entry.Prototype.ExactState.Clone(),
            EmbeddedReferences = CloneReferences(entry.Prototype.EmbeddedReferences)
        },
        entry.Quantity,
        entry.Location,
        entry.Flags);

    private int ActiveCount(PlayerItemStorageKind kind) => byId.Values.Count(entry =>
        entry.Prototype.Kind == kind
        && entry.Location is PlayerItemLocation.Stored or PlayerItemLocation.Equipped);

    private int ActiveAggregateCount() => byId.Values.Count(entry =>
        entry.Prototype.Kind != PlayerItemStorageKind.Unique
        && entry.Location is PlayerItemLocation.Stored or PlayerItemLocation.Equipped);

    private long ReservedQuantity(string entryId) =>
        reservedByEntry.TryGetValue(entryId, out long quantity) ? quantity : 0;

    private void AdvanceRevision()
    {
        revision = checked(revision + 1);
        persistenceStamp = checked(persistenceStamp + 1);
    }

    private void MoveLocation(StoredEntry entry, PlayerItemLocation destination)
    {
        if (entry.Location == PlayerItemLocation.Equipped && destination != PlayerItemLocation.Equipped)
        {
            foreach (string role in equipmentByRole.Where(pair => pair.Value == entry.EntryId).Select(pair => pair.Key).ToArray())
                equipmentByRole.Remove(role);
        }
        if (entry.Location == destination) return;
        RemoveFromQueryIndex(entry);
        entry.Location = destination;
        AddToQueryIndex(entry);
    }

    private void AddToQueryIndex(StoredEntry entry)
    {
        AddToQueryIndex(new QueryIndexKey(entry.Location, ""), entry.EntryId);
        AddToQueryIndex(
            new QueryIndexKey(entry.Location, entry.Prototype.Category.Trim().ToLowerInvariant()),
            entry.EntryId);
    }

    private void AddToQueryIndex(QueryIndexKey key, string entryId)
    {
        if (!queryIndex.TryGetValue(key, out HashSet<string>? entries))
        {
            entries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            queryIndex.Add(key, entries);
        }
        entries.Add(entryId);
    }

    private void RemoveFromQueryIndex(StoredEntry entry)
    {
        RemoveFromQueryIndex(new QueryIndexKey(entry.Location, ""), entry.EntryId);
        RemoveFromQueryIndex(
            new QueryIndexKey(entry.Location, entry.Prototype.Category.Trim().ToLowerInvariant()),
            entry.EntryId);
    }

    private void RemoveFromQueryIndex(QueryIndexKey key, string entryId)
    {
        if (!queryIndex.TryGetValue(key, out HashSet<string>? entries)) return;
        entries.Remove(entryId);
        if (entries.Count == 0) queryIndex.Remove(key);
    }

    private static bool IsValid(PlayerItemPrototype? prototype, long quantity)
    {
        return prototype != null
            && quantity > 0
            && (prototype.Kind != PlayerItemStorageKind.Unique || quantity == 1)
            && !string.IsNullOrWhiteSpace(prototype.AssetCode)
            && !string.IsNullOrWhiteSpace(prototype.Category)
            && !string.IsNullOrWhiteSpace(prototype.DisplayName)
            && prototype.ExactState != null
            && ReferencesAreValid(prototype.EmbeddedReferences)
            && Enum.IsDefined(prototype.AssetKind);
    }

    private CandidateKey KeyFor(PlayerItemPrototype prototype)
    {
        return new CandidateKey(
            prototype.Kind,
            prototype.AssetKind,
            prototype.AssetCode.ToLowerInvariant(),
            prototype.Category.ToLowerInvariant(),
            createStateHash(prototype.ExactState));
    }

    private sealed class StoredEntry
    {
        public StoredEntry(
            string entryId,
            PlayerItemPrototype prototype,
            long quantity,
            CandidateKey key)
        {
            EntryId = entryId;
            Prototype = prototype;
            Quantity = quantity;
            Key = key;
        }

        public string EntryId { get; }
        public PlayerItemPrototype Prototype { get; }
        public long Quantity { get; set; }
        public CandidateKey Key { get; }
        public PlayerItemLocation Location { get; set; } = PlayerItemLocation.Stored;
        public PlayerItemFlags Flags { get; set; }
        public PlayerItemRecoveryReason RecoveryReason { get; set; }
        public string RecoverySource { get; set; } = "";
    }

    private readonly record struct CandidateKey(
        PlayerItemStorageKind Kind,
        PlayerItemAssetKind AssetKind,
        string AssetCode,
        string Category,
        string StateHash);

    private readonly record struct QueryIndexKey(PlayerItemLocation Location, string Category);

    private static CanonicalCollectibleReference[]? CloneReferences(
        CanonicalCollectibleReference[]? references) =>
        references == null ? null : (CanonicalCollectibleReference[])references.Clone();

    private static bool ReferencesAreValid(CanonicalCollectibleReference[]? references)
    {
        if (references == null) return true;
        var seen = new HashSet<(PlayerItemAssetKind Kind, int Id)>();
        foreach (CanonicalCollectibleReference reference in references)
        {
            if (reference == null
                || reference.SourceRegistryId <= 0
                || string.IsNullOrWhiteSpace(reference.AssetCode)
                || !Enum.IsDefined(reference.AssetKind)
                || !seen.Add((reference.AssetKind, reference.SourceRegistryId)))
            {
                return false;
            }
        }
        return true;
    }

    private static bool ReferencesEqual(
        CanonicalCollectibleReference[]? left,
        CanonicalCollectibleReference[]? right)
    {
        left ??= Array.Empty<CanonicalCollectibleReference>();
        right ??= Array.Empty<CanonicalCollectibleReference>();
        if (left.Length != right.Length) return false;

        CanonicalCollectibleReference[] orderedLeft = OrderReferences(left);
        CanonicalCollectibleReference[] orderedRight = OrderReferences(right);
        for (int index = 0; index < orderedLeft.Length; index++)
        {
            if (orderedLeft[index].AssetKind != orderedRight[index].AssetKind
                || orderedLeft[index].SourceRegistryId != orderedRight[index].SourceRegistryId
                || !string.Equals(
                    orderedLeft[index].AssetCode,
                    orderedRight[index].AssetCode,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }
        return true;
    }

    private static CanonicalCollectibleReference[] OrderReferences(
        CanonicalCollectibleReference[] references) =>
        references
            .OrderBy(reference => reference.AssetKind)
            .ThenBy(reference => reference.SourceRegistryId)
            .ThenBy(reference => reference.AssetCode, StringComparer.OrdinalIgnoreCase)
            .ToArray();
}
