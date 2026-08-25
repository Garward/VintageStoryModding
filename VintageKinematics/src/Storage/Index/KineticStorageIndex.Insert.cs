using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using VintageKinematics.Api.Storage;
using VintageKinematics.Storage.Acceptance;

namespace VintageKinematics.Storage.Index
{
    public sealed partial class KineticStorageIndex
    {
        public StorageTransferResult TryInsert(
            IWorldAccessor world,
            ItemStack stack,
            out ItemStack remainder,
            int maxQuantity = int.MaxValue)
        {
            remainder = CloneWithQuantity(stack, stack?.StackSize ?? 0);
            if (stack?.Collectible == null || stack.StackSize <= 0)
            {
                return StorageTransferResult.Fail(StorageTransferStatus.EmptyInput);
            }
            if (maxQuantity <= 0)
            {
                return StorageTransferResult.Fail(StorageTransferStatus.InvalidQuantity);
            }

            int requested = Math.Min(stack.StackSize, maxQuantity);
            StorageAcceptanceResult acceptance = acceptanceValidator.Validate(world, stack, requested);
            if (!acceptance.Accepted)
            {
                return StorageTransferResult.Fail(StorageTransferStatus.ItemRejected, acceptance.MessageLangCode);
            }

            ItemKey key = keyFactory(stack);
            InternalStoredEntry existing = FindExactEntry(key, stack);
            long freeCapacity = StoredItems >= limits.ItemCapacity
                ? 0
                : limits.ItemCapacity - StoredItems;
            int moved = (int)Math.Min(requested, freeCapacity);
            if (moved <= 0)
            {
                return StorageTransferResult.Fail(StorageTransferStatus.Full);
            }
            if (existing == null && ReachedEntryLimit())
            {
                return StorageTransferResult.Fail(StorageTransferStatus.TypeLimitReached);
            }

            try
            {
                _ = checked(resolvedItems + moved);
                if (existing == null) _ = checked(nextEntryId + 1);
                else _ = checked(existing.Quantity + moved);
            }
            catch (OverflowException)
            {
                return StorageTransferResult.Fail(StorageTransferStatus.QuantityOverflow);
            }

            if (existing == null) AddEntry(key, stack, moved);
            else existing.Increase(moved);
            resolvedItems = checked(resolvedItems + moved);

            remainder = CloneWithQuantity(stack, stack.StackSize - moved);
            return StorageTransferResult.Ok(moved);
        }

        private bool ReachedEntryLimit()
        {
            return EntryCount >= limits.MaxEntries
                || (limits.TypeCapacity > 0 && EntryCount >= limits.TypeCapacity);
        }

        private InternalStoredEntry FindExactEntry(ItemKey key, ItemStack stack)
        {
            if (!collisionBuckets.TryGetValue(key, out List<InternalStoredEntry> bucket)) return null;

            foreach (InternalStoredEntry candidate in bucket)
            {
                if (exactMatch(candidate.Exemplar, stack)) return candidate;
            }
            return null;
        }

        private void AddEntry(ItemKey key, ItemStack stack, int quantity)
        {
            long entryId = nextEntryId;
            nextEntryId = checked(nextEntryId + 1);

            InternalStoredEntry entry = new InternalStoredEntry(entryId, key, stack, quantity);
            if (!collisionBuckets.TryGetValue(key, out List<InternalStoredEntry> bucket))
            {
                bucket = new List<InternalStoredEntry>();
                collisionBuckets.Add(key, bucket);
            }

            bucket.Add(entry);
            entriesById.Add(entryId, entry);
        }

        private static ItemStack CloneWithQuantity(ItemStack stack, int quantity)
        {
            if (stack == null || quantity <= 0) return null;
            ItemStack clone = stack.Clone();
            clone.StackSize = quantity;
            return clone;
        }
    }
}
