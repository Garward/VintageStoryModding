using System;
using Vintagestory.API.Common;
using VintageKinematics.Api.Storage;

namespace VintageKinematics.Storage.Index
{
    /// <summary>
    /// Mutable index-owned entry. It never escapes through the public API.
    /// </summary>
    internal sealed class InternalStoredEntry
    {
        public long EntryId { get; }
        public ItemKey Key { get; }
        public ItemStack Exemplar { get; }
        public long Quantity { get; private set; }

        public InternalStoredEntry(long entryId, ItemKey key, ItemStack exemplar, long quantity)
        {
            if (entryId <= 0) throw new ArgumentOutOfRangeException(nameof(entryId));
            if (exemplar?.Collectible == null) throw new ArgumentNullException(nameof(exemplar));
            if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));

            EntryId = entryId;
            Key = key;
            Exemplar = exemplar.Clone();
            Exemplar.StackSize = 1;
            Quantity = quantity;
        }

        public void Increase(int quantity)
        {
            if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));
            Quantity = checked(Quantity + quantity);
        }

        public void Decrease(int quantity)
        {
            if (quantity <= 0 || quantity > Quantity) throw new ArgumentOutOfRangeException(nameof(quantity));
            Quantity -= quantity;
        }

        public StoredEntry Snapshot()
        {
            return new StoredEntry(EntryId, Key, Exemplar, Quantity);
        }
    }
}
