using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Vintagestory.API.Common;

namespace VintageKinematics.Storage.Persistence
{
    internal static partial class StorageEntryCodec
    {
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        public static PersistedStorageEntry Create(
            long entryId,
            EnumItemClass itemClass,
            string code,
            byte[] attributeBytes,
            long quantity)
        {
            ValidateFields(entryId, itemClass, code, attributeBytes, quantity);
            byte[] raw = EncodeRaw(entryId, itemClass, code, attributeBytes, quantity);
            return new PersistedStorageEntry(entryId, itemClass, code, attributeBytes, quantity, raw);
        }

        internal static byte[] EncodeRaw(
            long entryId,
            EnumItemClass itemClass,
            string code,
            byte[] attributeBytes,
            long quantity)
        {
            byte[] payload = EncodePayload(entryId, itemClass, code, attributeBytes, quantity);
            byte[] checksum = SHA256.HashData(payload);
            byte[] raw = new byte[payload.Length + checksum.Length];
            Buffer.BlockCopy(payload, 0, raw, 0, payload.Length);
            Buffer.BlockCopy(checksum, 0, raw, payload.Length, checksum.Length);
            return raw;
        }

        private static byte[] EncodePayload(
            long entryId,
            EnumItemClass itemClass,
            string code,
            byte[] attributeBytes,
            long quantity)
        {
            byte[] codeBytes = StrictUtf8.GetBytes(code ?? string.Empty);
            byte[] attributes = attributeBytes ?? Array.Empty<byte>();

            using MemoryStream stream = new MemoryStream();
            using (BinaryWriter writer = new BinaryWriter(stream, StrictUtf8, leaveOpen: true))
            {
                writer.Write(entryId);
                writer.Write((byte)itemClass);
                writer.Write(codeBytes.Length);
                writer.Write(codeBytes);
                writer.Write(attributes.Length);
                writer.Write(attributes);
                writer.Write(quantity);
            }
            return stream.ToArray();
        }

        private static void ValidateFields(
            long entryId,
            EnumItemClass itemClass,
            string code,
            byte[] attributeBytes,
            long quantity)
        {
            if (entryId <= 0) throw new ArgumentOutOfRangeException(nameof(entryId));
            if (!IsSupportedClass(itemClass)) throw new ArgumentOutOfRangeException(nameof(itemClass));
            if (!IsValidCode(code)) throw new ArgumentException("Invalid collectible code.", nameof(code));
            if (StrictUtf8.GetByteCount(code) > StoragePersistenceConstants.MaxCodeBytes)
            {
                throw new ArgumentException("Collectible code is too long.", nameof(code));
            }
            if (attributeBytes == null || attributeBytes.Length == 0
                || attributeBytes.Length > StoragePersistenceConstants.MaxAttributeBytes)
            {
                throw new ArgumentException("Invalid attribute payload.", nameof(attributeBytes));
            }
            if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));
        }

        private static bool IsSupportedClass(EnumItemClass itemClass)
        {
            return itemClass == EnumItemClass.Item || itemClass == EnumItemClass.Block;
        }

        private static bool IsValidCode(string code)
        {
            if (string.IsNullOrEmpty(code)) return false;
            try
            {
                return new AssetLocation(code).Valid;
            }
            catch
            {
                return false;
            }
        }
    }
}
