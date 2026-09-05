using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace VRPG.Inventory;

public sealed partial class InMemoryPlayerItemStore
{
    public PlayerItemReservationResult Reserve(
        string requestId,
        PlayerItemAmount[] amounts,
        long expectedRevision)
    {
        string fingerprint = ReservationFingerprint("reserve", expectedRevision, amounts);
        lock (sync)
        {
            if (!TryBeginRequest(requestId, fingerprint, out PlayerItemReservationResult? replay, out bool conflict))
                return PlayerItemReservationResult.Rejected(
                    revision,
                    requestId,
                    conflict ? PlayerItemOperationRejection.RequestIdConflict : PlayerItemOperationRejection.InvalidRequest);
            if (replay != null) return replay;
            if (expectedRevision != revision)
                return Remember(requestId, fingerprint, PlayerItemReservationResult.Rejected(
                    revision, requestId, PlayerItemOperationRejection.StaleRevision));
            if (!TryNormalizeAmounts(amounts, out PlayerItemAmount[] normalized))
                return Remember(requestId, fingerprint, PlayerItemReservationResult.Rejected(
                    revision, requestId, PlayerItemOperationRejection.InvalidRequest));

            foreach (PlayerItemAmount amount in normalized)
            {
                if (!byId.TryGetValue(amount.EntryId, out StoredEntry? entry))
                    return Remember(requestId, fingerprint, PlayerItemReservationResult.Rejected(
                        revision, requestId, PlayerItemOperationRejection.EntryMissing));
                if (entry.Location != PlayerItemLocation.Stored)
                    return Remember(requestId, fingerprint, PlayerItemReservationResult.Rejected(
                        revision, requestId, PlayerItemOperationRejection.EntryUnavailable));
                if (entry.Flags.HasFlag(PlayerItemFlags.Protected))
                    return Remember(requestId, fingerprint, PlayerItemReservationResult.Rejected(
                        revision, requestId, PlayerItemOperationRejection.EntryProtected));
                if (entry.Prototype.Kind == PlayerItemStorageKind.Unique && amount.Quantity != 1
                    || amount.Quantity > entry.Quantity)
                {
                    return Remember(requestId, fingerprint, PlayerItemReservationResult.Rejected(
                        revision, requestId, PlayerItemOperationRejection.InsufficientQuantity));
                }
                if (amount.Quantity > entry.Quantity - ReservedQuantity(entry.EntryId))
                    return Remember(requestId, fingerprint, PlayerItemReservationResult.Rejected(
                        revision, requestId, PlayerItemOperationRejection.EntryReserved));
            }

            string reservationId = createReservationId();
            if (string.IsNullOrWhiteSpace(reservationId) || reservations.ContainsKey(reservationId))
                throw new InvalidOperationException("The reservation ID source returned an invalid or duplicate ID.");
            var reservation = new PendingReservation(reservationId, normalized);
            reservations.Add(reservationId, reservation);
            foreach (PlayerItemAmount amount in normalized)
                reservedByEntry[amount.EntryId] = checked(ReservedQuantity(amount.EntryId) + amount.Quantity);
            AdvanceRevision();
            return Remember(requestId, fingerprint,
                PlayerItemReservationResult.Accepted(revision, requestId, reservationId));
        }
    }

    public PlayerItemOperationResult CommitReservation(string requestId, string reservationId)
    {
        string fingerprint = $"commit|{reservationId}";
        lock (sync)
        {
            if (!TryBeginRequest(requestId, fingerprint, out PlayerItemOperationResult? replay, out bool conflict))
                return PlayerItemOperationResult.Rejected(
                    revision,
                    requestId,
                    conflict ? PlayerItemOperationRejection.RequestIdConflict : PlayerItemOperationRejection.InvalidRequest);
            if (replay != null) return replay;
            if (!reservations.TryGetValue(reservationId, out PendingReservation? reservation))
                return Remember(requestId, fingerprint, PlayerItemOperationResult.Rejected(
                    revision, requestId, PlayerItemOperationRejection.ReservationMissing));

            foreach (PlayerItemAmount amount in reservation.Amounts)
            {
                if (!byId.TryGetValue(amount.EntryId, out StoredEntry? entry)
                    || entry.Location != PlayerItemLocation.Stored
                    || entry.Quantity < amount.Quantity)
                {
                    return Remember(requestId, fingerprint, PlayerItemOperationResult.Rejected(
                        revision, requestId, PlayerItemOperationRejection.EntryUnavailable));
                }
            }

            ReleaseReservation(reservation);
            foreach (PlayerItemAmount amount in reservation.Amounts)
            {
                StoredEntry entry = byId[amount.EntryId];
                entry.Quantity -= amount.Quantity;
                if (entry.Quantity == 0) Remove(entry);
            }
            AdvanceRevision();
            return Remember(requestId, fingerprint,
                PlayerItemOperationResult.Accepted(revision, requestId));
        }
    }

