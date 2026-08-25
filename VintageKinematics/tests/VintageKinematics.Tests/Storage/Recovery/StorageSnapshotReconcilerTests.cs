using System;
using VintageKinematics.Storage.Recovery;
using Xunit;

namespace VintageKinematics.Tests.Storage.Recovery
{
    public class StorageSnapshotReconcilerTests
    {
        private const string WarehouseId = "cbb91396-f8c6-4992-8447-504841a13ed9";
        private static readonly StorageControllerLocation Controller = new(12, 34, -56, 2);

        [Fact]
        public void IdenticalCopies_LoadWithoutRecoveryOrSelection()
        {
            StorageSnapshotCopy blockEntity = Copy(LiveRecord(3, 10));
            StorageSnapshotCopy recovery = Copy(LiveRecord(3, 10));

            StorageReconciliationResult result =
                StorageSnapshotReconciler.Reconcile(blockEntity, recovery);

            Assert.Equal(StorageReconciliationOutcome.Identical, result.Outcome);
            Assert.False(result.RequiresAdminRecovery);
            Assert.False(result.RequiresConfirmation);
            Assert.Equal(StorageSnapshotSource.None, result.ProposedSource);
            Assert.Null(result.ProposedRecord);
            Assert.Same(blockEntity, result.BlockEntityCopy);
            Assert.Same(recovery, result.RecoveryCopy);
        }

        [Fact]
        public void MissingRecoveryCopy_ProposesValidBlockEntityCopy()
        {
            StorageRecoveryRecord valid = LiveRecord(3, 10);

            StorageReconciliationResult result = StorageSnapshotReconciler.Reconcile(
                Copy(valid),
                StorageSnapshotCopy.Missing());

            Assert.Equal(StorageReconciliationOutcome.SingleValidCopy, result.Outcome);
            Assert.Equal(StorageSnapshotSource.BlockEntity, result.ProposedSource);
            Assert.Same(valid, result.ProposedRecord);
            Assert.True(result.RequiresAdminRecovery);
            Assert.True(result.RequiresConfirmation);
        }

        [Fact]
        public void InvalidBlockEntityCopy_ProposesValidRecoveryCopy()
        {
            StorageRecoveryRecord valid = LiveRecord(3, 10);

            StorageReconciliationResult result = StorageSnapshotReconciler.Reconcile(
                StorageSnapshotCopy.Invalid(new byte[] { 1, 2, 3 }),
                Copy(valid));

            Assert.Equal(StorageReconciliationOutcome.SingleValidCopy, result.Outcome);
            Assert.Equal(StorageSnapshotSource.RecoveryRegistry, result.ProposedSource);
            Assert.Same(valid, result.ProposedRecord);
            Assert.True(result.RequiresConfirmation);
        }

        [Fact]
        public void TwoInvalidCopies_HaveNoAutomaticProposal()
        {
            StorageRecoveryRecord invalidChecksum = StorageRecoveryRecord.Restore(
                WarehouseId,
                Controller,
                1,
                new byte[] { 10 },
                new byte[32],
                false);

            StorageReconciliationResult result = StorageSnapshotReconciler.Reconcile(
                StorageSnapshotCopy.FromRecord(invalidChecksum),
                StorageSnapshotCopy.Invalid(new byte[] { 4, 5, 6 }));

            Assert.Equal(StorageReconciliationOutcome.NoValidCopy, result.Outcome);
            Assert.Equal(StorageSnapshotSource.None, result.ProposedSource);
            Assert.Null(result.ProposedRecord);
            Assert.True(result.RequiresAdminRecovery);
            Assert.False(result.RequiresConfirmation);
            Assert.False(result.RequiresExplicitChoice);
        }

        [Fact]
        public void HigherRecoveryRevision_IsProposedWithoutMerging()
        {
            StorageRecoveryRecord blockEntity = LiveRecord(3, 10);
            StorageRecoveryRecord recovery = LiveRecord(4, 20);

            StorageReconciliationResult result = StorageSnapshotReconciler.Reconcile(
                Copy(blockEntity),
                Copy(recovery));

            Assert.Equal(StorageReconciliationOutcome.Divergent, result.Outcome);
            Assert.Equal(StorageSnapshotSource.RecoveryRegistry, result.ProposedSource);
            Assert.Same(recovery, result.ProposedRecord);
            Assert.True(result.RequiresConfirmation);
            Assert.Equal(new byte[] { 10 }, result.BlockEntityCopy.Record.SnapshotBytes);
            Assert.Equal(new byte[] { 20 }, result.RecoveryCopy.Record.SnapshotBytes);
        }

