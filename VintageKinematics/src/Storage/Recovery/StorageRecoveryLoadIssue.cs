using System;

namespace VintageKinematics.Storage.Recovery
{
    /// <summary>
    /// Evidence retained when a recovery index or record cannot be trusted.
    /// </summary>
    public sealed class StorageRecoveryLoadIssue
    {
        private readonly byte[] rawBytes;

        public StorageRecoveryLoadIssueKind Kind { get; }
        public string WarehouseId { get; }
        public StorageRecoveryIndexEntry IndexEntry { get; }
        public StorageRecoveryRecord Record { get; }
        public byte[] RawBytes => (byte[])rawBytes.Clone();

        internal StorageRecoveryLoadIssue(
            StorageRecoveryLoadIssueKind kind,
            byte[] rawBytes,
            StorageRecoveryIndexEntry indexEntry = null,
            StorageRecoveryRecord record = null)
        {
            Kind = kind;
            IndexEntry = indexEntry;
            Record = record;
            WarehouseId = indexEntry?.WarehouseId ?? record?.WarehouseId;
            this.rawBytes = (byte[])(rawBytes?.Clone() ?? Array.Empty<byte>());
        }
    }
}
