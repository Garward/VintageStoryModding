using System;
using System.Collections.Generic;
using System.Linq;

namespace VintageKinematics.Storage.Recovery
{
    /// <summary>
    /// Loads and saves a recovery registry through a minimal key-value boundary.
    /// </summary>
    internal sealed class StorageRecoveryPersistence
    {
        private long committedIndexGeneration;
        private bool indexRepairRequired;
        private readonly Dictionary<string, int> recordSlots =
            new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly StorageRecoveryKeyspace keyspace;
        public bool HasPendingRepair => indexRepairRequired;

        public StorageRecoveryPersistence(StorageRecoveryKeyspace keyspace = null)
        {
            this.keyspace = keyspace ?? StorageRecoveryKeyspace.Recovery;
        }

        public StorageRecoveryLoadResult Load(IStorageRecoveryStore store)
        {
            if (store == null) throw new ArgumentNullException(nameof(store));

            StorageRecoveryRegistry registry = new StorageRecoveryRegistry();
            List<StorageRecoveryLoadIssue> issues = new List<StorageRecoveryLoadIssue>();
            byte[] indexBytes = LoadCommittedIndex(store, issues);
            if (indexBytes == null)
            {
                return new StorageRecoveryLoadResult(registry, issues);
            }

            StorageRecoveryIndexDecodeResult decodedIndex =
                StorageRecoveryRegistryCodec.DecodeIndex(indexBytes);
            if (!decodedIndex.Success)
            {
                issues.Add(new StorageRecoveryLoadIssue(
                    MapIndexError(decodedIndex.Error),
                    indexBytes));
                return new StorageRecoveryLoadResult(registry, issues);
            }

            foreach (StorageRecoveryIndexEntry entry in decodedIndex.Entries)
            {
                LoadRecord(store, registry, issues, entry);
            }
            return new StorageRecoveryLoadResult(registry, issues);
        }

        public bool Save(IStorageRecoveryStore store, StorageRecoveryRegistry registry)
        {
            if (store == null) throw new ArgumentNullException(nameof(store));
            if (registry == null) throw new ArgumentNullException(nameof(registry));

            StorageRecoverySaveBatch batch = registry.CaptureSaveBatch();
            if (!batch.HasChanges && !indexRepairRequired) return false;

            HashSet<string> dirtyIds = new HashSet<string>(
                batch.DirtyRecords.Select(record => record.WarehouseId),
                StringComparer.Ordinal);
            StorageRecoveryIndexEntry[] committedEntries = batch.IndexEntries
                .Select(entry => entry.WithRecordSlot(ResolveRecordSlot(entry.WarehouseId, dirtyIds)))
                .ToArray();

            foreach (StorageRecoveryRecord record in batch.DirtyRecords)
            {
                StorageRecoveryIndexEntry committedEntry = committedEntries.Single(
                    entry => entry.WarehouseId == record.WarehouseId);
                store.Store(
                    keyspace.WarehouseSlot(record.WarehouseId, committedEntry.RecordSlot),
                    StorageRecoveryRegistryCodec.EncodeRecord(record));
            }
            if (batch.RequiresIndexWrite || indexRepairRequired)
            {
                byte[] indexBytes = StorageRecoveryRegistryCodec.EncodeIndex(committedEntries);
                long nextGeneration = checked(committedIndexGeneration + 1);
                store.Store(
                    keyspace.IndexSlot((int)(nextGeneration & 1)),
                    StorageRecoveryIndexCommitCodec.Encode(nextGeneration, indexBytes));
                committedIndexGeneration = nextGeneration;
                indexRepairRequired = false;
                recordSlots.Clear();
                foreach (StorageRecoveryIndexEntry entry in committedEntries)
                {
                    if (entry.RecordSlot >= 0) recordSlots[entry.WarehouseId] = entry.RecordSlot;
                }
            }

            registry.AcknowledgeSaved(batch);
            return true;
        }

