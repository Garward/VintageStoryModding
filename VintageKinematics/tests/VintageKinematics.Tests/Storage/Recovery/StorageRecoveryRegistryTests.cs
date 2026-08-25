using System;
using System.Linq;
using VintageKinematics.Storage.Recovery;
using Xunit;

namespace VintageKinematics.Tests.Storage.Recovery
{
    public class StorageRecoveryRegistryTests
    {
        private const string FirstId = "cbb91396-f8c6-4992-8447-504841a13ed9";
        private static readonly StorageControllerLocation Controller = new(12, 34, -56, 2);

        [Fact]
        public void Upsert_AddsRecordAndMarksRegistryDirty()
        {
            StorageRecoveryRegistry registry = new StorageRecoveryRegistry();
            StorageRecoveryRecord record = CreateRecord(FirstId, 1, 10);

            Assert.True(registry.Upsert(record));
            Assert.True(registry.IsDirty);
            Assert.True(registry.TryGet(FirstId.ToUpperInvariant(), out StorageRecoveryRecord found));
            Assert.Same(record, found);

            StorageRecoverySaveBatch batch = registry.CaptureSaveBatch();
            Assert.True(batch.RequiresIndexWrite);
            Assert.Same(record, Assert.Single(batch.DirtyRecords));
            Assert.True(Assert.Single(batch.IndexEntries).Matches(record));
        }

        [Fact]
        public void EquivalentRecord_DoesNotCreateAnotherSave()
        {
            StorageRecoveryRegistry registry = new StorageRecoveryRegistry();
            registry.Upsert(CreateRecord(FirstId, 1, 10));
            registry.AcknowledgeSaved(registry.CaptureSaveBatch());

            bool changed = registry.Upsert(CreateRecord(FirstId, 1, 10));

            Assert.False(changed);
            Assert.False(registry.IsDirty);
        }

        [Fact]
        public void AcknowledgeSaved_DoesNotClearANewerRevision()
        {
            StorageRecoveryRegistry registry = new StorageRecoveryRegistry();
            registry.Upsert(CreateRecord(FirstId, 1, 10));
            StorageRecoverySaveBatch oldBatch = registry.CaptureSaveBatch();
            registry.Upsert(CreateRecord(FirstId, 2, 20));

            registry.AcknowledgeSaved(oldBatch);

            Assert.True(registry.IsDirty);
            StorageRecoverySaveBatch currentBatch = registry.CaptureSaveBatch();
            Assert.Equal(2, Assert.Single(currentBatch.DirtyRecords).Revision);
            Assert.True(currentBatch.RequiresIndexWrite);

            registry.AcknowledgeSaved(currentBatch);
            Assert.False(registry.IsDirty);
        }

        [Fact]
        public void Tombstone_IsDirtyAndCannotBeSilentlyResurrected()
        {
            StorageRecoveryRegistry registry = new StorageRecoveryRegistry();
            registry.Upsert(CreateRecord(FirstId, 1, 10));
            registry.AcknowledgeSaved(registry.CaptureSaveBatch());

            StorageRecoveryRecord tombstone = registry.Tombstone(FirstId, 2);

            Assert.True(tombstone.IsTombstone);
            Assert.Empty(tombstone.SnapshotBytes);
            Assert.True(registry.IsDirty);
            Assert.Throws<InvalidOperationException>(() =>
                registry.Upsert(CreateRecord(FirstId, 3, 30)));
        }

        [Fact]
        public void Restore_AcceptsInvalidEvidenceWithoutMarkingItSavedOrDirty()
        {
            StorageRecoveryRecord invalid = StorageRecoveryRecord.Restore(
                FirstId,
                Controller,
                1,
                new byte[] { 10 },
                new byte[32],
                false);
            StorageRecoveryRegistry registry = new StorageRecoveryRegistry();

            registry.Restore(invalid);

            Assert.False(invalid.HasValidChecksum);
            Assert.False(registry.IsDirty);
            Assert.Empty(registry.CaptureSaveBatch().DirtyRecords);
        }

        [Fact]
        public void RevisionRegression_IsRejected()
        {
            StorageRecoveryRegistry registry = new StorageRecoveryRegistry();
            registry.Upsert(CreateRecord(FirstId, 2, 20));

            Assert.Throws<InvalidOperationException>(() =>
                registry.Upsert(CreateRecord(FirstId, 1, 10)));
        }

        [Fact]
        public void SameRevisionWithDifferentContents_IsRejected()
        {
            StorageRecoveryRegistry registry = new StorageRecoveryRegistry();
            registry.Upsert(CreateRecord(FirstId, 1, 10));

            Assert.Throws<InvalidOperationException>(() =>
                registry.Upsert(CreateRecord(FirstId, 1, 20)));
        }

        [Fact]
        public void ExplicitRecoveryCanSupersedeATombstoneWithANewerLiveRevision()
        {
            StorageRecoveryRegistry registry = new StorageRecoveryRegistry();
            registry.Upsert(CreateRecord(FirstId, 1, 10));
            registry.Tombstone(FirstId, 2);
            StorageRecoveryRecord recovered = CreateRecord(FirstId, 3, 10);

            registry.ReplaceAfterExplicitRecovery(recovered);

            Assert.True(registry.TryGet(FirstId, out StorageRecoveryRecord stored));
            Assert.False(stored.IsTombstone);
            Assert.Equal(3, stored.Revision);
        }

        [Fact]
        public void ExplicitRecoveryStillRequiresANewerRevision()
        {
            StorageRecoveryRegistry registry = new StorageRecoveryRegistry();
            registry.Upsert(CreateRecord(FirstId, 2, 10));

            Assert.Throws<InvalidOperationException>(() =>
                registry.ReplaceAfterExplicitRecovery(CreateRecord(FirstId, 2, 20)));
        }

        [Fact]
        public void ControllerMove_IsRejected()
        {
            StorageRecoveryRegistry registry = new StorageRecoveryRegistry();
            registry.Upsert(CreateRecord(FirstId, 1, 10));
            StorageRecoveryRecord moved = StorageRecoveryRecord.Create(
                FirstId,
                new StorageControllerLocation(13, 34, -56, 2),
                2,
                new byte[] { 20 });

            Assert.Throws<InvalidOperationException>(() => registry.Upsert(moved));
        }

        [Fact]
        public void Records_AreReturnedInCanonicalIdOrder()
        {
            StorageRecoveryRegistry registry = new StorageRecoveryRegistry();
            registry.Upsert(CreateRecord("ffffffff-ffff-ffff-ffff-ffffffffffff", 1, 1));
            registry.Upsert(CreateRecord("00000000-0000-0000-0000-000000000001", 1, 2));

            string[] ids = registry.GetRecords().Select(record => record.WarehouseId).ToArray();

            Assert.Equal("00000000-0000-0000-0000-000000000001", ids[0]);
            Assert.Equal("ffffffff-ffff-ffff-ffff-ffffffffffff", ids[1]);
        }

        private static StorageRecoveryRecord CreateRecord(string id, long revision, byte value)
        {
            return StorageRecoveryRecord.Create(id, Controller, revision, new[] { value });
        }
    }
}
