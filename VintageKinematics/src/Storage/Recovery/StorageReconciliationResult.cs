using System;

namespace VintageKinematics.Storage.Recovery
{
    /// <summary>
    /// Non-mutating reconciliation decision with both original copies retained.
    /// </summary>
    public sealed class StorageReconciliationResult
    {
        public StorageReconciliationOutcome Outcome { get; }
        public StorageSnapshotCopy BlockEntityCopy { get; }
        public StorageSnapshotCopy RecoveryCopy { get; }
        public StorageSnapshotSource ProposedSource { get; }
        public StorageRecoveryRecord ProposedRecord { get; }
        public bool HasProposal => ProposedRecord != null;
        public bool RequiresAdminRecovery => Outcome != StorageReconciliationOutcome.Identical
            && Outcome != StorageReconciliationOutcome.IdenticalMirrorsWithStaleHeader;
        public bool RequiresConfirmation => HasProposal && RequiresAdminRecovery;
        public bool RequiresExplicitChoice => RequiresAdminRecovery
            && !HasProposal
            && Outcome != StorageReconciliationOutcome.NoValidCopy;

        internal StorageReconciliationResult(
            StorageReconciliationOutcome outcome,
            StorageSnapshotCopy blockEntityCopy,
            StorageSnapshotCopy recoveryCopy,
            StorageSnapshotSource proposedSource,
            StorageRecoveryRecord proposedRecord)
        {
            Outcome = outcome;
            BlockEntityCopy = blockEntityCopy
                ?? throw new ArgumentNullException(nameof(blockEntityCopy));
            RecoveryCopy = recoveryCopy
                ?? throw new ArgumentNullException(nameof(recoveryCopy));
            ProposedSource = proposedSource;
            ProposedRecord = proposedRecord;

            if ((proposedSource == StorageSnapshotSource.None) != (proposedRecord == null))
            {
                throw new ArgumentException("A proposed source and record must be supplied together.");
            }
        }
    }
}
