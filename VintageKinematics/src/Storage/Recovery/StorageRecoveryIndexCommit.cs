using System;

namespace VintageKinematics.Storage.Recovery
{
    internal sealed class StorageRecoveryIndexCommit
    {
        private readonly byte[] indexBytes;

        public long Generation { get; }
        public byte[] IndexBytes => (byte[])indexBytes.Clone();

        public StorageRecoveryIndexCommit(long generation, byte[] indexBytes)
        {
            if (generation <= 0) throw new ArgumentOutOfRangeException(nameof(generation));
            Generation = generation;
            this.indexBytes = (byte[])(indexBytes?.Clone()
                ?? throw new ArgumentNullException(nameof(indexBytes)));
        }
    }
}