        private byte[] LoadCommittedIndex(
            IStorageRecoveryStore store,
            List<StorageRecoveryLoadIssue> issues)
        {
            byte[] firstBytes = store.Get(keyspace.IndexSlot(0));
            byte[] secondBytes = store.Get(keyspace.IndexSlot(1));
            bool firstValid = StorageRecoveryIndexCommitCodec.TryDecode(
                firstBytes,
                out StorageRecoveryIndexCommit first);
            bool secondValid = StorageRecoveryIndexCommitCodec.TryDecode(
                secondBytes,
                out StorageRecoveryIndexCommit second);
            bool hasCommitEvidence = firstBytes != null || secondBytes != null;
            if (firstValid || secondValid)
            {
                StorageRecoveryIndexCommit selected;
                if (firstValid && secondValid && first.Generation == second.Generation
                    && !StorageRecoveryBytes.Equal(first.IndexBytes, second.IndexBytes))
                {
                    issues.Add(new StorageRecoveryLoadIssue(
                        StorageRecoveryLoadIssueKind.IndexMalformed,
                        firstBytes));
                    issues.Add(new StorageRecoveryLoadIssue(
                        StorageRecoveryLoadIssueKind.IndexMalformed,
                        secondBytes));
                    return null;
                }
                selected = !secondValid || (firstValid && first.Generation > second.Generation)
                    ? first
                    : second;
                committedIndexGeneration = selected.Generation;
                indexRepairRequired = !firstValid || !secondValid;
                return selected.IndexBytes;
            }
            if (hasCommitEvidence)
            {
                if (firstBytes != null)
                {
                    issues.Add(new StorageRecoveryLoadIssue(
                        StorageRecoveryLoadIssueKind.IndexMalformed,
                        firstBytes));
                }
                if (secondBytes != null)
                {
                    issues.Add(new StorageRecoveryLoadIssue(
                        StorageRecoveryLoadIssueKind.IndexMalformed,
                        secondBytes));
                }
                return null;
            }

            // Before dual commits existed, the index lived under one mutable key.
            return store.Get(keyspace.LegacyIndex);
        }

        private void LoadRecord(
            IStorageRecoveryStore store,
            StorageRecoveryRegistry registry,
            List<StorageRecoveryLoadIssue> issues,
            StorageRecoveryIndexEntry entry)
        {
            byte[] recordBytes = entry.RecordSlot >= 0
                ? store.Get(keyspace.WarehouseSlot(entry.WarehouseId, entry.RecordSlot))
                : null;
            if (recordBytes == null)
            {
                recordBytes = store.Get(keyspace.WarehouseVersion(entry));
            }
            if (recordBytes == null)
            {
                // Schema-one saves stored one mutable record per warehouse. Keep that data
                // readable until the next successful version-addressed registry update.
                recordBytes = store.Get(keyspace.LegacyWarehouse(entry.WarehouseId));
            }
            if (recordBytes == null)
            {
                issues.Add(new StorageRecoveryLoadIssue(
                    StorageRecoveryLoadIssueKind.RecordMissing,
                    null,
                    entry));
                return;
            }

            StorageRecoveryRecordDecodeResult decodedRecord =
                StorageRecoveryRegistryCodec.DecodeRecord(recordBytes);
            if (!decodedRecord.Success)
            {
                issues.Add(new StorageRecoveryLoadIssue(
                    MapRecordError(decodedRecord.Error),
                    recordBytes,
                    entry));
                return;
            }

            StorageRecoveryRecord record = decodedRecord.Record;
            if (!entry.Matches(record))
            {
                issues.Add(new StorageRecoveryLoadIssue(
                    StorageRecoveryLoadIssueKind.RecordIndexMismatch,
                    recordBytes,
                    entry,
                    record));
                return;
            }

            registry.Restore(record);
            if (entry.RecordSlot >= 0) recordSlots[entry.WarehouseId] = entry.RecordSlot;
            if (!record.HasValidChecksum)
            {
                issues.Add(new StorageRecoveryLoadIssue(
                    StorageRecoveryLoadIssueKind.RecordInvalidChecksum,
                    recordBytes,
                    entry,
                    record));
            }
        }

        private int ResolveRecordSlot(string warehouseId, HashSet<string> dirtyIds)
        {
            bool hasCurrent = recordSlots.TryGetValue(warehouseId, out int current);
            if (!dirtyIds.Contains(warehouseId)) return hasCurrent ? current : -1;
            return hasCurrent ? 1 - current : 0;
        }

        private static StorageRecoveryLoadIssueKind MapIndexError(StorageRecoveryDecodeError error)
        {
            return error switch
            {
                StorageRecoveryDecodeError.UnsupportedSchema =>
                    StorageRecoveryLoadIssueKind.IndexUnsupportedSchema,
                StorageRecoveryDecodeError.TooLarge =>
                    StorageRecoveryLoadIssueKind.IndexTooLarge,
                StorageRecoveryDecodeError.WarehouseLimitExceeded =>
                    StorageRecoveryLoadIssueKind.IndexWarehouseLimitExceeded,
                StorageRecoveryDecodeError.DuplicateWarehouseId =>
                    StorageRecoveryLoadIssueKind.IndexDuplicateWarehouseId,
                _ => StorageRecoveryLoadIssueKind.IndexMalformed
            };
        }

        private static StorageRecoveryLoadIssueKind MapRecordError(StorageRecoveryDecodeError error)
        {
            return error switch
            {
                StorageRecoveryDecodeError.UnsupportedSchema =>
                    StorageRecoveryLoadIssueKind.RecordUnsupportedSchema,
                StorageRecoveryDecodeError.TooLarge =>
                    StorageRecoveryLoadIssueKind.RecordTooLarge,
                _ => StorageRecoveryLoadIssueKind.RecordMalformed
            };
        }
    }
}