    public PlayerItemReservationSnapshot Reservation(string reservationId)
    {
        lock (sync)
        {
            if (string.IsNullOrWhiteSpace(reservationId)
                || !reservations.TryGetValue(reservationId, out PendingReservation? reservation))
            {
                return new PlayerItemReservationSnapshot(
                    revision,
                    false,
                    reservationId ?? "",
                    Array.Empty<PlayerItemReservedEntry>());
            }

            PlayerItemReservedEntry[] entries = reservation.Amounts.Select(amount =>
            {
                StoredEntry entry = byId[amount.EntryId];
                PlayerItemPrototype prototype = entry.Prototype with
                {
                    ExactState = (byte[])entry.Prototype.ExactState.Clone(),
                    EmbeddedReferences = CloneReferences(entry.Prototype.EmbeddedReferences)
                };
                return new PlayerItemReservedEntry(entry.EntryId, prototype, amount.Quantity);
            }).ToArray();
            return new PlayerItemReservationSnapshot(revision, true, reservationId, entries);
        }
    }

    public PlayerItemOperationResult RollbackReservation(string requestId, string reservationId)
    {
        string fingerprint = $"rollback|{reservationId}";
        lock (sync)
        {
            if (!TryBeginRequest(requestId, fingerprint, out PlayerItemOperationResult? replay, out bool conflict))
                return PlayerItemOperationResult.Rejected(
                    revision,
                    requestId,
                    conflict ? PlayerItemOperationRejection.RequestIdConflict : PlayerItemOperationRejection.InvalidRequest);
            if (replay != null) return replay;
            if (!reservations.TryGetValue(reservationId, out PendingReservation? reservation))
                return Remember(requestId, fingerprint, PlayerItemOperationResult.Rejected(
                    revision, requestId, PlayerItemOperationRejection.ReservationMissing));

            ReleaseReservation(reservation);
            AdvanceRevision();
            return Remember(requestId, fingerprint,
                PlayerItemOperationResult.Accepted(revision, requestId));
        }
    }

    public bool TryGetCompletedEquip(
        string requestId,
        string entryId,
        string roleCode,
        long expectedRevision,
        out PlayerItemOperationResult? result)
    {
        string fingerprint = EquipFingerprint(entryId, roleCode, expectedRevision);
        lock (sync)
        {
            if (TryBeginRequest(
                requestId,
                fingerprint,
                out PlayerItemOperationResult? replay,
                out bool conflict))
            {
                result = replay;
                return replay != null;
            }

            if (conflict)
            {
                result = PlayerItemOperationResult.Rejected(
                    revision,
                    requestId,
                    PlayerItemOperationRejection.RequestIdConflict);
                return true;
            }

            result = null;
            return false;
        }
    }

    public PlayerItemOperationResult Equip(
        string requestId,
        string entryId,
        string roleCode,
        long expectedRevision)
    {
        string fingerprint = EquipFingerprint(entryId, roleCode, expectedRevision);
        lock (sync)
        {
            if (!BeginOperation(requestId, fingerprint, out PlayerItemOperationResult? immediate)) return immediate!;
            if (expectedRevision != revision)
                return RejectAndRemember(requestId, fingerprint, PlayerItemOperationRejection.StaleRevision);
            if (string.IsNullOrWhiteSpace(roleCode))
                return RejectAndRemember(requestId, fingerprint, PlayerItemOperationRejection.EquipmentRoleMissing);
            if (!byId.TryGetValue(entryId, out StoredEntry? entry))
                return RejectAndRemember(requestId, fingerprint, PlayerItemOperationRejection.EntryMissing);
            if (entry.Prototype.Kind != PlayerItemStorageKind.Unique || entry.Quantity != 1)
                return RejectAndRemember(requestId, fingerprint, PlayerItemOperationRejection.NotEquippable);
            if (ReservedQuantity(entry.EntryId) != 0)
                return RejectAndRemember(requestId, fingerprint, PlayerItemOperationRejection.EntryReserved);
            if (entry.Location is PlayerItemLocation.Overflow or PlayerItemLocation.Unresolved)
                return RejectAndRemember(requestId, fingerprint, PlayerItemOperationRejection.EntryUnavailable);

            string normalizedRole = roleCode.Trim().ToLowerInvariant();
            if (equipmentByRole.TryGetValue(normalizedRole, out string? priorId)
                && byId.TryGetValue(priorId, out StoredEntry? prior)
                && !string.Equals(prior.EntryId, entry.EntryId, StringComparison.OrdinalIgnoreCase))
            {
                MoveLocation(prior, PlayerItemLocation.Stored);
            }
            foreach (string oldRole in equipmentByRole
                .Where(pair => string.Equals(pair.Value, entry.EntryId, StringComparison.OrdinalIgnoreCase))
                .Select(pair => pair.Key)
                .ToArray())
            {
                equipmentByRole.Remove(oldRole);
            }
            equipmentByRole[normalizedRole] = entry.EntryId;
            MoveLocation(entry, PlayerItemLocation.Equipped);
            AdvanceRevision();
            return Remember(requestId, fingerprint,
                PlayerItemOperationResult.Accepted(revision, requestId, entry.EntryId, normalizedRole));
        }
    }

