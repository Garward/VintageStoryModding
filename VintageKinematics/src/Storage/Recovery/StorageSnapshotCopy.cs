using System;

namespace VintageKinematics.Storage.Recovery
{
    /// <summary>
    /// One retained controller or registry copy and its validation state.
    /// </summary>
    public sealed class StorageSnapshotCopy
    {
        private readonly byte[] rawBytes;

        public StorageSnapshotCopyState State { get; }
        public StorageRecoveryRecord Record { get; }
        public StorageRecoveryIndexEntry Header { get; }
        public byte[] RawBytes => (byte[])rawBytes.Clone();

        private StorageSnapshotCopy(
            StorageSnapshotCopyState state,
            StorageRecoveryRecord record,
            byte[] rawBytes,
            StorageRecoveryIndexEntry header)
        {
            State = state;
            Record = record;
            Header = header;
            this.rawBytes = (byte[])(rawBytes?.Clone() ?? Array.Empty<byte>());
        }

        public static StorageSnapshotCopy Missing()
        {
            return new StorageSnapshotCopy(StorageSnapshotCopyState.Missing, null, null, null);
        }

        public static StorageSnapshotCopy FromRecord(
            StorageRecoveryRecord record,
            byte[] rawBytes = null)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            StorageSnapshotCopyState state = record.HasValidChecksum
                ? StorageSnapshotCopyState.Valid
                : StorageSnapshotCopyState.Invalid;
            return new StorageSnapshotCopy(state, record, rawBytes, null);
        }

        public static StorageSnapshotCopy Invalid(
            byte[] rawBytes,
            StorageRecoveryRecord record = null,
            StorageRecoveryIndexEntry header = null)
        {
            return new StorageSnapshotCopy(
                StorageSnapshotCopyState.Invalid,
                record,
                rawBytes,
                header);
        }
    }
}
