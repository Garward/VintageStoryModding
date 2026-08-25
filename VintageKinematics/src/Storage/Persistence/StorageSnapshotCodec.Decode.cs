using System;
using System.IO;
using System.Text;

namespace VintageKinematics.Storage.Persistence
{
    internal static partial class StorageSnapshotCodec
    {
        public static StorageSnapshotDecodeResult Decode(byte[] bytes)
        {
            StorageSnapshotDecodeResult result = new StorageSnapshotDecodeResult();
            if (bytes == null || bytes.Length == 0
                || bytes.Length > StoragePersistenceConstants.MaxSnapshotBytes)
            {
                AddSnapshotError(result, bytes, StorageQuarantineReason.MalformedSnapshot);
                return result;
            }

            try
            {
                DecodeContents(bytes, result);
            }
            catch
            {
                AddSnapshotError(result, bytes, StorageQuarantineReason.MalformedSnapshot);
            }
            return result;
        }

        private static void DecodeContents(byte[] bytes, StorageSnapshotDecodeResult result)
        {
            using MemoryStream stream = new MemoryStream(bytes, writable: false);
            using BinaryReader reader = new BinaryReader(stream, Encoding.UTF8);
            if (reader.ReadUInt32() != StoragePersistenceConstants.SnapshotMagic)
            {
                AddSnapshotError(result, bytes, StorageQuarantineReason.MalformedSnapshot);
                return;
            }

            result.SchemaVersion = reader.ReadInt32();
            if (result.SchemaVersion != StoragePersistenceConstants.SchemaVersion)
            {
                AddSnapshotError(result, bytes, StorageQuarantineReason.UnsupportedSchema);
                return;
            }

            result.NextEntryId = reader.ReadInt64();
            int count = reader.ReadInt32();
            if (count < 0 || count > StoragePersistenceConstants.MaxSnapshotEntries)
            {
                AddSnapshotError(result, bytes, StorageQuarantineReason.EntryLimitExceeded);
                return;
            }

            for (int i = 0; i < count; i++) DecodeRecord(reader, result);
            if (stream.Position != stream.Length)
            {
                AddSnapshotError(result, bytes, StorageQuarantineReason.MalformedSnapshot);
            }
        }

        private static void DecodeRecord(BinaryReader reader, StorageSnapshotDecodeResult result)
        {
            int length = reader.ReadInt32();
            if (length <= 0 || length > StoragePersistenceConstants.MaxRecordBytes)
            {
                throw new InvalidDataException("Invalid storage record length.");
            }

            byte[] raw = reader.ReadBytes(length);
            if (raw.Length != length) throw new EndOfStreamException();
            if (StorageEntryCodec.TryDecode(raw, out PersistedStorageEntry entry, out QuarantinedStorageEntry invalid))
            {
                result.Entries.Add(entry);
            }
            else
            {
                result.QuarantinedEntries.Add(invalid);
            }
        }

        private static void AddSnapshotError(
            StorageSnapshotDecodeResult result,
            byte[] bytes,
            StorageQuarantineReason reason)
        {
            result.QuarantinedEntries.Add(new QuarantinedStorageEntry(reason, bytes));
        }
    }
}
