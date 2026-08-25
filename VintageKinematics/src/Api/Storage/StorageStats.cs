using System;

namespace VintageKinematics.Api.Storage
{
    /// <summary>
    /// Cheap summary for tooltips, GUI headers, and logic sensors.
    /// </summary>
    public readonly struct StorageStats
    {
        public readonly long StoredItems;
        public readonly long ItemCapacity;
        public readonly int EntryCount;
        public readonly int TypeCapacity;
        public readonly StorageState State;
        public readonly int ImportRate;
        public readonly int ExportRate;
        public readonly bool PowerRequired;
        public readonly bool Powered;

        public StorageStats(
            long storedItems,
            long itemCapacity,
            int entryCount,
            int typeCapacity,
            StorageState state,
            int importRate,
            int exportRate,
            bool powerRequired = false,
            bool powered = true)
        {
            if (storedItems < 0) throw new ArgumentOutOfRangeException(nameof(storedItems));
            if (itemCapacity < 0) throw new ArgumentOutOfRangeException(nameof(itemCapacity));
            if (entryCount < 0) throw new ArgumentOutOfRangeException(nameof(entryCount));
            if (typeCapacity < 0) throw new ArgumentOutOfRangeException(nameof(typeCapacity));

            StoredItems = storedItems;
            ItemCapacity = itemCapacity;
            EntryCount = entryCount;
            TypeCapacity = typeCapacity;
            State = state;
            ImportRate = importRate;
            ExportRate = exportRate;
            PowerRequired = powerRequired;
            Powered = powered;
        }

        public bool IsLocked => State != StorageState.Online && State != StorageState.OverCapacity;
        public bool IsOverCapacity => State == StorageState.OverCapacity;
        public long FreeCapacity => StoredItems >= ItemCapacity ? 0 : ItemCapacity - StoredItems;
        public bool IsFull => ItemCapacity > 0 && StoredItems >= ItemCapacity;
        public bool IsOperational => !PowerRequired || Powered;
    }
}
