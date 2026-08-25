using System;
using System.Collections.Generic;
using System.Linq;

namespace VintageKinematics.Storage.Recovery
{
    /// <summary>
    /// Server-thread recovery mirror with revision-aware dirty tracking.
    /// </summary>
    public sealed class StorageRecoveryRegistry
    {
        private readonly Dictionary<string, StorageRecoveryRecord> records =
            new(StringComparer.Ordinal);
        private readonly HashSet<string> dirtyWarehouseIds = new(StringComparer.Ordinal);
        private long generation;
        private bool indexDirty;

        public int Count => records.Count;
        public bool IsDirty => indexDirty || dirtyWarehouseIds.Count > 0;

        public IReadOnlyList<StorageRecoveryRecord> GetRecords()
        {
            return records.Values.OrderBy(record => record.WarehouseId, StringComparer.Ordinal).ToArray();
        }

        public bool TryGet(string warehouseId, out StorageRecoveryRecord record)
        {
            if (!StorageWarehouseId.TryNormalize(warehouseId, out string normalized))
            {
                record = null;
                return false;
            }
            return records.TryGetValue(normalized, out record);
        }

        public bool Upsert(StorageRecoveryRecord record)
        {
            if (!ValidateUpsert(record)) return false;

            records[record.WarehouseId] = record;
            MarkDirty(record.WarehouseId);
            return true;
        }

        internal bool ValidateUpsert(StorageRecoveryRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            if (!record.HasValidChecksum)
            {
                throw new InvalidOperationException("Cannot commit a recovery record with an invalid checksum.");
            }

            if (records.TryGetValue(record.WarehouseId, out StorageRecoveryRecord existing))
            {
                ValidateUpdate(existing, record);
                return !existing.IsEquivalentTo(record);
            }
            if (records.Count >= StorageRecoveryConstants.MaxWarehouses)
            {
                throw new InvalidOperationException("Recovery warehouse limit exceeded.");
            }
            return true;
        }

        public StorageRecoveryRecord Tombstone(string warehouseId, long revision)
        {
            if (!TryGet(warehouseId, out StorageRecoveryRecord existing))
            {
                throw new KeyNotFoundException("Cannot tombstone an unknown warehouse.");
            }

            StorageRecoveryRecord tombstone = StorageRecoveryRecord.Create(
                existing.WarehouseId,
                existing.Controller,
                revision,
                Array.Empty<byte>(),
                isTombstone: true);
            Upsert(tombstone);
            return tombstone;
        }

        /// <summary>
        /// Replaces one identity after a caller has explicitly selected retained recovery evidence.
        /// Unlike normal updates, this may intentionally supersede a tombstone or tied divergence.
        /// </summary>
        public bool ReplaceAfterExplicitRecovery(StorageRecoveryRecord record)
        {
            ValidateExplicitRecovery(record);

            records[record.WarehouseId] = record;
            MarkDirty(record.WarehouseId);
            return true;
        }

        internal void ValidateExplicitRecovery(StorageRecoveryRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            if (!record.HasValidChecksum)
            {
                throw new InvalidOperationException("Explicit recovery requires a valid checksum.");
            }
            if (records.TryGetValue(record.WarehouseId, out StorageRecoveryRecord existing))
            {
                if (existing.Controller != record.Controller)
                {
                    throw new InvalidOperationException("Explicit recovery cannot move a warehouse controller.");
                }
                if (record.Revision <= existing.Revision)
                {
                    throw new InvalidOperationException("Explicit recovery must create a newer revision.");
                }
            }
            else if (records.Count >= StorageRecoveryConstants.MaxWarehouses)
            {
                throw new InvalidOperationException("Recovery warehouse limit exceeded.");
            }
        }

        public void Restore(StorageRecoveryRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            if (records.ContainsKey(record.WarehouseId))
            {
                throw new InvalidOperationException("Duplicate warehouse id while restoring recovery records.");
            }
            if (records.Count >= StorageRecoveryConstants.MaxWarehouses)
            {
                throw new InvalidOperationException("Recovery warehouse limit exceeded.");
            }

            records.Add(record.WarehouseId, record);
        }

        public StorageRecoverySaveBatch CaptureSaveBatch()
        {
            StorageRecoveryIndexEntry[] indexEntries = records.Values
                .OrderBy(record => record.WarehouseId, StringComparer.Ordinal)
                .Select(record => new StorageRecoveryIndexEntry(record))
                .ToArray();
            StorageRecoveryRecord[] dirtyRecords = dirtyWarehouseIds
                .OrderBy(id => id, StringComparer.Ordinal)
                .Select(id => records[id])
                .ToArray();

            return new StorageRecoverySaveBatch(
                generation,
                indexDirty,
                indexEntries,
                dirtyRecords);
        }

        public void AcknowledgeSaved(StorageRecoverySaveBatch batch)
        {
            if (batch == null) throw new ArgumentNullException(nameof(batch));

            foreach (StorageRecoveryRecord saved in batch.DirtyRecords)
            {
                if (records.TryGetValue(saved.WarehouseId, out StorageRecoveryRecord current)
                    && ReferenceEquals(current, saved))
                {
                    dirtyWarehouseIds.Remove(saved.WarehouseId);
                }
            }

            if (batch.RequiresIndexWrite && batch.Generation == generation)
            {
                indexDirty = false;
            }
        }

        private void MarkDirty(string warehouseId)
        {
            dirtyWarehouseIds.Add(warehouseId);
            generation = checked(generation + 1);
            indexDirty = true;
        }

        private static void ValidateUpdate(StorageRecoveryRecord existing, StorageRecoveryRecord replacement)
        {
            if (existing.Controller != replacement.Controller)
            {
                throw new InvalidOperationException("A warehouse recovery record cannot move controllers.");
            }
            if (existing.IsTombstone && !replacement.IsTombstone)
            {
                throw new InvalidOperationException("A tombstoned warehouse cannot be silently resurrected.");
            }
            if (replacement.Revision < existing.Revision)
            {
                throw new InvalidOperationException("Recovery record revision cannot move backward.");
            }
            if (existing.Revision == long.MaxValue)
            {
                throw new InvalidOperationException("Recovery record revision is exhausted.");
            }
            if (replacement.Revision > existing.Revision + 1)
            {
                throw new InvalidOperationException("Recovery record revisions must advance one step at a time.");
            }
            if (replacement.Revision == existing.Revision && !existing.IsEquivalentTo(replacement))
            {
                throw new InvalidOperationException("Divergent recovery records share the same revision.");
            }
        }

    }
}
