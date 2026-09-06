using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using VRPG.Inventory.Persistence;

namespace VRPG.Inventory;

public sealed partial class InMemoryPlayerItemStore
{
    private IDictionary<string, JToken>? extensionData;
    public bool IsDirty
    {
        get
        {
            lock (sync) return persistenceStamp != persistedStamp;
        }
    }

    public PlayerItemStoreCapture Capture()
    {
        lock (sync)
        {
            PlayerItemEntryState[] entryStates = byId.Values
                .OrderBy(entry => entry.EntryId, StringComparer.OrdinalIgnoreCase)
                .Select(entry => new PlayerItemEntryState
                {
                    EntryId = entry.EntryId,
                    Prototype = ClonePrototype(entry.Prototype),
                    Quantity = entry.Quantity,
                    Location = entry.Location,
                    Flags = entry.Flags,
                    ProtectionSource = entry.ProtectionSource,
                    ExtensionData = CloneExtensions(entry.ExtensionData),
                    RecoveryReason = entry.RecoveryReason,
                    RecoverySource = entry.RecoverySource
                })
                .ToArray();
            EquippedPlayerItemRecord[] equipment = equipmentByRole
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new EquippedPlayerItemRecord(pair.Key, pair.Value))
                .ToArray();
            PlayerItemReservationState[] reservationStates = reservations.Values
                .OrderBy(value => value.ReservationId, StringComparer.OrdinalIgnoreCase)
                .Select(value => new PlayerItemReservationState
                {
                    ReservationId = value.ReservationId,
                    Amounts = value.Amounts.ToArray()
                })
                .ToArray();
            PlayerItemCompletedRequestState[] requestStates = requestCache
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => CaptureRequest(pair.Key, pair.Value))
                .ToArray();