    public PlayerItemOperationResult Unequip(string requestId, string roleCode, long expectedRevision)
    {
        string fingerprint = $"unequip|{expectedRevision}|{roleCode}";
        lock (sync)
        {
            if (!BeginOperation(requestId, fingerprint, out PlayerItemOperationResult? immediate)) return immediate!;
            if (expectedRevision != revision)
                return RejectAndRemember(requestId, fingerprint, PlayerItemOperationRejection.StaleRevision);
            string normalizedRole = roleCode?.Trim().ToLowerInvariant() ?? "";
            if (normalizedRole.Length == 0)
                return RejectAndRemember(requestId, fingerprint, PlayerItemOperationRejection.EquipmentRoleMissing);
            if (!equipmentByRole.TryGetValue(normalizedRole, out string? entryId))
                return RejectAndRemember(requestId, fingerprint, PlayerItemOperationRejection.EntryMissing);

            equipmentByRole.Remove(normalizedRole);
            if (byId.TryGetValue(entryId, out StoredEntry? entry))
                MoveLocation(entry, PlayerItemLocation.Stored);
            AdvanceRevision();
            return Remember(requestId, fingerprint,
                PlayerItemOperationResult.Accepted(revision, requestId, entryId, normalizedRole));
        }
    }

    public PlayerItemOperationResult SetFlags(
        string requestId,
        string entryId,
        PlayerItemFlags flags,
        long expectedRevision)
    {
        string fingerprint = $"flags|{expectedRevision}|{entryId}|{(int)flags}";
        lock (sync)
        {
            if (!BeginOperation(requestId, fingerprint, out PlayerItemOperationResult? immediate)) return immediate!;
            if (expectedRevision != revision)
                return RejectAndRemember(requestId, fingerprint, PlayerItemOperationRejection.StaleRevision);
            const PlayerItemFlags valid = PlayerItemFlags.Favorite | PlayerItemFlags.Protected | PlayerItemFlags.Junk;
            if ((flags & ~valid) != 0
                || flags.HasFlag(PlayerItemFlags.Favorite) && flags.HasFlag(PlayerItemFlags.Junk))
            {
                return RejectAndRemember(requestId, fingerprint, PlayerItemOperationRejection.InvalidRequest);
            }
            if (!byId.TryGetValue(entryId, out StoredEntry? entry))
                return RejectAndRemember(requestId, fingerprint, PlayerItemOperationRejection.EntryMissing);
            if (ReservedQuantity(entryId) != 0)
                return RejectAndRemember(requestId, fingerprint, PlayerItemOperationRejection.EntryReserved);

            entry.Flags = flags;
            AdvanceRevision();
            return Remember(requestId, fingerprint,
                PlayerItemOperationResult.Accepted(revision, requestId, entryId));
        }
    }

