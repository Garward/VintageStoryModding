using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using VintageKinematics.Api.Storage;
using VintageKinematics.BlockEntities.Storage;
using VintageKinematics.Storage.Persistence;
using VintageKinematics.Storage.Topology;

namespace VintageKinematics.Storage.Recovery
{
    public sealed partial class KineticStorageRecoverySystem
    {
        private readonly Dictionary<string, StorageRecoveryRecord> missingControllers =
            new Dictionary<string, StorageRecoveryRecord>(StringComparer.Ordinal);
        private readonly HashSet<string> pendingControllerAudits =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> reportedMissingControllers =
            new HashSet<string>(StringComparer.Ordinal);

        private const int InitialAuditDelayMilliseconds = 250;
        private const int RetryAuditDelayMilliseconds = 1000;
        private const int MaximumAuditAttempts = 5;

        public IReadOnlyCollection<StorageRecoveryRecord> MissingControllers =>
            missingControllers.Values.ToArray();

        public void ObserveController(string warehouseId, StorageControllerLocation location)
        {
            if (Registry.TryGet(warehouseId, out StorageRecoveryRecord record)
                && record.Controller == location)
            {
                missingControllers.Remove(EvidenceKey(record));
            }
            if (ControllerRegistry.TryGet(warehouseId, out StorageRecoveryRecord controllerRecord)
                && controllerRecord.Controller == location)
            {
                missingControllers.Remove(EvidenceKey(controllerRecord));
            }
        }

        private void StartWorldAudit(ICoreServerAPI api)
        {
            api.Event.ChunkColumnLoaded += OnChunkColumnLoadedForRecovery;
        }

        private void StopWorldAudit(ICoreServerAPI api)
        {
            api.Event.ChunkColumnLoaded -= OnChunkColumnLoadedForRecovery;
        }

        private void OnChunkColumnLoadedForRecovery(Vec2i chunkCoord, IWorldChunk[] chunks)
        {
            RunWhenRegistryReady(() => QueueControllerColumnAudit(
                chunkCoord,
                1,
                InitialAuditDelayMilliseconds));
        }

        private void QueueControllerColumnAudit(Vec2i chunkCoord, int attempt, int delay)
        {
            if (!IsLoaded) return;
            IEnumerable<StorageRecoveryRecord> records = Registry.GetRecords()
                .Concat(ControllerRegistry.GetRecords())
                .GroupBy(EvidenceKey, StringComparer.Ordinal)
                .Select(group => group.First());
            foreach (StorageRecoveryRecord record in records)
            {
                if (record.IsTombstone || !IsInColumn(record.Controller, chunkCoord)) continue;
                QueueControllerAudit(record, attempt, delay);
            }
        }

        private void QueueControllerAudit(StorageRecoveryRecord record, int attempt, int delay)
        {
            string key = EvidenceKey(record);
            if (!pendingControllerAudits.Add(key)) return;
            serverApi.Event.RegisterCallback(_ =>
            {
                pendingControllerAudits.Remove(key);
                AuditController(record, attempt);
            }, delay);
        }

        private void AuditController(StorageRecoveryRecord record, int attempt)
        {
            StorageTopologyPosition position = new StorageTopologyPosition(
                record.Controller.X,
                record.Controller.InternalY,
                record.Controller.Z,
                record.Controller.Dimension);
            var source = new WorldStorageTopologySource(serverApi.World.BlockAccessor);
            if (!source.IsChunkLoaded(source.GetChunk(position))) return;

            BlockPos pos = WorldStorageTopologySource.ToBlockPos(position);
            BlockEntity blockEntity = serverApi.World.BlockAccessor.GetBlockEntity(pos);
            if (blockEntity is BEKineticWarehouseTerminal controller)
            {
                if (controller.WarehouseId == record.WarehouseId)
                {
                    missingControllers.Remove(EvidenceKey(record));
                    return;
                }
                if (string.IsNullOrWhiteSpace(controller.WarehouseId)
                    && RetryControllerAudit(record, attempt))
                {
                    return;
                }
                RecordMissingController(record);
                return;
            }

            Block block = serverApi.World.BlockAccessor.GetBlock(pos);
            bool controllerBlockStillPresent = block?.Attributes?["vkStorageMember"]
                .AsBool(false) == true;
            if (blockEntity == null && controllerBlockStillPresent)
            {
                if (RetryControllerAudit(record, attempt)) return;
                RecordMissingController(record);
                return;
            }

            if (blockEntity is IVKStorageStructureMember || source.IsConfirmedStorageMemberAbsent(position))
            {
                RecordMissingController(record);
            }
        }

        private bool RetryControllerAudit(StorageRecoveryRecord record, int attempt)
        {
            if (attempt >= MaximumAuditAttempts) return false;
            QueueControllerAudit(record, attempt + 1, RetryAuditDelayMilliseconds);
            return true;
        }

        private void RecordMissingController(StorageRecoveryRecord record)
        {
            string key = EvidenceKey(record);
            if (TryRetireEmptyOrphan(record))
            {
                missingControllers.Remove(key);
                return;
            }

            if (!missingControllers.ContainsKey(key))
            {
                missingControllers.Add(key, record);
            }
            if (reportedMissingControllers.Add(key))
            {
                serverApi.Logger.Error(
                    "[VintageKinematics] Recovery record {0} has no matching controller at {1}/{2}/{3} in dimension {4}; evidence is retained and the warehouse requires recovery.",
                    record.WarehouseId,
                    record.Controller.X,
                    record.Controller.InternalY,
                    record.Controller.Z,
                    record.Controller.Dimension);
            }
        }

        private bool TryRetireEmptyOrphan(StorageRecoveryRecord observed)
        {
            if (!Registry.TryGet(observed.WarehouseId, out StorageRecoveryRecord recovery)
                || !ControllerRegistry.TryGet(
                    observed.WarehouseId,
                    out StorageRecoveryRecord controller)
                || recovery.Controller != observed.Controller
                || !StorageOrphanRecoveryPolicy.CanTombstoneEmptyMirrors(
                    recovery,
                    controller,
                    new KineticStoragePersistence(serverApi.World)))
            {
                return false;
            }

            TombstoneMirrors(observed.WarehouseId, checked(recovery.Revision + 1));
            return true;
        }

        private static string EvidenceKey(StorageRecoveryRecord record)
        {
            StorageControllerLocation location = record.Controller;
            return record.WarehouseId
                + "@"
                + location.Dimension
                + ":"
                + location.X
                + ","
                + location.InternalY
                + ","
                + location.Z;
        }

        private static bool IsInColumn(StorageControllerLocation location, Vec2i chunkCoord)
        {
            int size = GlobalConstants.ChunkSize;
            return FloorDivide(location.X, size) == chunkCoord.X
                && FloorDivide(location.Z, size) == chunkCoord.Y;
        }

        private static int FloorDivide(int value, int divisor)
        {
            int quotient = value / divisor;
            return value % divisor < 0 ? quotient - 1 : quotient;
        }
    }
}
