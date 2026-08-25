using System.Collections.Generic;
using VintageKinematics.Storage.Index;

namespace VintageKinematics.Storage.Persistence
{
    public sealed partial class KineticStoragePersistence
    {
        private static void RestoreNextEntryId(
            StorageSnapshotDecodeResult decoded,
            KineticStorageIndex index,
            List<QuarantinedStorageEntry> quarantined,
            byte[] snapshotBytes)
        {
            long maxEntryId = 0;
            foreach (PersistedStorageEntry entry in decoded.Entries)
            {
                if (entry.EntryId > maxEntryId) maxEntryId = entry.EntryId;
            }

            long nextEntryId = decoded.NextEntryId;
            if (nextEntryId <= maxEntryId || nextEntryId <= 0)
            {
                quarantined.Add(new QuarantinedStorageEntry(
                    StorageQuarantineReason.InvalidNextEntryId,
                    snapshotBytes));
                nextEntryId = maxEntryId == long.MaxValue ? long.MaxValue : maxEntryId + 1;
                if (nextEntryId <= 0) nextEntryId = 1;
            }
            index.RestoreNextEntryId(nextEntryId);
        }
    }
}