    public PlayerItemOperationResult MoveToRecovery(
        string requestId,
        string entryId,
        PlayerItemLocation destination,
        PlayerItemRecoveryReason reason,
        string source,
        long expectedRevision)
    {
        string fingerprint = $"recovery|{expectedRevision}|{entryId}|{destination}|{reason}|{source}";
        lock (sync)
        {
            if (!BeginOperation(requestId, fingerprint, out PlayerItemOperationResult? immediate)) return immediate!;
            if (expectedRevision != revision)
                return RejectAndRemember(requestId, fingerprint, PlayerItemOperationRejection.StaleRevision);
            if (destination is not (PlayerItemLocation.Overflow or PlayerItemLocation.Unresolved)
                || string.IsNullOrWhiteSpace(source))
            {
                return RejectAndRemember(requestId, fingerprint, PlayerItemOperationRejection.InvalidRequest);
            }
            if (!byId.TryGetValue(entryId, out StoredEntry? entry))
                return RejectAndRemember(requestId, fingerprint, PlayerItemOperationRejection.EntryMissing);
            if (entry.Location is not (PlayerItemLocation.Stored or PlayerItemLocation.Equipped))
                return RejectAndRemember(requestId, fingerprint, PlayerItemOperationRejection.EntryUnavailable);
            if (ReservedQuantity(entryId) != 0)
                return RejectAndRemember(requestId, fingerprint, PlayerItemOperationRejection.EntryReserved);

            MoveLocation(entry, destination);
            entry.RecoveryReason = reason;
            entry.RecoverySource = source.Trim();
            AdvanceRevision();
            return Remember(requestId, fingerprint,
                PlayerItemOperationResult.Accepted(revision, requestId, entryId));
        }
    }

    public PlayerItemOperationResult Recover(string requestId, string entryId, long expectedRevision)
    {
        string fingerprint = $"recover|{expectedRevision}|{entryId}";
        lock (sync)
        {
            if (!BeginOperation(requestId, fingerprint, out PlayerItemOperationResult? immediate)) return immediate!;
            if (expectedRevision != revision)
                return RejectAndRemember(requestId, fingerprint, PlayerItemOperationRejection.StaleRevision);
            if (!byId.TryGetValue(entryId, out StoredEntry? entry))
                return RejectAndRemember(requestId, fingerprint, PlayerItemOperationRejection.EntryMissing);
            if (entry.Location is not (PlayerItemLocation.Overflow or PlayerItemLocation.Unresolved))
                return RejectAndRemember(requestId, fingerprint, PlayerItemOperationRejection.EntryUnavailable);
            StoredEntry? aggregateTarget = entry.Prototype.Kind == PlayerItemStorageKind.Unique
                ? null
                : ExactMatch(entry.Key, entry.Prototype);
            if (aggregateTarget != null)
            {
                long merged;
                try
                {
                    merged = checked(aggregateTarget.Quantity + entry.Quantity);
                }
                catch (OverflowException)
                {
                    return RejectAndRemember(requestId, fingerprint, PlayerItemOperationRejection.EntryUnavailable);
                }
                if (merged > capacity.AggregateQuantityPerEntry)
                    return RejectAndRemember(requestId, fingerprint, PlayerItemOperationRejection.EntryUnavailable);
                aggregateTarget.Quantity = merged;
                Remove(entry);
                AdvanceRevision();
                return Remember(requestId, fingerprint,
                    PlayerItemOperationResult.Accepted(revision, requestId, aggregateTarget.EntryId));
            }
            if (!HasCapacityToRecover(entry))
                return RejectAndRemember(requestId, fingerprint, PlayerItemOperationRejection.EntryUnavailable);

            MoveLocation(entry, PlayerItemLocation.Stored);
            entry.RecoveryReason = default;
            entry.RecoverySource = "";
            AdvanceRevision();
            return Remember(requestId, fingerprint,
                PlayerItemOperationResult.Accepted(revision, requestId, entryId));
        }
    }

    public PlayerItemMutationResult ImportRecovery(
        string requestId,
        PlayerItemPrototype prototype,
        long quantity,
        PlayerItemLocation destination,
        PlayerItemRecoveryReason reason,
        string source,
        long expectedRevision)
    {
        string fingerprint = $"import-recovery|{expectedRevision}|{quantity}|{destination}|{reason}|{source}|{PrototypeFingerprint(prototype)}";
        lock (sync)
        {
            if (!TryBeginRequest(requestId, fingerprint, out PlayerItemMutationResult? replay, out bool conflict))
                return conflict
                    ? PlayerItemMutationResult.Rejected(revision, PlayerItemMutationRejection.RequestIdConflict, requestId)
                    : PlayerItemMutationResult.Rejected(revision, PlayerItemMutationRejection.InvalidRequest, requestId);
            if (replay != null) return replay;
            if (expectedRevision != revision)
                return Remember(requestId, fingerprint, PlayerItemMutationResult.Rejected(
                    revision, PlayerItemMutationRejection.StaleRevision, requestId));
            if (!IsValid(prototype, quantity)
                || destination is not (PlayerItemLocation.Overflow or PlayerItemLocation.Unresolved)
                || string.IsNullOrWhiteSpace(source)
                || prototype.Kind != PlayerItemStorageKind.Unique
                    && quantity > capacity.AggregateQuantityPerEntry)
            {
                return Remember(requestId, fingerprint, PlayerItemMutationResult.Rejected(
                    revision, PlayerItemMutationRejection.InvalidRequest, requestId));
            }

            StoredEntry entry = AddNew(prototype, quantity);
            MoveLocation(entry, destination);
            entry.RecoveryReason = reason;
            entry.RecoverySource = source.Trim();
            AdvanceRevision();
            return Remember(requestId, fingerprint, new PlayerItemMutationResult(
                true,
                revision,
                entry.EntryId,
                entry.Quantity,
                PlayerItemMutationRejection.None,
                requestId));
        }
    }

