using System;
using System.Collections.Generic;

namespace VintageKinematics.Storage.Recovery
{
    internal sealed class StorageRecoveryIndexDecodeResult
    {
        private readonly IReadOnlyList<StorageRecoveryIndexEntry> entries;

        public bool Success => Error == StorageRecoveryDecodeError.None;
        public StorageRecoveryDecodeError Error { get; }
        public IReadOnlyList<StorageRecoveryIndexEntry> Entries => entries;

        private StorageRecoveryIndexDecodeResult(
            StorageRecoveryDecodeError error,
            IReadOnlyList<StorageRecoveryIndexEntry> entries)
        {
            Error = error;
            this.entries = entries ?? Array.Empty<StorageRecoveryIndexEntry>();
        }

        public static StorageRecoveryIndexDecodeResult Succeeded(
            IReadOnlyList<StorageRecoveryIndexEntry> entries)
        {
            return new StorageRecoveryIndexDecodeResult(StorageRecoveryDecodeError.None, entries);
        }

        public static StorageRecoveryIndexDecodeResult Failed(StorageRecoveryDecodeError error)
        {
            return new StorageRecoveryIndexDecodeResult(error, null);
        }
    }
}