            var state = new PlayerItemStoreState
            {
                Revision = revision,
                Capacity = capacity,
                Entries = entryStates,
                Equipment = equipment,
                Reservations = reservationStates,
                CompletedRequests = requestStates,
                ExtensionData = CloneExtensions(extensionData)
            };
            return new PlayerItemStoreCapture(state, persistenceStamp);
        }
    }

    public void MarkPersisted(long capturedPersistenceStamp)
    {
        lock (sync)
        {
            if (capturedPersistenceStamp == persistenceStamp)
                persistedStamp = capturedPersistenceStamp;
        }
    }

    public static bool TryRestore(
        PlayerItemStoreState? state,
        out InMemoryPlayerItemStore? store,
        out string error,
        Func<string>? createEntryId = null,
        Func<string>? createReservationId = null,
        Func<byte[], string>? createStateHash = null,
        PlayerItemCategoryRegistry? categories = null)
    {
        store = null;
        error = "";
        if (state == null || state.Capacity == null || !state.Capacity.IsValid || state.Revision < 0)
            return Fail("The persisted store header is invalid.", out error);
        if (state.Entries == null || state.Equipment == null
            || state.Reservations == null || state.CompletedRequests == null)
        {
            return Fail("A persisted store collection is missing.", out error);
        }
        if (state.Entries.Length > 100_000 || state.Reservations.Length > 100_000
            || state.CompletedRequests.Length > 1_000_000)
        {
            return Fail("The persisted store exceeds safety limits.", out error);
        }

        var restored = new InMemoryPlayerItemStore(
            createEntryId,
            createReservationId,
            state.Capacity,
            createStateHash,
            categories);
        restored.revision = state.Revision;
        restored.extensionData = CloneExtensions(state.ExtensionData);

        foreach (PlayerItemEntryState entryState in state.Entries)
        {
            if (entryState == null
                || string.IsNullOrWhiteSpace(entryState.EntryId)
                || restored.byId.ContainsKey(entryState.EntryId)
                || !IsValid(entryState.Prototype, entryState.Quantity)
                || !Enum.IsDefined(entryState.Location)
                || !FlagsAreValid(entryState.Flags)
                || !Enum.IsDefined(entryState.RecoveryReason)
                || !PlayerItemPolicy.QuantityFits(entryState.Prototype, entryState.Quantity, state.Capacity))
            {
                return Fail("A persisted inventory entry is invalid or duplicated.", out error);
            }
            if (entryState.Location == PlayerItemLocation.Equipped
                && entryState.Prototype.Kind != PlayerItemStorageKind.Unique)
            {
                return Fail("An aggregate entry cannot be equipped.", out error);
            }
            PlayerItemProtectionSource protection = entryState.ProtectionSource
                ?? PlayerItemPolicy.LegacyProtection(entryState.Prototype, entryState.Flags);
            const PlayerItemProtectionSource validProtection = PlayerItemProtectionSource.User | PlayerItemProtectionSource.System;
            if ((protection & ~validProtection) != 0
                || PlayerItemPolicy.RequiresSystemProtection(entryState.Prototype)
                    && !protection.HasFlag(PlayerItemProtectionSource.System)
                || entryState.ProtectionSource.HasValue
                    && PlayerItemPolicy.EffectiveFlags(entryState.Flags, protection) != entryState.Flags)
            {
                return Fail("Persisted protection metadata is inconsistent.", out error);
            }
            if (PlayerItemPolicy.RequiresRecovery(entryState.Prototype)
                && entryState.Location is PlayerItemLocation.Stored or PlayerItemLocation.Equipped)
            {
                return Fail("Unsafe evolving content must remain frozen in recovery.", out error);
            }
            if (entryState.Location is PlayerItemLocation.Overflow or PlayerItemLocation.Unresolved
                && string.IsNullOrWhiteSpace(entryState.RecoverySource))
            {
                return Fail("A recovery entry is missing its recovery source.", out error);
            }

            PlayerItemPrototype prototype = ClonePrototype(entryState.Prototype);
            CandidateKey key = restored.KeyFor(prototype);
            var entry = new StoredEntry(entryState.EntryId, prototype, entryState.Quantity, key)
            {
                Location = entryState.Location,
                Flags = PlayerItemPolicy.EffectiveFlags(entryState.Flags, protection),
                ProtectionSource = protection,
                ExtensionData = CloneExtensions(entryState.ExtensionData),
                RecoveryReason = entryState.RecoveryReason,
                RecoverySource = entryState.RecoverySource?.Trim() ?? ""
            };
            restored.byId.Add(entry.EntryId, entry);
            restored.AddToQueryIndex(entry);
            if (!restored.collisionBuckets.TryGetValue(key, out List<StoredEntry>? bucket))
            {
                bucket = new List<StoredEntry>();
                restored.collisionBuckets.Add(key, bucket);
            }
            bucket.Add(entry);
        }

        if (restored.ActiveCount(PlayerItemStorageKind.Unique) > state.Capacity.UniqueEntryLimit
            || restored.ActiveAggregateCount() > state.Capacity.AggregateEntryLimit)
        {
            return Fail("Persisted active entries exceed configured capacity.", out error);
        }

        var equippedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (EquippedPlayerItemRecord equipped in state.Equipment)
        {
            string role = equipped?.RoleCode?.Trim().ToLowerInvariant() ?? "";
            if (role.Length == 0
                || !restored.byId.TryGetValue(equipped!.EntryId, out StoredEntry? entry)
                || entry.Location != PlayerItemLocation.Equipped
                || !equippedIds.Add(entry.EntryId)
                || !restored.equipmentByRole.TryAdd(role, entry.EntryId))
            {
                return Fail("Persisted equipment references are inconsistent.", out error);
            }
        }
        if (restored.byId.Values.Any(entry =>
            entry.Location == PlayerItemLocation.Equipped && !equippedIds.Contains(entry.EntryId)))
        {
            return Fail("An equipped entry has no equipment role.", out error);
        }

        foreach (PlayerItemReservationState reservationState in state.Reservations)
        {
            if (reservationState == null
                || string.IsNullOrWhiteSpace(reservationState.ReservationId)
                || restored.reservations.ContainsKey(reservationState.ReservationId)
                || !TryNormalizeAmounts(reservationState.Amounts, out PlayerItemAmount[] normalized))
            {
                return Fail("A persisted reservation is invalid or duplicated.", out error);
            }
            foreach (PlayerItemAmount amount in normalized)
            {
                if (!restored.byId.TryGetValue(amount.EntryId, out StoredEntry? entry)
                    || entry.Location != PlayerItemLocation.Stored
                    || entry.Flags.HasFlag(PlayerItemFlags.Protected))
                {
                    return Fail("A persisted reservation references an unavailable entry.", out error);
                }
                long existing = restored.ReservedQuantity(entry.EntryId);
                if (amount.Quantity > entry.Quantity - existing)
                    return Fail("Persisted reservations exceed an entry quantity.", out error);
                restored.reservedByEntry[entry.EntryId] = existing + amount.Quantity;
            }
            restored.reservations.Add(
                reservationState.ReservationId,
                new PendingReservation(reservationState.ReservationId, normalized));
        }

        foreach (PlayerItemCompletedRequestState requestState in state.CompletedRequests)
        {
            if (!TryRestoreRequest(requestState, out CachedRequest? request)
                || !restored.requestCache.TryAdd(requestState.RequestId, request!))
            {
                return Fail("A persisted completed request is invalid or duplicated.", out error);
            }
        }

        restored.persistenceStamp = 0;
        restored.persistedStamp = 0;
        store = restored;
        return true;
    }

    private static PlayerItemPrototype ClonePrototype(PlayerItemPrototype prototype) =>
        prototype with
        {
            ExactState = (byte[])prototype.ExactState.Clone(),
            EmbeddedReferences = CloneReferences(prototype.EmbeddedReferences)
        };

    private static IDictionary<string, JToken>? CloneExtensions(IDictionary<string, JToken>? source) =>
        source?.ToDictionary(pair => pair.Key, pair => pair.Value.DeepClone(), StringComparer.Ordinal);

    private static bool FlagsAreValid(PlayerItemFlags flags)
    {
        const PlayerItemFlags valid = PlayerItemFlags.Favorite | PlayerItemFlags.Protected | PlayerItemFlags.Junk;
        return (flags & ~valid) == 0
            && !(flags.HasFlag(PlayerItemFlags.Favorite) && flags.HasFlag(PlayerItemFlags.Junk));
    }

    private static PlayerItemCompletedRequestState CaptureRequest(string requestId, CachedRequest request)
    {
        var state = new PlayerItemCompletedRequestState
        {
            RequestId = requestId,
            Fingerprint = request.Fingerprint
        };
        switch (request.Result)
        {
            case PlayerItemMutationResult mutation:
                state.ResultKind = PlayerItemPersistedResultKind.Mutation;
                state.Success = mutation.Success;
                state.Revision = mutation.Revision;
                state.EntryId = mutation.EntryId;
                state.Quantity = mutation.Quantity;
                state.Rejection = (int)mutation.Rejection;
                break;
            case PlayerItemOperationResult operation:
                state.ResultKind = PlayerItemPersistedResultKind.Operation;
                state.Success = operation.Success;
                state.Revision = operation.Revision;
                state.EntryId = operation.EntryId;
                state.RoleCode = operation.RoleCode;
                state.Rejection = (int)operation.Rejection;
                break;
            case PlayerItemReservationResult reservation:
                state.ResultKind = PlayerItemPersistedResultKind.Reservation;
                state.Success = reservation.Success;
                state.Revision = reservation.Revision;
                state.ReservationId = reservation.ReservationId;
                state.Rejection = (int)reservation.Rejection;
                break;
            default:
                throw new InvalidOperationException("The request cache contains an unsupported result type.");
        }
        return state;
    }

    private static bool TryRestoreRequest(
        PlayerItemCompletedRequestState? state,
        out CachedRequest? request)
    {
        request = null;
        if (state == null
            || string.IsNullOrWhiteSpace(state.RequestId)
            || state.RequestId.Length > 128
            || string.IsNullOrWhiteSpace(state.Fingerprint)
            || state.Revision < 0
            || state.Success != (state.Rejection == 0)
            || !Enum.IsDefined(state.ResultKind))
        {
            return false;
        }

        object result;
        switch (state.ResultKind)
        {
            case PlayerItemPersistedResultKind.Mutation:
                if (!Enum.IsDefined((PlayerItemMutationRejection)state.Rejection)) return false;
                result = new PlayerItemMutationResult(
                    state.Success,
                    state.Revision,
                    state.EntryId ?? "",
                    state.Quantity,
                    (PlayerItemMutationRejection)state.Rejection,
                    state.RequestId);
                break;
            case PlayerItemPersistedResultKind.Operation:
                if (!Enum.IsDefined((PlayerItemOperationRejection)state.Rejection)) return false;
                result = new PlayerItemOperationResult(
                    state.Success,
                    state.Revision,
                    state.RequestId,
                    state.EntryId ?? "",
                    state.RoleCode ?? "",
                    (PlayerItemOperationRejection)state.Rejection);
                break;
            case PlayerItemPersistedResultKind.Reservation:
                if (!Enum.IsDefined((PlayerItemOperationRejection)state.Rejection)) return false;
                result = new PlayerItemReservationResult(
                    state.Success,
                    state.Revision,
                    state.RequestId,
                    state.ReservationId ?? "",
                    (PlayerItemOperationRejection)state.Rejection);
                break;
            default:
                return false;
        }
        request = new CachedRequest(state.Fingerprint, result);
        return true;
    }

    private static bool Fail(string message, out string error)
    {
        error = message;
        return false;
    }
}
