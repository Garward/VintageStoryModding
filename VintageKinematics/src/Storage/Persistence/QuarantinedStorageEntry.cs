using System;

namespace VintageKinematics.Storage.Persistence
{
    public sealed class QuarantinedStorageEntry
    {
        private readonly byte[] rawBytes;

        public StorageQuarantineReason Reason { get; }
        public string Detail { get; }
        public byte[] RawBytes => (byte[])rawBytes.Clone();

        public QuarantinedStorageEntry(
            StorageQuarantineReason reason,
            byte[] rawBytes,
            string detail = null)
        {
            Reason = reason;
            this.rawBytes = (byte[])(rawBytes?.Clone() ?? Array.Empty<byte>());
            Detail = detail ?? string.Empty;
        }
    }
}
