using System;
using System.Collections.Generic;
using VintageKinematics.Api.Storage;
using VintageKinematics.Storage.Index;

namespace VintageKinematics.Storage.Persistence
{
    public sealed class StorageLoadResult
    {
        public KineticStorageIndex Index { get; }
        public IReadOnlyList<UnresolvedStorageEntry> UnresolvedEntries { get; }
        public IReadOnlyList<QuarantinedStorageEntry> QuarantinedEntries { get; }
        public StorageState SuggestedState { get; }

        public bool HasCorruption => QuarantinedEntries.Count > 0;

        internal StorageLoadResult(
            KineticStorageIndex index,
            IReadOnlyList<UnresolvedStorageEntry> unresolvedEntries,
            IReadOnlyList<QuarantinedStorageEntry> quarantinedEntries)
        {
            Index = index;
            UnresolvedEntries = unresolvedEntries ?? Array.Empty<UnresolvedStorageEntry>();
            QuarantinedEntries = quarantinedEntries ?? Array.Empty<QuarantinedStorageEntry>();
            if (QuarantinedEntries.Count > 0) SuggestedState = StorageState.Corrupt;
            else if (Index.StoredItems > Index.Limits.ItemCapacity) SuggestedState = StorageState.OverCapacity;
            else SuggestedState = StorageState.Online;
        }
    }
}
