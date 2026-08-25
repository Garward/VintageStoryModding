using System;
using VintageKinematics.Storage.Index;
using VintageKinematics.Storage.Persistence;
using VintageKinematics.Storage.Recovery;
using Xunit;

namespace VintageKinematics.Tests.Storage.Recovery
{
    public class StorageControllerRecoveryLoaderTests
    {
        private const string WarehouseId = "00000000-0000-0000-0000-000000000001";
        private static readonly StorageControllerLocation Controller = new(1, 2, 3, 0);
        private static readonly StorageIndexLimits Limits = new(4096, 64);

        [Fact]
        public void IdenticalValidLiveCopies_OpenNormally()
        {
            StorageRecoveryRecord record = EmptyRecord();

            StorageControllerRecoveryDecision decision = StorageControllerRecoveryLoader.Prepare(
                StorageSnapshotCopy.FromRecord(record),
                StorageSnapshotCopy.FromRecord(record),
                new KineticStoragePersistence(null),
                Limits);

            Assert.True(decision.CanOpen);
            Assert.False(decision.RequiresRecovery);
            Assert.Equal(StorageReconciliationOutcome.Identical, decision.Reconciliation.Outcome);
            Assert.Equal(0, decision.LoadedSnapshot.Index.StoredItems);
        }

        [Fact]
        public void ValidOuterChecksumCannotHideMalformedItemSnapshot()
        {
            StorageRecoveryRecord malformed = StorageRecoveryRecord.Create(
                WarehouseId,
                Controller,
                1,
                new byte[] { 1, 2, 3 });

            StorageControllerRecoveryDecision decision = StorageControllerRecoveryLoader.Prepare(
                StorageSnapshotCopy.FromRecord(malformed),
                StorageSnapshotCopy.FromRecord(malformed),
                new KineticStoragePersistence(null),
                Limits);

            Assert.False(decision.CanOpen);
            Assert.Equal(StorageReconciliationOutcome.NoValidCopy, decision.Reconciliation.Outcome);
        }

        [Fact]
        public void IdenticalTombstonesDoNotOpenAnExistingController()
        {
            StorageRecoveryRecord tombstone = StorageRecoveryRecord.Create(
                WarehouseId,
                Controller,
                2,
                Array.Empty<byte>(),
                isTombstone: true);

            StorageControllerRecoveryDecision decision = StorageControllerRecoveryLoader.Prepare(
                StorageSnapshotCopy.FromRecord(tombstone),
                StorageSnapshotCopy.FromRecord(tombstone),
                new KineticStoragePersistence(null),
                Limits);

            Assert.True(decision.RequiresRecovery);
            Assert.Equal(StorageReconciliationOutcome.Identical, decision.Reconciliation.Outcome);
        }

        [Fact]
        public void IdenticalFullMirrorsRepairAStaleControllerHeader()
        {
            StorageRecoveryRecord older = EmptyRecord(1);
            StorageRecoveryRecord record = EmptyRecord(2);

            StorageControllerRecoveryDecision decision = StorageControllerRecoveryLoader.Prepare(
                StorageSnapshotCopy.Invalid(
                    new byte[] { 9 },
                    record,
                    new StorageRecoveryIndexEntry(older)),
                StorageSnapshotCopy.FromRecord(record),
                new KineticStoragePersistence(null),
                Limits);

            Assert.True(decision.CanOpen);
            Assert.False(decision.RequiresRecovery);
            Assert.Equal(
                StorageReconciliationOutcome.IdenticalMirrorsWithStaleHeader,
                decision.Reconciliation.Outcome);
            Assert.False(decision.Reconciliation.RequiresAdminRecovery);
        }

        [Fact]
        public void AheadCompactHeaderCannotBeRolledBackToOlderFullMirrors()
        {
            StorageRecoveryRecord older = EmptyRecord(1);
            StorageRecoveryRecord ahead = EmptyRecord(2);

            StorageControllerRecoveryDecision decision = StorageControllerRecoveryLoader.Prepare(
                StorageSnapshotCopy.Invalid(
                    new byte[] { 9 },
                    older,
                    new StorageRecoveryIndexEntry(ahead)),
                StorageSnapshotCopy.FromRecord(older),
                new KineticStoragePersistence(null),
                Limits);

            Assert.False(decision.CanOpen);
            Assert.True(decision.RequiresRecovery);
            Assert.NotEqual(
                StorageReconciliationOutcome.IdenticalMirrorsWithStaleHeader,
                decision.Reconciliation.Outcome);
            Assert.Equal(2, decision.Reconciliation.BlockEntityCopy.Header.Revision);
        }

        [Fact]
        public void ConflictingCompactHeaderAtSameRevisionRequiresRecovery()
        {
            StorageRecoveryRecord mirror = EmptyRecord(2);
            StorageRecoveryRecord conflicting = StorageRecoveryRecord.Create(
                WarehouseId,
                Controller,
                2,
                new byte[] { 1, 2, 3, 4 });

            StorageControllerRecoveryDecision decision = StorageControllerRecoveryLoader.Prepare(
                StorageSnapshotCopy.Invalid(
                    new byte[] { 9 },
                    mirror,
                    new StorageRecoveryIndexEntry(conflicting)),
                StorageSnapshotCopy.FromRecord(mirror),
                new KineticStoragePersistence(null),
                Limits);

            Assert.False(decision.CanOpen);
            Assert.True(decision.RequiresRecovery);
        }

        private static StorageRecoveryRecord EmptyRecord(long revision = 1)
        {
            KineticStoragePersistence persistence = new KineticStoragePersistence(null);
            KineticStorageIndex index = new KineticStorageIndex(null, Limits);
            byte[] snapshot = persistence.Encode(persistence.Capture(index));
            return StorageRecoveryRecord.Create(WarehouseId, Controller, revision, snapshot);
        }
    }
}
