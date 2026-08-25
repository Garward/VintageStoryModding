using System;
using VintageKinematics.Api.Storage;
using VintageKinematics.Storage.Persistence;
using VintageKinematics.Storage.Recovery;
using VintageKinematics.Storage.Index;

namespace VintageKinematics.BlockEntities.Storage
{
    public partial class BEKineticWarehouseTerminal
    {
        public bool CanConfirmEmptyRecovery =>
            Api?.Side == Vintagestory.API.Common.EnumAppSide.Server
            && LastReconciliation?.Outcome == StorageReconciliationOutcome.NoValidCopy
            && SyncedStoredItems == 0;

        /// <summary>
        /// Converges both copies on a newly numbered revision. Callers must enforce admin access.
        /// </summary>
        public bool TryConfirmRecovery(
            StorageSnapshotSource selectedSource,
            out string failureCode)
        {
            lock (persistenceSync)
            {
                return TryConfirmRecoverySynchronized(selectedSource, out failureCode);
            }
        }

        private bool TryConfirmRecoverySynchronized(
            StorageSnapshotSource selectedSource,
            out string failureCode)
        {
            failureCode = null;
            if (Api?.Side != Vintagestory.API.Common.EnumAppSide.Server
                || LastReconciliation == null)
            {
                failureCode = "storage-recovery-unavailable";
                return false;
            }

            StorageSnapshotCopy selected = selectedSource switch
            {
                StorageSnapshotSource.BlockEntity => LastReconciliation.BlockEntityCopy,
                StorageSnapshotSource.RecoveryRegistry => LastReconciliation.RecoveryCopy,
                _ => null
            };
            selected = ValidatePhysicalIdentity(selected);
            if (selected?.State != StorageSnapshotCopyState.Valid
                || selected.Record == null
                || selected.Record.IsTombstone)
            {
                failureCode = "storage-recovery-invalid-selection";
                return false;
            }

            KineticStoragePersistence persistence = new KineticStoragePersistence(Api.World);
            StorageLoadResult loaded = persistence.Load(
                selected.Record.SnapshotBytes,
                CurrentIndexLimits());
            if (loaded.HasCorruption)
            {
                failureCode = "storage-recovery-corrupt-selection";
                return false;
            }

            long revision = checked(MaxRetainedRevision() + 1);
            StorageRecoveryRecord converged = StorageRecoveryRecord.Create(
                WarehouseId,
                PhysicalLocation(),
                revision,
                selected.Record.SnapshotBytes);
            KineticStorageRecoverySystem recoverySystem =
                Api.ModLoader.GetModSystem<KineticStorageRecoverySystem>();
            if (recoverySystem?.ApplyExplicitRecovery(converged) != true)
            {
                failureCode = "storage-recovery-commit-rejected";
                return false;
            }

            itemIndex = loaded.Index;
            unresolvedEntries.Clear();
            unresolvedEntries.AddRange(loaded.UnresolvedEntries);
            activeRecoveryRecord = converged;
            persistedItemCopy = StorageSnapshotCopy.FromRecord(converged);
            persistedItemHeader = new StorageRecoveryIndexEntry(converged);
            persistedItemHeaderBytes = Array.Empty<byte>();
            LastReconciliation = StorageSnapshotReconciler.Reconcile(
                persistedItemCopy,
                StorageSnapshotCopy.FromRecord(converged));
            MarkDirty();
            if (!recoverySystem.CanPersist)
            {
                StructureState = StorageState.RecoveryRequired;
                failureCode = "storage-recovery-other-issues-remain";
                return true;
            }
            StructureState = StorageState.StructureUnknown;
            RequestStructureRebuild(StorageChangeReason.Recovery);
            return true;
        }

        public bool TryConfirmEmptyRecovery(out string failureCode)
        {
            lock (persistenceSync)
            {
                return TryConfirmEmptyRecoverySynchronized(out failureCode);
            }
        }

        private bool TryConfirmEmptyRecoverySynchronized(out string failureCode)
        {
            failureCode = null;
            if (!CanConfirmEmptyRecovery)
            {
                failureCode = "storage-recovery-empty-reset-not-safe";
                return false;
            }

            KineticStorageIndex emptyIndex = new KineticStorageIndex(
                Api.World,
                CurrentIndexLimits());
            KineticStoragePersistence persistence = new KineticStoragePersistence(Api.World);
            byte[] snapshot = persistence.Encode(persistence.Capture(emptyIndex));
            StorageRecoveryRecord converged = StorageRecoveryRecord.Create(
                WarehouseId,
                PhysicalLocation(),
                checked(MaxRetainedRevision() + 1),
                snapshot);
            KineticStorageRecoverySystem recoverySystem =
                Api.ModLoader.GetModSystem<KineticStorageRecoverySystem>();
            if (recoverySystem?.ApplyExplicitRecovery(converged) != true)
            {
                failureCode = "storage-recovery-commit-rejected";
                return false;
            }

            itemIndex = emptyIndex;
            unresolvedEntries.Clear();
            activeRecoveryRecord = converged;
            persistedItemCopy = StorageSnapshotCopy.FromRecord(converged);
            persistedItemHeader = new StorageRecoveryIndexEntry(converged);
            persistedItemHeaderBytes = Array.Empty<byte>();
            LastReconciliation = StorageSnapshotReconciler.Reconcile(
                persistedItemCopy,
                StorageSnapshotCopy.FromRecord(converged));
            StructureState = StorageState.StructureUnknown;
            MarkDirty();
            RequestStructureRebuild(StorageChangeReason.Recovery);
            return true;
        }

        private long MaxRetainedRevision()
        {
            long maximum = 0;
            maximum = Math.Max(maximum, LastReconciliation.BlockEntityCopy.Record?.Revision ?? 0);
            maximum = Math.Max(maximum, LastReconciliation.BlockEntityCopy.Header?.Revision ?? 0);
            maximum = Math.Max(maximum, LastReconciliation.RecoveryCopy.Record?.Revision ?? 0);
            maximum = Math.Max(maximum, LastReconciliation.RecoveryCopy.Header?.Revision ?? 0);
            return maximum;
        }
    }
}
