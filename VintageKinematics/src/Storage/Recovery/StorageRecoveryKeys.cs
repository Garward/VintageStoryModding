namespace VintageKinematics.Storage.Recovery
{
    internal static class StorageRecoveryKeys
    {
        public const string Index = "vintagekinematics:storage:index";
        private const string IndexSlotPrefix = "vintagekinematics:storage:index-slot:";
        private const string WarehousePrefix = "vintagekinematics:storage:warehouse:";
        private const string WarehouseVersionSeparator = ":version:";
        private const string WarehouseSlotSeparator = ":slot:";

        /// <summary>
        /// Legacy schema-one key. Loading retains this fallback so existing saves migrate safely.
        /// </summary>
        public static string Warehouse(string warehouseId)
        {
            return WarehousePrefix + StorageWarehouseId.Normalize(warehouseId);
        }

        /// <summary>
        /// Immutable record key referenced implicitly by the committed index metadata.
        /// </summary>
        public static string WarehouseVersion(StorageRecoveryIndexEntry entry)
        {
            if (entry == null) throw new System.ArgumentNullException(nameof(entry));
            return WarehouseVersion(entry.WarehouseId, entry.Revision, entry.ChecksumHex);
        }

        public static string WarehouseVersion(StorageRecoveryRecord record)
        {
            if (record == null) throw new System.ArgumentNullException(nameof(record));
            return WarehouseVersion(record.WarehouseId, record.Revision, record.ChecksumHex);
        }

        public static string WarehouseSlot(string warehouseId, int slot)
        {
            if (slot != 0 && slot != 1)
            {
                throw new System.ArgumentOutOfRangeException(nameof(slot));
            }
            return Warehouse(warehouseId) + WarehouseSlotSeparator + slot;
        }

        private static string WarehouseVersion(string warehouseId, long revision, string checksumHex)
        {
            return Warehouse(warehouseId)
                + WarehouseVersionSeparator
                + revision.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + ":"
                + checksumHex;
        }

        public static string IndexSlot(int slot)
        {
            if (slot != 0 && slot != 1)
            {
                throw new System.ArgumentOutOfRangeException(nameof(slot));
            }
            return IndexSlotPrefix + slot;
        }
    }
}
