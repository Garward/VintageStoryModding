using System;

namespace VintageKinematics.Storage.Recovery
{
    /// <summary>
    /// Compares controller and registry copies without merging or mutating either one.
    /// </summary>
    public static class StorageSnapshotReconciler
    {
        public static StorageReconciliationResult Reconcile(
            StorageSnapshotCopy blockEntityCopy,
            StorageSnapshotCopy recoveryCopy)
        {
            if (blockEntityCopy == null) throw new ArgumentNullException(nameof(blockEntityCopy));
            if (recoveryCopy == null) throw new ArgumentNullException(nameof(recoveryCopy));

            bool blockEntityValid = IsValid(blockEntityCopy);
            bool recoveryValid = IsValid(recoveryCopy);
            if (!blockEntityValid || !recoveryValid)
            {
                return ReconcileMissingOrInvalid(
                    blockEntityCopy,
                    recoveryCopy,
                    blockEntityValid,
                    recoveryValid);
            }

            StorageRecoveryRecord blockEntityRecord = blockEntityCopy.Record;
            StorageRecoveryRecord recoveryRecord = recoveryCopy.Record;
            if (blockEntityRecord.IsEquivalentTo(recoveryRecord))
            {
                return Result(
                    StorageReconciliationOutcome.Identical,
                    blockEntityCopy,
                    recoveryCopy);
            }

            if (!HasSameIdentity(blockEntityRecord, recoveryRecord))
            {
                return Result(
                    StorageReconciliationOutcome.IdentityConflict,
                    blockEntityCopy,
                    recoveryCopy);
            }

            if (blockEntityRecord.IsTombstone != recoveryRecord.IsTombstone)
            {
                return ReconcileTombstoneConflict(
                    blockEntityCopy,
                    recoveryCopy,
                    blockEntityRecord,
                    recoveryRecord);
            }

            if (blockEntityRecord.Revision > recoveryRecord.Revision)
            {
                return Result(
                    StorageReconciliationOutcome.Divergent,
                    blockEntityCopy,
                    recoveryCopy,
                    StorageSnapshotSource.BlockEntity,
                    blockEntityRecord);
            }
            if (recoveryRecord.Revision > blockEntityRecord.Revision)
            {
                return Result(
                    StorageReconciliationOutcome.Divergent,
                    blockEntityCopy,
                    recoveryCopy,
                    StorageSnapshotSource.RecoveryRegistry,
                    recoveryRecord);
            }

            return Result(
                StorageReconciliationOutcome.Divergent,
                blockEntityCopy,
                recoveryCopy);
        }

        private static StorageReconciliationResult ReconcileMissingOrInvalid(
            StorageSnapshotCopy blockEntityCopy,
            StorageSnapshotCopy recoveryCopy,
            bool blockEntityValid,
            bool recoveryValid)
        {
            if (blockEntityValid)
            {
                return Result(
                    StorageReconciliationOutcome.SingleValidCopy,
                    blockEntityCopy,
                    recoveryCopy,
                    StorageSnapshotSource.BlockEntity,
                    blockEntityCopy.Record);
            }
            if (recoveryValid)
            {
                return Result(
                    StorageReconciliationOutcome.SingleValidCopy,
                    blockEntityCopy,
                    recoveryCopy,
                    StorageSnapshotSource.RecoveryRegistry,
                    recoveryCopy.Record);
            }

            return Result(
                StorageReconciliationOutcome.NoValidCopy,
                blockEntityCopy,
                recoveryCopy);
        }

        private static StorageReconciliationResult ReconcileTombstoneConflict(
            StorageSnapshotCopy blockEntityCopy,
            StorageSnapshotCopy recoveryCopy,
            StorageRecoveryRecord blockEntityRecord,
            StorageRecoveryRecord recoveryRecord)
        {
            if (blockEntityRecord.IsTombstone
                && blockEntityRecord.Revision > recoveryRecord.Revision)
            {
                return Result(
                    StorageReconciliationOutcome.TombstoneConflict,
                    blockEntityCopy,
                    recoveryCopy,
                    StorageSnapshotSource.BlockEntity,
                    blockEntityRecord);
            }
            if (recoveryRecord.IsTombstone
                && recoveryRecord.Revision > blockEntityRecord.Revision)
            {
                return Result(
                    StorageReconciliationOutcome.TombstoneConflict,
                    blockEntityCopy,
                    recoveryCopy,
                    StorageSnapshotSource.RecoveryRegistry,
                    recoveryRecord);
            }

            return Result(
                StorageReconciliationOutcome.TombstoneConflict,
                blockEntityCopy,
                recoveryCopy);
        }

        private static bool IsValid(StorageSnapshotCopy copy)
        {
            return copy.State == StorageSnapshotCopyState.Valid
                && copy.Record != null
                && copy.Record.HasValidChecksum;
        }

        private static bool HasSameIdentity(
            StorageRecoveryRecord blockEntityRecord,
            StorageRecoveryRecord recoveryRecord)
        {
            return blockEntityRecord.WarehouseId == recoveryRecord.WarehouseId
                && blockEntityRecord.Controller == recoveryRecord.Controller;
        }

        private static StorageReconciliationResult Result(
            StorageReconciliationOutcome outcome,
            StorageSnapshotCopy blockEntityCopy,
            StorageSnapshotCopy recoveryCopy,
            StorageSnapshotSource proposedSource = StorageSnapshotSource.None,
            StorageRecoveryRecord proposedRecord = null)
        {
            return new StorageReconciliationResult(
                outcome,
                blockEntityCopy,
                recoveryCopy,
                proposedSource,
                proposedRecord);
        }
    }
}
