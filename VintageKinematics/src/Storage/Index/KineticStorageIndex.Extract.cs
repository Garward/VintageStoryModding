using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using VintageKinematics.Api.Storage;

namespace VintageKinematics.Storage.Index
{
    public sealed partial class KineticStorageIndex
    {
        public StorageTransferResult TryExtract(long entryId, int quantity, out ItemStack extracted)
        {
            extracted = null;
            if (quantity <= 0)
            {
                return StorageTransferResult.Fail(StorageTransferStatus.InvalidQuantity);
            }
            if (!entriesById.TryGetValue(entryId, out InternalStoredEntry entry))
            {
                return StorageTransferResult.Fail(StorageTransferStatus.NotFound);
            }

            int maxStackSize = entry.Exemplar.Collectible.MaxStackSize;
            if (maxStackSize <= 0)
            {
                return StorageTransferResult.Fail(StorageTransferStatus.Corrupt);
            }

            int moved = (int)Math.Min(Math.Min((long)quantity, maxStackSize), entry.Quantity);
            extracted = VKStorageKeys.ExtractClone(entry.Exemplar, moved);
            entry.Decrease(moved);
            resolvedItems = checked(resolvedItems - moved);

            if (entry.Quantity == 0) RemoveEntry(entry);
            return StorageTransferResult.Ok(moved);
        }

        private void RemoveEntry(InternalStoredEntry entry)
        {
            entriesById.Remove(entry.EntryId);
            List<InternalStoredEntry> bucket = collisionBuckets[entry.Key];
            bucket.Remove(entry);
            if (bucket.Count == 0) collisionBuckets.Remove(entry.Key);
        }
    }
}
