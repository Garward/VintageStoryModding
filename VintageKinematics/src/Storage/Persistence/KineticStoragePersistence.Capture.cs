using System;
using System.Collections.Generic;
using VintageKinematics.Api.Storage;
using VintageKinematics.Storage.Index;

namespace VintageKinematics.Storage.Persistence
{
    public sealed partial class KineticStoragePersistence
    {
        public StoragePersistenceSnapshot Capture(
            KineticStorageIndex index,
            IReadOnlyCollection<UnresolvedStorageEntry> unresolvedEntries = null)
        {
            if (index == null) throw new ArgumentNullException(nameof(index));

            List<PersistedStorageEntry> records = new List<PersistedStorageEntry>();
            HashSet<long> entryIds = new HashSet<long>();
            long unresolvedQuantity = 0;
            foreach (StoredEntry entry in index.GetEntries())
            {
                byte[] attributes = StorageAttributeCodec.Encode(entry.Exemplar.Attributes);
                PersistedStorageEntry record = StorageEntryCodec.Create(
                    entry.EntryId,
                    entry.Key.ItemClass,
                    entry.Key.Code,
                    attributes,
                    entry.Quantity);
                AddUnique(records, entryIds, record);
            }

            if (unresolvedEntries != null)
            {
                foreach (UnresolvedStorageEntry unresolved in unresolvedEntries)
                {
                    if (unresolved?.Record == null) continue;
                    AddUnique(records, entryIds, unresolved.Record);
                    unresolvedQuantity = checked(unresolvedQuantity + unresolved.Record.Quantity);
                }
            }

            if (records.Count != index.EntryCount || unresolvedQuantity != index.UnresolvedItems)
            {
                throw new InvalidOperationException("Index reservations do not match persistence records.");
            }
            return new StoragePersistenceSnapshot(index.NextEntryId, records);
        }

        private static void AddUnique(
            List<PersistedStorageEntry> records,
            HashSet<long> entryIds,
            PersistedStorageEntry record)
        {
            if (!entryIds.Add(record.EntryId))
            {
                throw new InvalidOperationException("Duplicate storage entry id during capture.");
            }
            records.Add(record);
        }
    }
}
