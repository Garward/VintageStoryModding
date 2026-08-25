using System;

namespace VintageKinematics.Storage.Recovery
{
    /// <summary>
    /// Compact index metadata used to locate and compare a warehouse recovery record.
    /// </summary>
    public sealed class StorageRecoveryIndexEntry
    {
        private readonly byte[] checksum;

        public string WarehouseId { get; }
        public StorageControllerLocation Controller { get; }
        public long Revision { get; }
        public bool IsTombstone { get; }
        public int RecordSlot { get; }
        public byte[] Checksum => (byte[])checksum.Clone();
        public string ChecksumHex => Convert.ToHexString(checksum).ToLowerInvariant();

        public StorageRecoveryIndexEntry(StorageRecoveryRecord record, int recordSlot = -1)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));

            WarehouseId = record.WarehouseId;
            Controller = record.Controller;
            Revision = record.Revision;
            IsTombstone = record.IsTombstone;
            RecordSlot = ValidateRecordSlot(recordSlot);
            checksum = record.Checksum;
        }

        internal StorageRecoveryIndexEntry(
            string warehouseId,
            StorageControllerLocation controller,
            long revision,
            bool isTombstone,
            byte[] checksum,
            int recordSlot = -1)
        {
            WarehouseId = StorageWarehouseId.Normalize(warehouseId);
            if (revision <= 0) throw new ArgumentOutOfRangeException(nameof(revision));
            if (checksum == null) throw new ArgumentNullException(nameof(checksum));
            if (checksum.Length != StorageRecoveryChecksum.Size)
            {
                throw new ArgumentException("A recovery checksum must contain 32 bytes.", nameof(checksum));
            }

            Controller = controller;
            Revision = revision;
            IsTombstone = isTombstone;
            RecordSlot = ValidateRecordSlot(recordSlot);
            this.checksum = (byte[])checksum.Clone();
        }

        internal StorageRecoveryIndexEntry WithRecordSlot(int recordSlot)
        {
            return new StorageRecoveryIndexEntry(
                WarehouseId,
                Controller,
                Revision,
                IsTombstone,
                checksum,
                recordSlot);
        }

        public bool Matches(StorageRecoveryRecord record)
        {
            return record != null
                && WarehouseId == record.WarehouseId
                && Controller == record.Controller
                && Revision == record.Revision
                && IsTombstone == record.IsTombstone
                && record.HasChecksum(checksum);
        }

        private static int ValidateRecordSlot(int recordSlot)
        {
            if (recordSlot < -1 || recordSlot > 1)
            {
                throw new ArgumentOutOfRangeException(nameof(recordSlot));
            }
            return recordSlot;
        }
    }
}
