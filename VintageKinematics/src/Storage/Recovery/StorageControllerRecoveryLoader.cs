using System;
using VintageKinematics.Storage.Index;
using VintageKinematics.Storage.Persistence;

namespace VintageKinematics.Storage.Recovery
{
    /// <summary>
    /// Validates both snapshot envelopes and their item payloads before selecting normal startup.
    /// </summary>
    internal static class StorageControllerRecoveryLoader
    {
        public static StorageControllerRecoveryDecision Prepare(
            StorageSnapshotCopy blockEntityCopy,
            StorageSnapshotCopy recoveryCopy,
            KineticStoragePersistence persistence,
            StorageIndexLimits limits)
        {
            if (persistence == null) throw new ArgumentNullException(nameof(persistence));

            StorageSnapshotCopy validatedBlockEntity = ValidatePayload(
                blockEntityCopy,
                persistence,
                limits,
                out StorageLoadResult blockEntityLoad);
            StorageSnapshotCopy validatedRecovery = ValidatePayload(
                recoveryCopy,
                persistence,
                limits,
                out StorageLoadResult recoveryLoad);

            if (CanRepairStaleHeader(validatedBlockEntity, validatedRecovery, recoveryLoad))
            {
                var repaired = new StorageReconciliationResult(
                    StorageReconciliationOutcome.IdenticalMirrorsWithStaleHeader,
                    validatedBlockEntity,
                    validatedRecovery,
                    StorageSnapshotSource.RecoveryRegistry,
                    validatedRecovery.Record);
                return new StorageControllerRecoveryDecision(
                    repaired,
                    validatedRecovery.Record,
                    recoveryLoad);
            }

            StorageReconciliationResult reconciliation = StorageSnapshotReconciler.Reconcile(
                validatedBlockEntity,
                validatedRecovery);

            StorageRecoveryRecord record = validatedBlockEntity.Record;
            bool opensNormally = reconciliation.Outcome == StorageReconciliationOutcome.Identical
                && record != null
                && !record.IsTombstone
                && blockEntityLoad != null
                && !blockEntityLoad.HasCorruption;
            return opensNormally
                ? new StorageControllerRecoveryDecision(reconciliation, record, blockEntityLoad)
                : new StorageControllerRecoveryDecision(reconciliation);
        }

        private static bool CanRepairStaleHeader(
            StorageSnapshotCopy controllerCopy,
            StorageSnapshotCopy recoveryCopy,
            StorageLoadResult recoveryLoad)
        {
            bool mirrorsAgree = controllerCopy.State == StorageSnapshotCopyState.Invalid
                && controllerCopy.Record != null
                && recoveryCopy.State == StorageSnapshotCopyState.Valid
                && recoveryCopy.Record != null
                && !recoveryCopy.Record.IsTombstone
                && recoveryLoad != null
                && !recoveryLoad.HasCorruption
                && controllerCopy.Record.IsEquivalentTo(recoveryCopy.Record);
            if (!mirrorsAgree) return false;

            StorageRecoveryIndexEntry header = controllerCopy.Header;
            if (header == null)
            {
                // A missing or malformed compact header can be reconstructed when both
                // independently persisted full mirrors prove the same valid state.
                return true;
            }

            // A decoded header is durable evidence. Only an older live header is stale.
            // Equal conflicting or ahead headers require explicit recovery so a Stratum
            // incremental chunk flush can never be silently rolled back to older mirrors.
            return !header.IsTombstone
                && header.WarehouseId == recoveryCopy.Record.WarehouseId
                && header.Controller == recoveryCopy.Record.Controller
                && header.Revision < recoveryCopy.Record.Revision;
        }

        private static StorageSnapshotCopy ValidatePayload(
            StorageSnapshotCopy copy,
            KineticStoragePersistence persistence,
            StorageIndexLimits limits,
            out StorageLoadResult loaded)
        {
            copy ??= StorageSnapshotCopy.Missing();
            loaded = null;
            if (copy.State != StorageSnapshotCopyState.Valid
                || copy.Record == null
                || copy.Record.IsTombstone)
            {
                return copy;
            }

            loaded = persistence.Load(copy.Record.SnapshotBytes, limits);
            if (!loaded.HasCorruption) return copy;

            byte[] evidence = copy.RawBytes.Length > 0
                ? copy.RawBytes
                : copy.Record.SnapshotBytes;
            loaded = null;
            return StorageSnapshotCopy.Invalid(evidence, copy.Record, copy.Header);
        }
    }
}
