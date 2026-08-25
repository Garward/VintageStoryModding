using System;
using VintageKinematics.Storage.Recovery;
using Xunit;

namespace VintageKinematics.Tests.Storage.Recovery
{
    public class StorageRecoveryRecordTests
    {
        private const string WarehouseId = "cbb91396-f8c6-4992-8447-504841a13ed9";
        private static readonly StorageControllerLocation Controller = new(12, 34, -56, 2);

        [Fact]
        public void Create_NormalizesWarehouseIdAndComputesChecksum()
        {
            StorageRecoveryRecord record = StorageRecoveryRecord.Create(
                WarehouseId.ToUpperInvariant(),
                Controller,
                7,
                new byte[] { 1, 2, 3 });

            Assert.Equal(WarehouseId, record.WarehouseId);
            Assert.Equal(Controller, record.Controller);
            Assert.Equal(7, record.Revision);
            Assert.False(record.IsTombstone);
            Assert.True(record.HasValidChecksum);
            Assert.Equal(64, record.ChecksumHex.Length);
        }

        [Fact]
        public void Record_DefensivelyCopiesSnapshotAndChecksumBytes()
        {
            byte[] snapshot = new byte[] { 1, 2, 3 };
            StorageRecoveryRecord record = StorageRecoveryRecord.Create(
                WarehouseId,
                Controller,
                1,
                snapshot);
            byte[] checksum = record.Checksum;

            snapshot[0] = 9;
            checksum[0] ^= 0xff;
            record.SnapshotBytes[1] = 9;

            Assert.Equal(new byte[] { 1, 2, 3 }, record.SnapshotBytes);
            Assert.True(record.HasValidChecksum);
        }

        [Fact]
        public void Restore_PreservesInvalidChecksumForFailClosedLoading()
        {
            StorageRecoveryRecord record = StorageRecoveryRecord.Restore(
                WarehouseId,
                Controller,
                1,
                new byte[] { 1, 2, 3 },
                new byte[32],
                false);

            Assert.False(record.HasValidChecksum);
        }

        [Fact]
        public void Tombstone_CanOmitSnapshotBytes()
        {
            StorageRecoveryRecord record = StorageRecoveryRecord.Create(
                WarehouseId,
                Controller,
                2,
                Array.Empty<byte>(),
                isTombstone: true);

            Assert.True(record.IsTombstone);
            Assert.True(record.HasValidChecksum);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("not-a-uuid")]
        [InlineData("00000000-0000-0000-0000-000000000000")]
        public void InvalidWarehouseId_IsRejected(string warehouseId)
        {
            Assert.Throws<ArgumentException>(() => StorageRecoveryRecord.Create(
                warehouseId,
                Controller,
                1,
                new byte[] { 1 }));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void NonPositiveRevision_IsRejected(long revision)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => StorageRecoveryRecord.Create(
                WarehouseId,
                Controller,
                revision,
                new byte[] { 1 }));
        }

        [Fact]
        public void LiveRecordWithoutSnapshot_IsRejected()
        {
            Assert.Throws<ArgumentException>(() => StorageRecoveryRecord.Create(
                WarehouseId,
                Controller,
                1,
                Array.Empty<byte>()));
        }

        [Fact]
        public void ControllerLocation_UsesValueEquality()
        {
            StorageControllerLocation same = new(12, 34, -56, 2);
            StorageControllerLocation differentDimension = new(12, 34, -56, 3);

            Assert.Equal(Controller, same);
            Assert.True(Controller == same);
            Assert.NotEqual(Controller, differentDimension);
            Assert.True(Controller != differentDimension);
        }
    }
}
