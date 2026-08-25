using System;

namespace VintageKinematics.Storage.Index
{
    /// <summary>
    /// Capacity limits supplied by the warehouse structure layer.
    /// </summary>
    public readonly struct StorageIndexLimits
    {
        public readonly long ItemCapacity;
        public readonly int TypeCapacity;
        public readonly int MaxEntries;

        public StorageIndexLimits(long itemCapacity, int typeCapacity = 0, int maxEntries = 4096)
        {
            if (itemCapacity < 0) throw new ArgumentOutOfRangeException(nameof(itemCapacity));
            if (typeCapacity < 0) throw new ArgumentOutOfRangeException(nameof(typeCapacity));
            if (maxEntries <= 0) throw new ArgumentOutOfRangeException(nameof(maxEntries));

            ItemCapacity = itemCapacity;
            TypeCapacity = typeCapacity;
            MaxEntries = maxEntries;
        }
    }
}