    public PlayerItemOperationResult Destroy(
        string requestId,
        string entryId,
        long quantity,
        bool confirmed,
        long expectedRevision)
    {
        string fingerprint = $"destroy|{expectedRevision}|{entryId}|{quantity}|{confirmed}";
        lock (sync)
        {
            if (!BeginOperation(requestId, fingerprint, out PlayerItemOperationResult? immediate)) return immediate!;
            if (expectedRevision != revision)
                return RejectAndRemember(requestId, fingerprint, PlayerItemOperationRejection.StaleRevision);
            if (!confirmed)
                return RejectAndRemember(requestId, fingerprint, PlayerItemOperationRejection.ConfirmationRequired);
            if (!byId.TryGetValue(entryId, out StoredEntry? entry))
                return RejectAndRemember(requestId, fingerprint, PlayerItemOperationRejection.EntryMissing);
            if (entry.Location != PlayerItemLocation.Stored)
                return RejectAndRemember(requestId, fingerprint, PlayerItemOperationRejection.EntryUnavailable);
            if (entry.Flags.HasFlag(PlayerItemFlags.Protected))
                return RejectAndRemember(requestId, fingerprint, PlayerItemOperationRejection.EntryProtected);
            if (ReservedQuantity(entryId) != 0)
                return RejectAndRemember(requestId, fingerprint, PlayerItemOperationRejection.EntryReserved);
            if (quantity <= 0 || quantity > entry.Quantity
                || entry.Prototype.Kind == PlayerItemStorageKind.Unique && quantity != 1)
            {
                return RejectAndRemember(requestId, fingerprint, PlayerItemOperationRejection.InsufficientQuantity);
            }

            entry.Quantity -= quantity;
            long remaining = entry.Quantity;
            if (remaining == 0) Remove(entry);
            AdvanceRevision();
            return Remember(requestId, fingerprint,
                PlayerItemOperationResult.Accepted(revision, requestId, entryId));
        }
    }

