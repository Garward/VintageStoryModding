using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace VintageKinematics.Storage.Recovery
{
    internal static partial class StorageRecoveryRegistryCodec
    {
        public static StorageRecoveryIndexDecodeResult DecodeIndex(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
            {
                return StorageRecoveryIndexDecodeResult.Failed(StorageRecoveryDecodeError.MissingData);
            }
            if (bytes.Length > StorageRecoveryConstants.MaxIndexBytes)
            {
                return StorageRecoveryIndexDecodeResult.Failed(StorageRecoveryDecodeError.TooLarge);
            }

            try
            {
                using MemoryStream stream = new MemoryStream(bytes, writable: false);
                using BinaryReader reader = new BinaryReader(stream, Encoding.UTF8);
                if (reader.ReadUInt32() != StorageRecoveryConstants.IndexMagic)
                {
                    return StorageRecoveryIndexDecodeResult.Failed(StorageRecoveryDecodeError.Malformed);
                }
                int schemaVersion = reader.ReadInt32();
                if (schemaVersion != 1
                    && schemaVersion != StorageRecoveryConstants.IndexSchemaVersion)
                {
                    return StorageRecoveryIndexDecodeResult.Failed(StorageRecoveryDecodeError.UnsupportedSchema);
                }

                int count = reader.ReadInt32();
                if (count < 0 || count > StorageRecoveryConstants.MaxWarehouses)
                {
                    return StorageRecoveryIndexDecodeResult.Failed(
                        StorageRecoveryDecodeError.WarehouseLimitExceeded);
                }

                List<StorageRecoveryIndexEntry> entries = new List<StorageRecoveryIndexEntry>(count);
                HashSet<string> warehouseIds = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < count; i++)
                {
                    StorageRecoveryIndexEntry entry = ReadIndexEntry(reader, schemaVersion);
                    if (!warehouseIds.Add(entry.WarehouseId))
                    {
                        return StorageRecoveryIndexDecodeResult.Failed(
                            StorageRecoveryDecodeError.DuplicateWarehouseId);
                    }
                    entries.Add(entry);
                }

                if (stream.Position != stream.Length)
                {
                    return StorageRecoveryIndexDecodeResult.Failed(StorageRecoveryDecodeError.Malformed);
                }
                return StorageRecoveryIndexDecodeResult.Succeeded(entries);
            }
            catch
            {
                return StorageRecoveryIndexDecodeResult.Failed(StorageRecoveryDecodeError.Malformed);
            }
        }

        public static StorageRecoveryRecordDecodeResult DecodeRecord(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
            {
                return StorageRecoveryRecordDecodeResult.Failed(StorageRecoveryDecodeError.MissingData);
            }
            if (bytes.Length > StorageRecoveryConstants.MaxRecordBytes)
            {
                return StorageRecoveryRecordDecodeResult.Failed(StorageRecoveryDecodeError.TooLarge);
            }

            try
            {
                using MemoryStream stream = new MemoryStream(bytes, writable: false);
                using BinaryReader reader = new BinaryReader(stream, Encoding.UTF8);
                if (reader.ReadUInt32() != StorageRecoveryConstants.RecordMagic)
                {
                    return StorageRecoveryRecordDecodeResult.Failed(StorageRecoveryDecodeError.Malformed);
                }
                if (reader.ReadInt32() != StorageRecoveryConstants.RecordSchemaVersion)
                {
                    return StorageRecoveryRecordDecodeResult.Failed(
                        StorageRecoveryDecodeError.UnsupportedSchema);
                }

                string warehouseId = ReadWarehouseId(reader);
                StorageControllerLocation controller = ReadController(reader);
                long revision = reader.ReadInt64();
                bool isTombstone = ReadBoolean(reader);
                byte[] checksum = ReadExact(reader, StorageRecoveryChecksum.Size);
                int snapshotLength = reader.ReadInt32();
                if (snapshotLength < 0)
                {
                    return StorageRecoveryRecordDecodeResult.Failed(StorageRecoveryDecodeError.Malformed);
                }
                if (snapshotLength > StorageRecoveryConstants.MaxSnapshotBytes)
                {
                    return StorageRecoveryRecordDecodeResult.Failed(StorageRecoveryDecodeError.TooLarge);
                }
                byte[] snapshot = ReadExact(reader, snapshotLength);
                if (stream.Position != stream.Length)
                {
                    return StorageRecoveryRecordDecodeResult.Failed(StorageRecoveryDecodeError.Malformed);
                }

                StorageRecoveryRecord record = StorageRecoveryRecord.Restore(
                    warehouseId,
                    controller,
                    revision,
                    snapshot,
                    checksum,
                    isTombstone);
                return StorageRecoveryRecordDecodeResult.Succeeded(record);
            }
            catch
            {
                return StorageRecoveryRecordDecodeResult.Failed(StorageRecoveryDecodeError.Malformed);
            }
        }

        private static StorageRecoveryIndexEntry ReadIndexEntry(BinaryReader reader, int schemaVersion)
        {
            string warehouseId = ReadWarehouseId(reader);
            StorageControllerLocation controller = ReadController(reader);
            long revision = reader.ReadInt64();
            bool isTombstone = ReadBoolean(reader);
            byte[] checksum = ReadExact(reader, StorageRecoveryChecksum.Size);
            int recordSlot = -1;
            if (schemaVersion >= 2)
            {
                byte encodedSlot = reader.ReadByte();
                recordSlot = encodedSlot == byte.MaxValue ? -1 : encodedSlot;
            }
            return new StorageRecoveryIndexEntry(
                warehouseId,
                controller,
                revision,
                isTombstone,
                checksum,
                recordSlot);
        }

        private static string ReadWarehouseId(BinaryReader reader)
        {
            return new Guid(ReadExact(reader, 16)).ToString("D");
        }

        private static StorageControllerLocation ReadController(BinaryReader reader)
        {
            return new StorageControllerLocation(
                reader.ReadInt32(),
                reader.ReadInt32(),
                reader.ReadInt32(),
                reader.ReadInt32());
        }

        private static bool ReadBoolean(BinaryReader reader)
        {
            byte value = reader.ReadByte();
            if (value > 1) throw new InvalidDataException("Invalid recovery boolean.");
            return value == 1;
        }

        private static byte[] ReadExact(BinaryReader reader, int length)
        {
            byte[] bytes = reader.ReadBytes(length);
            if (bytes.Length != length) throw new EndOfStreamException();
            return bytes;
        }
    }
}
