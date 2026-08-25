namespace VintageKinematics.Storage.Recovery
{
    /// <summary>
    /// Names one independently committed full-snapshot mirror.
    /// </summary>
    internal sealed class StorageRecoveryKeyspace
    {
        public static readonly StorageRecoveryKeyspace Recovery = new StorageRecoveryKeyspace(
            "vintagekinematics:storage:index",
            "vintagekinematics:storage:index-slot:",
            "vintagekinematics:storage:warehouse:");

        public static readonly StorageRecoveryKeyspace Controller = new StorageRecoveryKeyspace(
            "vintagekinematics:storage:controller-index",
            "vintagekinematics:storage:controller-index-slot:",
            "vintagekinematics:storage:controller-warehouse:");

        private readonly string indexSlotPrefix;
        private readonly string warehousePrefix;

        public string LegacyIndex { get; }

        private StorageRecoveryKeyspace(
            string legacyIndex,
            string indexSlotPrefix,
            string warehousePrefix)
        {
            LegacyIndex = legacyIndex;
            this.indexSlotPrefix = indexSlotPrefix;
            this.warehousePrefix = warehousePrefix;
        }

        public string IndexSlot(int slot)
        {
            ValidateSlot(slot);
            return indexSlotPrefix + slot;
        }

        public string LegacyWarehouse(string warehouseId)
        {
            return warehousePrefix + StorageWarehouseId.Normalize(warehouseId);
        }

        public string WarehouseSlot(string warehouseId, int slot)
        {
            ValidateSlot(slot);
            return LegacyWarehouse(warehouseId) + ":slot:" + slot;
        }

        public string WarehouseVersion(StorageRecoveryIndexEntry entry)
        {
            return LegacyWarehouse(entry.WarehouseId)
                + ":version:"
                + entry.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + ":"
                + entry.ChecksumHex;
        }

        private static void ValidateSlot(int slot)
        {
            if (slot != 0 && slot != 1)
            {
                throw new System.ArgumentOutOfRangeException(nameof(slot));
            }
        }
    }
}
