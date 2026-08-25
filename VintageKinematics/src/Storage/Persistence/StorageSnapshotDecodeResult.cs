using System.Collections.Generic;

namespace VintageKinematics.Storage.Persistence
{
    internal sealed class StorageSnapshotDecodeResult
    {
        public int SchemaVersion { get; set; }
        public long NextEntryId { get; set; }
        public List<PersistedStorageEntry> Entries { get; } = new();
        public List<QuarantinedStorageEntry> QuarantinedEntries { get; } = new();
    }
}