    public PlayerEquipmentSnapshot Equipment()
    {
        lock (sync)
        {
            EquippedPlayerItemRecord[] entries = equipmentByRole
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new EquippedPlayerItemRecord(pair.Key, pair.Value))
                .ToArray();
            return new PlayerEquipmentSnapshot(revision, entries);
        }
    }

    public PlayerRecoverySnapshot Recovery()
    {
        lock (sync)
        {
            PlayerRecoverySummary[] entries = byId.Values
                .Where(entry => entry.Location is PlayerItemLocation.Overflow or PlayerItemLocation.Unresolved)
                .OrderBy(entry => entry.EntryId, StringComparer.OrdinalIgnoreCase)
                .Select(entry => new PlayerRecoverySummary(
                    entry.EntryId,
                    entry.Location,
                    entry.RecoveryReason,
                    entry.RecoverySource))
                .ToArray();
            return new PlayerRecoverySnapshot(revision, entries);
        }
    }

    private bool BeginOperation(
        string requestId,
        string fingerprint,
        out PlayerItemOperationResult? immediate)
    {
        if (TryBeginRequest(requestId, fingerprint, out PlayerItemOperationResult? replay, out bool conflict))
        {
            immediate = replay;
            return replay == null;
        }
        immediate = PlayerItemOperationResult.Rejected(
            revision,
            requestId,
            conflict ? PlayerItemOperationRejection.RequestIdConflict : PlayerItemOperationRejection.InvalidRequest);
        return false;
    }

    private PlayerItemOperationResult RejectAndRemember(
        string requestId,
        string fingerprint,
        PlayerItemOperationRejection rejection) =>
        Remember(requestId, fingerprint,
            PlayerItemOperationResult.Rejected(revision, requestId, rejection));

    private bool HasCapacityToRecover(StoredEntry entry)
    {
        return entry.Prototype.Kind == PlayerItemStorageKind.Unique
            ? ActiveCount(PlayerItemStorageKind.Unique) < capacity.UniqueEntryLimit
            : ActiveAggregateCount() < capacity.AggregateEntryLimit;
    }

    private void ReleaseReservation(PendingReservation reservation)
    {
        reservations.Remove(reservation.ReservationId);
        foreach (PlayerItemAmount amount in reservation.Amounts)
        {
            long remaining = ReservedQuantity(amount.EntryId) - amount.Quantity;
            if (remaining == 0) reservedByEntry.Remove(amount.EntryId);
            else reservedByEntry[amount.EntryId] = remaining;
        }
    }

    private bool TryBeginRequest<T>(
        string requestId,
        string fingerprint,
        out T? replay,
        out bool conflict)
        where T : class
    {
        replay = null;
        conflict = false;
        if (string.IsNullOrWhiteSpace(requestId) || requestId.Length > 128) return false;
        if (!requestCache.TryGetValue(requestId, out CachedRequest? cached)) return true;
        if (!string.Equals(cached.Fingerprint, fingerprint, StringComparison.Ordinal)
            || cached.Result is not T typed)
        {
            conflict = true;
            return false;
        }
        replay = typed;
        return true;
    }

    private T Remember<T>(string requestId, string fingerprint, T result) where T : class
    {
        RememberRequest(requestId, fingerprint, result);
        return result;
    }

    private void RememberRequest(string requestId, string fingerprint, object result)
    {
        requestCache[requestId] = new CachedRequest(fingerprint, result);
        persistenceStamp = checked(persistenceStamp + 1);
    }

    private static bool TryNormalizeAmounts(PlayerItemAmount[]? amounts, out PlayerItemAmount[] normalized)
    {
        normalized = Array.Empty<PlayerItemAmount>();
        if (amounts == null || amounts.Length == 0 || amounts.Length > 64) return false;
        var totals = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (PlayerItemAmount amount in amounts)
            {
                if (string.IsNullOrWhiteSpace(amount.EntryId) || amount.Quantity <= 0) return false;
                totals[amount.EntryId] = checked(
                    (totals.TryGetValue(amount.EntryId, out long current) ? current : 0) + amount.Quantity);
            }
        }
        catch (OverflowException)
        {
            return false;
        }
        normalized = totals
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => new PlayerItemAmount(pair.Key, pair.Value))
            .ToArray();
        return true;
    }

    private static string ReservationFingerprint(string operation, long revision, PlayerItemAmount[]? amounts)
    {
        if (!TryNormalizeAmounts(amounts, out PlayerItemAmount[] normalized))
            return $"{operation}|{revision}|invalid";
        return $"{operation}|{revision}|{string.Join(';', normalized.Select(amount => $"{amount.EntryId}:{amount.Quantity}"))}";
    }

    private static string EquipFingerprint(
        string entryId,
        string roleCode,
        long expectedRevision) =>
        $"equip|{expectedRevision}|{entryId}|{roleCode}";

    private static string PrototypeFingerprint(PlayerItemPrototype? prototype)
    {
        if (prototype?.ExactState == null) return "invalid";
        byte[] hash = SHA256.HashData(prototype.ExactState);
        return string.Join('|',
            prototype.Kind,
            prototype.AssetKind,
            prototype.AssetCode?.Trim().ToLowerInvariant(),
            prototype.Category?.Trim().ToLowerInvariant(),
            Convert.ToHexString(hash),
            string.Join(',', (prototype.EmbeddedReferences ?? Array.Empty<CanonicalCollectibleReference>())
                .Select(ReferenceFingerprint)
                .OrderBy(value => value, StringComparer.Ordinal)));
    }

    private static string ReferenceFingerprint(CanonicalCollectibleReference? reference) =>
        reference == null
            ? "invalid:null"
            : $"{reference.AssetKind}:{reference.SourceRegistryId}:{reference.AssetCode?.Trim().ToLowerInvariant() ?? "invalid"}";

    private sealed record CachedRequest(string Fingerprint, object Result);
    private sealed record PendingReservation(string ReservationId, PlayerItemAmount[] Amounts);
}
