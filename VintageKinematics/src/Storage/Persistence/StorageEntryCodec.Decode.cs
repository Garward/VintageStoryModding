using System;
using System.IO;
using System.Security.Cryptography;
using Vintagestory.API.Common;

namespace VintageKinematics.Storage.Persistence
{
    internal static partial class StorageEntryCodec
    {
        public static bool TryDecode(
            byte[] raw,
            out PersistedStorageEntry entry,
            out QuarantinedStorageEntry quarantine)
        {
            entry = null;
            quarantine = null;
            if (raw == null || raw.Length <= StoragePersistenceConstants.ChecksumSize
                || raw.Length > StoragePersistenceConstants.MaxRecordBytes)
            {
                quarantine = Invalid(StorageQuarantineReason.MalformedRecord, raw);
                return false;
            }

            int payloadLength = raw.Length - StoragePersistenceConstants.ChecksumSize;
            byte[] payload = new byte[payloadLength];
            byte[] savedChecksum = new byte[StoragePersistenceConstants.ChecksumSize];
            Buffer.BlockCopy(raw, 0, payload, 0, payloadLength);
            Buffer.BlockCopy(raw, payloadLength, savedChecksum, 0, savedChecksum.Length);
            if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(payload), savedChecksum))
            {
                quarantine = Invalid(StorageQuarantineReason.ChecksumMismatch, raw);
                return false;
            }

            try
            {
                return TryReadPayload(payload, raw, out entry, out quarantine);
            }
            catch
            {
                quarantine = Invalid(StorageQuarantineReason.MalformedRecord, raw);
                return false;
            }
        }

        private static bool TryReadPayload(
            byte[] payload,
            byte[] raw,
            out PersistedStorageEntry entry,
            out QuarantinedStorageEntry quarantine)
        {
            entry = null;
            quarantine = null;
            using MemoryStream stream = new MemoryStream(payload, writable: false);
            using BinaryReader reader = new BinaryReader(stream, StrictUtf8);

            long entryId = reader.ReadInt64();
            EnumItemClass itemClass = (EnumItemClass)reader.ReadByte();
            string code = ReadCode(reader);
            byte[] attributes = ReadAttributes(reader);
            long quantity = reader.ReadInt64();
            if (stream.Position != stream.Length)
            {
                quarantine = Invalid(StorageQuarantineReason.MalformedRecord, raw);
                return false;
            }

            StorageQuarantineReason? invalidReason = ValidateDecoded(entryId, itemClass, code, attributes, quantity);
            if (invalidReason.HasValue)
            {
                quarantine = Invalid(invalidReason.Value, raw);
                return false;
            }

            entry = new PersistedStorageEntry(entryId, itemClass, code, attributes, quantity, raw);
            return true;
        }

        private static string ReadCode(BinaryReader reader)
        {
            int length = reader.ReadInt32();
            if (length <= 0 || length > StoragePersistenceConstants.MaxCodeBytes)
            {
                throw new InvalidDataException("Invalid code length.");
            }
            return StrictUtf8.GetString(ReadExactBytes(reader, length));
        }

        private static byte[] ReadAttributes(BinaryReader reader)
        {
            int length = reader.ReadInt32();
            if (length <= 0 || length > StoragePersistenceConstants.MaxAttributeBytes)
            {
                throw new InvalidDataException("Invalid attribute length.");
            }
            return ReadExactBytes(reader, length);
        }

        private static byte[] ReadExactBytes(BinaryReader reader, int length)
        {
            byte[] bytes = reader.ReadBytes(length);
            if (bytes.Length != length) throw new EndOfStreamException();
            return bytes;
        }

        private static StorageQuarantineReason? ValidateDecoded(
            long entryId,
            EnumItemClass itemClass,
            string code,
            byte[] attributes,
            long quantity)
        {
            if (entryId <= 0) return StorageQuarantineReason.InvalidEntryId;
            if (!IsSupportedClass(itemClass)) return StorageQuarantineReason.InvalidItemClass;
            if (!IsValidCode(code)) return StorageQuarantineReason.MissingCollectibleCode;
            if (attributes == null || attributes.Length == 0) return StorageQuarantineReason.InvalidAttributes;
            if (quantity <= 0) return StorageQuarantineReason.InvalidQuantity;
            return null;
        }

        private static QuarantinedStorageEntry Invalid(StorageQuarantineReason reason, byte[] raw)
        {
            return new QuarantinedStorageEntry(reason, raw);
        }
    }
}
