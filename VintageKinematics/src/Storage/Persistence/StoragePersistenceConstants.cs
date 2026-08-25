namespace VintageKinematics.Storage.Persistence
{
    internal static class StoragePersistenceConstants
    {
        public const uint SnapshotMagic = 0x54534B56; // VKST in little-endian byte order.
        public const int SchemaVersion = 1;
        public const int ChecksumSize = 32;
        public const int MaxCodeBytes = 1024;
        public const int MaxAttributeBytes = 16 * 1024 * 1024;
        public const int MaxRecordBytes = MaxAttributeBytes + MaxCodeBytes + 128;
        public const int MaxSnapshotBytes = 64 * 1024 * 1024;
        public const int MaxSnapshotEntries = 4096;
    }
}
