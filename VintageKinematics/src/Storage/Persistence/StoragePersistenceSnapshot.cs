using System;
using System.Collections.Generic;

namespace VintageKinematics.Storage.Persistence
{
    public sealed class StoragePersistenceSnapshot
    {
        private readonly IReadOnlyList<PersistedStorageEntry> entries;

        public int SchemaVersion { get; }
        public long NextEntryId { get; }
        public IReadOnlyList<PersistedStorageEntry> Entries => entries;

        public StoragePersistenceSnapshot(
            long nextEntryId,
            IReadOnlyList<PersistedStorageEntry> entries,
            int schemaVersion = StoragePersistenceConstants.SchemaVersion)
        {
            if (nextEntryId <= 0) throw new ArgumentOutOfRangeException(nameof(nextEntryId));
            SchemaVersion = schemaVersion;
            NextEntryId = nextEntryId;
            if (entries == null)
            {
                this.entries = Array.Empty<PersistedStorageEntry>();
            }
            else
            {
                PersistedStorageEntry[] copy = new PersistedStorageEntry[entries.Count];
                for (int i = 0; i < entries.Count; i++) copy[i] = entries[i];
                this.entries = copy;
            }
        }
    }
}
