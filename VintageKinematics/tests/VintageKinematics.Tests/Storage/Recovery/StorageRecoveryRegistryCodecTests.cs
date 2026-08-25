using System;
using VintageKinematics.Storage.Recovery;
using Xunit;

namespace VintageKinematics.Tests.Storage.Recovery
{
    public class StorageRecoveryRegistryCodecTests
    {
        private const string FirstId = "00000000-0000-0000-0000-000000000001";
        private const string SecondId = "ffffffff-ffff-ffff-ffff-ffffffffffff";
        private static readonly StorageControllerLocation Controller = new(12, 34, -56, 2);

        [Fact]
        public void Index_RoundTripsDeterministicallyInWarehouseIdOrder()
        {
            StorageRecoveryIndexEntry first = new StorageRecoveryIndexEntry(
                CreateRecord(FirstId, 1, 10));
            StorageRecoveryIndexEntry second = new StorageRecoveryIndexEntry(
                CreateRecord(SecondId, 2, 20, isTombstone: true));

            byte[] forward = StorageRecoveryRegistryCodec.EncodeIndex(new[] { first, second });
            byte[] reverse = StorageRecoveryRegistryCodec.EncodeIndex(new[] { second, first });
            StorageRecoveryIndexDecodeResult decoded =
                StorageRecoveryRegistryCodec.DecodeIndex(reverse);

            Assert.Equal(forward, reverse);
            Assert.True(decoded.Success);
            Assert.Equal(FirstId, decoded.Entries[0].WarehouseId);
            Assert.Equal(SecondId, decoded.Entries[1].WarehouseId);
            Assert.True(decoded.Entries[0].Matches(CreateRecord(FirstId, 1, 10)));
            Assert.True(decoded.Entries[1].IsTombstone);
        }

        [Fact]
        public void Record_RoundTripsAllMetadataAndSnapshotBytes()
        {
            StorageRecoveryRecord original = CreateRecord(FirstId, 7, 10);

            StorageRecoveryRecordDecodeResult decoded = StorageRecoveryRegistryCodec.DecodeRecord(
                StorageRecoveryRegistryCodec.EncodeRecord(original));

            Assert.True(decoded.Success);
            Assert.Equal(original.WarehouseId, decoded.Record.WarehouseId);
            Assert.Equal(original.Controller, decoded.Record.Controller);
            Assert.Equal(original.Revision, decoded.Record.Revision);
            Assert.Equal(original.SnapshotBytes, decoded.Record.SnapshotBytes);
            Assert.Equal(original.Checksum, decoded.Record.Checksum);
            Assert.True(decoded.Record.HasValidChecksum);
        }

        [Fact]
        public void Record_InvalidChecksumIsPreservedForReconciliation()
        {
            byte[] bytes = StorageRecoveryRegistryCodec.EncodeRecord(
                CreateRecord(FirstId, 1, 10));
            bytes[49] ^= 0xff;

            StorageRecoveryRecordDecodeResult decoded =
                StorageRecoveryRegistryCodec.DecodeRecord(bytes);

            Assert.True(decoded.Success);
            Assert.False(decoded.Record.HasValidChecksum);
        }

        [Fact]
        public void Tombstone_RoundTripsWithoutSnapshotBytes()
        {
            StorageRecoveryRecord original = CreateRecord(FirstId, 2, 0, isTombstone: true);

            StorageRecoveryRecordDecodeResult decoded = StorageRecoveryRegistryCodec.DecodeRecord(
                StorageRecoveryRegistryCodec.EncodeRecord(original));

            Assert.True(decoded.Success);
            Assert.True(decoded.Record.IsTombstone);
            Assert.Empty(decoded.Record.SnapshotBytes);
            Assert.True(decoded.Record.HasValidChecksum);
        }

