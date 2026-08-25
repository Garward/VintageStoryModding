using System;
using System.Collections.Generic;

namespace VintageKinematics.Storage.Recovery
{
    /// <summary>
    /// Immutable registry state captured for one save attempt.
    /// </summary>
    public sealed class StorageRecoverySaveBatch
    {
        private readonly IReadOnlyList<StorageRecoveryIndexEntry> indexEntries;
        private readonly IReadOnlyList<StorageRecoveryRecord> dirtyRecords;

        public long Generation { get; }
        public bool RequiresIndexWrite { get; }
        public IReadOnlyList<StorageRecoveryIndexEntry> IndexEntries => indexEntries;
        public IReadOnlyList<StorageRecoveryRecord> DirtyRecords => dirtyRecords;
        public bool HasChanges => RequiresIndexWrite || dirtyRecords.Count > 0;

        internal StorageRecoverySaveBatch(
            long generation,
            bool requiresIndexWrite,
            IReadOnlyList<StorageRecoveryIndexEntry> indexEntries,
            IReadOnlyList<StorageRecoveryRecord> dirtyRecords)
        {
            Generation = generation;
            RequiresIndexWrite = requiresIndexWrite;
            this.indexEntries = indexEntries ?? Array.Empty<StorageRecoveryIndexEntry>();
            this.dirtyRecords = dirtyRecords ?? Array.Empty<StorageRecoveryRecord>();
        }
    }
}
