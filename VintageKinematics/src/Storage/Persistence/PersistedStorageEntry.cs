using System;
using Vintagestory.API.Common;

namespace VintageKinematics.Storage.Persistence
{
    /// <summary>
    /// Validated schema-version-one entry plus its exact encoded record bytes.
    /// </summary>
    public sealed class PersistedStorageEntry
    {
        private readonly byte[] attributeBytes;
        private readonly byte[] rawRecordBytes;

        public long EntryId { get; }
        public EnumItemClass ItemClass { get; }
        public string Code { get; }
        public byte[] AttributeBytes => (byte[])attributeBytes.Clone();
        public long Quantity { get; }
        public byte[] RawRecordBytes => (byte[])rawRecordBytes.Clone();

        internal PersistedStorageEntry(
            long entryId,
            EnumItemClass itemClass,
            string code,
            byte[] attributeBytes,
            long quantity,
            byte[] rawRecordBytes)
        {
            EntryId = entryId;
            ItemClass = itemClass;
            Code = code;
            this.attributeBytes = (byte[])(attributeBytes?.Clone() ?? Array.Empty<byte>());
            Quantity = quantity;
            this.rawRecordBytes = (byte[])(rawRecordBytes?.Clone() ?? Array.Empty<byte>());
        }
    }
}
