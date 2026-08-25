namespace VintageKinematics.Storage.Recovery
{
    internal sealed class StorageRecoveryRecordDecodeResult
    {
        public bool Success => Error == StorageRecoveryDecodeError.None;
        public StorageRecoveryDecodeError Error { get; }
        public StorageRecoveryRecord Record { get; }

        private StorageRecoveryRecordDecodeResult(
            StorageRecoveryDecodeError error,
            StorageRecoveryRecord record)
        {
            Error = error;
            Record = record;
        }

        public static StorageRecoveryRecordDecodeResult Succeeded(StorageRecoveryRecord record)
        {
            return new StorageRecoveryRecordDecodeResult(StorageRecoveryDecodeError.None, record);
        }

        public static StorageRecoveryRecordDecodeResult Failed(StorageRecoveryDecodeError error)
        {
            return new StorageRecoveryRecordDecodeResult(error, null);
        }
    }
}
