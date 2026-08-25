using System;

namespace VintageKinematics.Storage.Recovery
{
    /// <summary>
    /// Immutable recovery mirror for one warehouse snapshot.
    /// </summary>
    public sealed class StorageRecoveryRecord
    {
        private readonly byte[] snapshotBytes;
        private readonly byte[] checksum;

        public string WarehouseId { get; }
        public StorageControllerLocation Controller { get; }
        public long Revision { get; }
        public bool IsTombstone { get; }
        public byte[] SnapshotBytes => (byte[])snapshotBytes.Clone();
        public byte[] Checksum => (byte[])checksum.Clone();
        public bool HasValidChecksum => StorageRecoveryChecksum.Matches(snapshotBytes, checksum);
        public string ChecksumHex => Convert.ToHexString(checksum).ToLowerInvariant();

        private StorageRecoveryRecord(
            string warehouseId,
            StorageControllerLocation controller,
            long revision,
            byte[] snapshotBytes,
            byte[] checksum,
            bool isTombstone)
        {
            WarehouseId = StorageWarehouseId.Normalize(warehouseId);
            if (revision <= 0) throw new ArgumentOutOfRangeException(nameof(revision));
            if (snapshotBytes == null) throw new ArgumentNullException(nameof(snapshotBytes));
            if (!isTombstone && snapshotBytes.Length == 0)
            {
                throw new ArgumentException("A live recovery record requires snapshot bytes.", nameof(snapshotBytes));
            }
            if (checksum == null) throw new ArgumentNullException(nameof(checksum));
            if (checksum.Length != StorageRecoveryChecksum.Size)
            {
                throw new ArgumentException("A recovery checksum must contain 32 bytes.", nameof(checksum));
            }

            Controller = controller;
            Revision = revision;
            this.snapshotBytes = (byte[])snapshotBytes.Clone();
            this.checksum = (byte[])checksum.Clone();
            IsTombstone = isTombstone;
        }

        public static StorageRecoveryRecord Create(
            string warehouseId,
            StorageControllerLocation controller,
            long revision,
            byte[] snapshotBytes,
            bool isTombstone = false)
        {
            byte[] computedChecksum = StorageRecoveryChecksum.Compute(snapshotBytes);
            return new StorageRecoveryRecord(
                warehouseId,
                controller,
                revision,
                snapshotBytes,
                computedChecksum,
                isTombstone);
        }

        internal static StorageRecoveryRecord Restore(
            string warehouseId,
            StorageControllerLocation controller,
            long revision,
            byte[] snapshotBytes,
            byte[] checksum,
            bool isTombstone)
        {
            return new StorageRecoveryRecord(
                warehouseId,
                controller,
                revision,
                snapshotBytes,
                checksum,
                isTombstone);
        }

        internal bool HasChecksum(byte[] expectedChecksum)
        {
            return StorageRecoveryBytes.Equal(checksum, expectedChecksum);
        }

        internal bool IsEquivalentTo(StorageRecoveryRecord other)
        {
            return other != null
                && WarehouseId == other.WarehouseId
                && Controller == other.Controller
                && Revision == other.Revision
                && IsTombstone == other.IsTombstone
                && StorageRecoveryBytes.Equal(checksum, other.checksum)
                && StorageRecoveryBytes.Equal(snapshotBytes, other.snapshotBytes);
        }

    }
}
