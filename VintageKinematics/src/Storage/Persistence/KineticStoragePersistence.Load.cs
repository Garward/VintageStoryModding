using System;
using System.Collections.Generic;
using VintageKinematics.Storage.Index;

namespace VintageKinematics.Storage.Persistence
{
    public sealed partial class KineticStoragePersistence
    {
        public StorageLoadResult Load(byte[] bytes, StorageIndexLimits limits)
        {
            StorageSnapshotDecodeResult decoded = StorageSnapshotCodec.Decode(bytes);
            KineticStorageIndex index = new KineticStorageIndex(world, limits, acceptanceValidator);
            List<QuarantinedStorageEntry> quarantined = new(decoded.QuarantinedEntries);
            List<UnresolvedStorageEntry> unresolved = new();
            if (decoded.SchemaVersion != StoragePersistenceConstants.SchemaVersion)
            {
                return new StorageLoadResult(index, unresolved, quarantined);
            }

            RestoreEntries(decoded, index, unresolved, quarantined);
            RestoreNextEntryId(decoded, index, quarantined, bytes);
            return new StorageLoadResult(index, unresolved, quarantined);
        }

        private void RestoreEntries(
            StorageSnapshotDecodeResult decoded,
            KineticStorageIndex index,
            List<UnresolvedStorageEntry> unresolved,
            List<QuarantinedStorageEntry> quarantined)
        {
            StorageRecordResolver resolver = new StorageRecordResolver(
                world,
                collectibleResolver,
                acceptanceValidator);
            HashSet<long> seenIds = new HashSet<long>();
            long retainedQuantity = 0;
            long unresolvedQuantity = 0;

            foreach (PersistedStorageEntry record in decoded.Entries)
            {
                if (!seenIds.Add(record.EntryId))
                {
                    quarantined.Add(Quarantine(record, StorageQuarantineReason.DuplicateEntryId));
                    continue;
                }

                StorageRecordResolution resolution = resolver.Resolve(record);
                if (resolution.Kind == StorageRecordResolutionKind.Quarantined)
                {
                    quarantined.Add(resolution.Quarantine);
                    continue;
                }
                if (!TryAdd(retainedQuantity, record.Quantity, out long nextTotal))
                {
                    quarantined.Add(Quarantine(record, StorageQuarantineReason.QuantityOverflow));
                    continue;
                }

                if (resolution.Kind == StorageRecordResolutionKind.Unresolved)
                {
                    if (!TryAdd(unresolvedQuantity, record.Quantity, out unresolvedQuantity))
                    {
                        quarantined.Add(Quarantine(record, StorageQuarantineReason.QuantityOverflow));
                        continue;
                    }
                    unresolved.Add(new UnresolvedStorageEntry(record));
                }
                else
                {
                    try
                    {
                        index.RestoreResolvedEntry(record.EntryId, resolution.Exemplar, record.Quantity);
                    }
                    catch (OverflowException)
                    {
                        quarantined.Add(Quarantine(record, StorageQuarantineReason.QuantityOverflow));
                        continue;
                    }
                }
                retainedQuantity = nextTotal;
            }

            index.RestoreUnresolvedReservations(unresolvedQuantity, unresolved.Count);
        }

        private static bool TryAdd(long left, long right, out long sum)
        {
            try
            {
                sum = checked(left + right);
                return true;
            }
            catch (OverflowException)
            {
                sum = left;
                return false;
            }
        }

        private static QuarantinedStorageEntry Quarantine(
            PersistedStorageEntry record,
            StorageQuarantineReason reason)
        {
            return new QuarantinedStorageEntry(reason, record.RawRecordBytes);
        }
    }
}