        [Fact]
        public void HigherBlockEntityRevision_IsProposedWithoutMerging()
        {
            StorageRecoveryRecord blockEntity = LiveRecord(5, 30);
            StorageRecoveryRecord recovery = LiveRecord(4, 20);

            StorageReconciliationResult result = StorageSnapshotReconciler.Reconcile(
                Copy(blockEntity),
                Copy(recovery));

            Assert.Equal(StorageReconciliationOutcome.Divergent, result.Outcome);
            Assert.Equal(StorageSnapshotSource.BlockEntity, result.ProposedSource);
            Assert.Same(blockEntity, result.ProposedRecord);
            Assert.True(result.RequiresConfirmation);
        }

        [Fact]
        public void SameRevisionWithDifferentContents_HasNoAutomaticProposal()
        {
            StorageReconciliationResult result = StorageSnapshotReconciler.Reconcile(
                Copy(LiveRecord(3, 10)),
                Copy(LiveRecord(3, 20)));

            Assert.Equal(StorageReconciliationOutcome.Divergent, result.Outcome);
            Assert.Equal(StorageSnapshotSource.None, result.ProposedSource);
            Assert.Null(result.ProposedRecord);
            Assert.True(result.RequiresAdminRecovery);
            Assert.True(result.RequiresExplicitChoice);
        }

        [Fact]
        public void WarehouseIdentityConflict_HasNoAutomaticProposal()
        {
            StorageRecoveryRecord otherWarehouse = StorageRecoveryRecord.Create(
                "01476d56-5104-44f0-9ee3-405252f136e6",
                Controller,
                10,
                new byte[] { 20 });

            StorageReconciliationResult result = StorageSnapshotReconciler.Reconcile(
                Copy(LiveRecord(3, 10)),
                Copy(otherWarehouse));

            Assert.Equal(StorageReconciliationOutcome.IdentityConflict, result.Outcome);
            Assert.Equal(StorageSnapshotSource.None, result.ProposedSource);
            Assert.Null(result.ProposedRecord);
            Assert.True(result.RequiresExplicitChoice);
        }

        [Fact]
        public void ControllerLocationConflict_HasNoAutomaticProposal()
        {
            StorageRecoveryRecord moved = StorageRecoveryRecord.Create(
                WarehouseId,
                new StorageControllerLocation(13, 34, -56, 2),
                10,
                new byte[] { 20 });

            StorageReconciliationResult result = StorageSnapshotReconciler.Reconcile(
                Copy(LiveRecord(3, 10)),
                Copy(moved));

            Assert.Equal(StorageReconciliationOutcome.IdentityConflict, result.Outcome);
            Assert.Null(result.ProposedRecord);
        }

        [Fact]
        public void NewerTombstone_IsProposedButStillRequiresConfirmation()
        {
            StorageRecoveryRecord tombstone = TombstoneRecord(4);

            StorageReconciliationResult result = StorageSnapshotReconciler.Reconcile(
                Copy(LiveRecord(3, 10)),
                Copy(tombstone));

            Assert.Equal(StorageReconciliationOutcome.TombstoneConflict, result.Outcome);
            Assert.Equal(StorageSnapshotSource.RecoveryRegistry, result.ProposedSource);
            Assert.Same(tombstone, result.ProposedRecord);
            Assert.True(result.RequiresConfirmation);
        }

        [Fact]
        public void NewerLiveCopy_CannotAutomaticallyOverrideTombstone()
        {
            StorageReconciliationResult result = StorageSnapshotReconciler.Reconcile(
                Copy(LiveRecord(5, 20)),
                Copy(TombstoneRecord(4)));

            Assert.Equal(StorageReconciliationOutcome.TombstoneConflict, result.Outcome);
            Assert.Equal(StorageSnapshotSource.None, result.ProposedSource);
            Assert.Null(result.ProposedRecord);
            Assert.True(result.RequiresAdminRecovery);
            Assert.True(result.RequiresExplicitChoice);
        }

        [Fact]
        public void SnapshotCopy_DefensivelyRetainsRawEvidence()
        {
            byte[] raw = new byte[] { 1, 2, 3 };
            StorageSnapshotCopy copy = StorageSnapshotCopy.Invalid(raw);

            raw[0] = 9;
            copy.RawBytes[1] = 9;

            Assert.Equal(new byte[] { 1, 2, 3 }, copy.RawBytes);
        }

        private static StorageSnapshotCopy Copy(StorageRecoveryRecord record)
        {
            return StorageSnapshotCopy.FromRecord(record);
        }

        private static StorageRecoveryRecord LiveRecord(long revision, byte value)
        {
            return StorageRecoveryRecord.Create(
                WarehouseId,
                Controller,
                revision,
                new[] { value });
        }

        private static StorageRecoveryRecord TombstoneRecord(long revision)
        {
            return StorageRecoveryRecord.Create(
                WarehouseId,
                Controller,
                revision,
                Array.Empty<byte>(),
                isTombstone: true);
        }
    }
}
