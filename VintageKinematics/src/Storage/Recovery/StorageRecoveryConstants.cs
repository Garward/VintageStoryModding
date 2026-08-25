namespace VintageKinematics.Storage.Recovery
{
    internal static class StorageRecoveryConstants
    {
        public const uint IndexMagic = 0x49524B56; // VKRI in little-endian byte order.
        public const uint RecordMagic = 0x57524B56; // VKRW in little-endian byte order.
        public const uint IndexCommitMagic = 0x43524B56; // VKRC in little-endian byte order.
        public const int IndexSchemaVersion = 2;
        public const int RecordSchemaVersion = 1;
        public const int IndexCommitSchemaVersion = 1;
        public const int MaxWarehouses = 4096;
        public const int MaxIndexBytes = 4 * 1024 * 1024;
        public const int MaxIndexCommitBytes = MaxIndexBytes + 64;
        public const int MaxSnapshotBytes = 64 * 1024 * 1024;
        public const int MaxRecordBytes = MaxSnapshotBytes + 256;
    }
}