        [Fact]
        public void DuplicateIndexWarehouseId_IsRejectedDuringDecode()
        {
            StorageRecoveryIndexEntry first = new StorageRecoveryIndexEntry(
                CreateRecord(FirstId, 1, 10));
            StorageRecoveryIndexEntry second = new StorageRecoveryIndexEntry(
                CreateRecord(SecondId, 2, 20));
            byte[] bytes = StorageRecoveryRegistryCodec.EncodeIndex(new[] { first, second });
            const int headerLength = 12;
            const int entryLength = 74;
            Buffer.BlockCopy(bytes, headerLength, bytes, headerLength + entryLength, entryLength);

            StorageRecoveryIndexDecodeResult decoded =
                StorageRecoveryRegistryCodec.DecodeIndex(bytes);

            Assert.False(decoded.Success);
            Assert.Equal(StorageRecoveryDecodeError.DuplicateWarehouseId, decoded.Error);
        }

        [Fact]
        public void DeclaredOversizedSnapshot_IsRejectedBeforeAllocation()
        {
            byte[] bytes = StorageRecoveryRegistryCodec.EncodeRecord(
                CreateRecord(FirstId, 1, 10));
            const int snapshotLengthOffset = 81;
            Buffer.BlockCopy(
                BitConverter.GetBytes(StorageRecoveryConstants.MaxSnapshotBytes + 1),
                0,
                bytes,
                snapshotLengthOffset,
                sizeof(int));

            StorageRecoveryRecordDecodeResult decoded =
                StorageRecoveryRegistryCodec.DecodeRecord(bytes);

            Assert.False(decoded.Success);
            Assert.Equal(StorageRecoveryDecodeError.TooLarge, decoded.Error);
        }

        [Fact]
        public void UnsupportedSchema_IsReportedWithoutReadingContents()
        {
            byte[] bytes = StorageRecoveryRegistryCodec.EncodeIndex(
                Array.Empty<StorageRecoveryIndexEntry>());
            bytes[4] = 3;

            StorageRecoveryIndexDecodeResult decoded =
                StorageRecoveryRegistryCodec.DecodeIndex(bytes);

            Assert.False(decoded.Success);
            Assert.Equal(StorageRecoveryDecodeError.UnsupportedSchema, decoded.Error);
        }

        [Fact]
        public void SchemaOneIndexWithoutRecordSlotsRemainsReadable()
        {
            byte[] current = StorageRecoveryRegistryCodec.EncodeIndex(
                new[] { new StorageRecoveryIndexEntry(CreateRecord(FirstId, 1, 10)) });
            byte[] schemaOne = new byte[current.Length - 1];
            Buffer.BlockCopy(current, 0, schemaOne, 0, schemaOne.Length);
            schemaOne[4] = 1;

            StorageRecoveryIndexDecodeResult decoded =
                StorageRecoveryRegistryCodec.DecodeIndex(schemaOne);

            Assert.True(decoded.Success);
            Assert.Equal(-1, Assert.Single(decoded.Entries).RecordSlot);
        }

        [Fact]
        public void TrailingRecordData_IsRejected()
        {
            byte[] encoded = StorageRecoveryRegistryCodec.EncodeRecord(
                CreateRecord(FirstId, 1, 10));
            byte[] withTrailingData = new byte[encoded.Length + 1];
            Buffer.BlockCopy(encoded, 0, withTrailingData, 0, encoded.Length);

            StorageRecoveryRecordDecodeResult decoded =
                StorageRecoveryRegistryCodec.DecodeRecord(withTrailingData);

            Assert.False(decoded.Success);
            Assert.Equal(StorageRecoveryDecodeError.Malformed, decoded.Error);
        }

        private static StorageRecoveryRecord CreateRecord(
            string id,
            long revision,
            byte value,
            bool isTombstone = false)
        {
            byte[] snapshot = isTombstone ? Array.Empty<byte>() : new[] { value };
            return StorageRecoveryRecord.Create(id, Controller, revision, snapshot, isTombstone);
        }
    }
}
