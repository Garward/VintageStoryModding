using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using VintageKinematics.Api.Storage;

namespace VintageKinematics.Storage.Index
{
    public sealed partial class KineticStorageIndex
    {
        internal long NextEntryId => nextEntryId;

        internal void RestoreResolvedEntry(long entryId, ItemStack exemplar, long quantity)
        {
            if (entryId <= 0) throw new ArgumentOutOfRangeException(nameof(entryId));
            if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));
            if (entriesById.ContainsKey(entryId)) throw new InvalidOperationException("Duplicate storage entry id.");

            ItemKey key = keyFactory(exemplar);
            InternalStoredEntry entry = new InternalStoredEntry(entryId, key, exemplar, quantity);
            if (!collisionBuckets.TryGetValue(key, out List<InternalStoredEntry> bucket))
            {
                bucket = new List<InternalStoredEntry>();
                collisionBuckets.Add(key, bucket);
            }

            bucket.Add(entry);
            entriesById.Add(entryId, entry);
            resolvedItems = checked(resolvedItems + quantity);
        }

        internal void RestoreUnresolvedReservations(long quantity, int entryCount)
        {
            if (quantity < 0) throw new ArgumentOutOfRangeException(nameof(quantity));
            if (entryCount < 0) throw new ArgumentOutOfRangeException(nameof(entryCount));

            _ = checked(resolvedItems + quantity);
            _ = checked(entriesById.Count + entryCount);
            unresolvedItems = quantity;
            unresolvedEntries = entryCount;
        }

        internal void RestoreNextEntryId(long restoredNextEntryId)
        {
            if (restoredNextEntryId <= 0) throw new ArgumentOutOfRangeException(nameof(restoredNextEntryId));
            nextEntryId = restoredNextEntryId;
        }
    }
}
