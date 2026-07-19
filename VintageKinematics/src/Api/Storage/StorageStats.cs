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
        public readonly bool IsLocked;
        public readonly bool IsOverCapacity;
        public readonly int ImportRate;
        public readonly int ExportRate;

        public StorageStats(
            long storedItems,
            long itemCapacity,
            int entryCount,
            int typeCapacity,
            bool isLocked,
            bool isOverCapacity,
            int importRate,
            int exportRate)
        {
            StoredItems = storedItems;
            ItemCapacity = itemCapacity;
            EntryCount = entryCount;
            TypeCapacity = typeCapacity;
            IsLocked = isLocked;
            IsOverCapacity = isOverCapacity;
            ImportRate = importRate;
            ExportRate = exportRate;
        }

        public long FreeCapacity => ItemCapacity - StoredItems;
        public bool IsFull => ItemCapacity > 0 && StoredItems >= ItemCapacity;
    }
}
