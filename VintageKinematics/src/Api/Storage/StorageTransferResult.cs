namespace VintageKinematics.Api.Storage
{
    public enum StorageTransferStatus
    {
        Ok,
        EmptyInput,
        Full,
        TypeLimitReached,
        Locked,
        StructureUnknown,
        RecoveryRequired,
        Corrupt,
        Busy,
        ItemRejected,
        NotFound,
        InvalidQuantity,
        QuantityOverflow,
        Unpowered
    }

    public readonly struct StorageTransferResult
    {
        public readonly StorageTransferStatus Status;
        public readonly int Moved;
        public readonly string MessageLangCode;

        public StorageTransferResult(StorageTransferStatus status, int moved = 0, string messageLangCode = null)
        {
            Status = status;
            Moved = moved;
            MessageLangCode = messageLangCode;
        }

        public bool Success => Status == StorageTransferStatus.Ok && Moved > 0;

        public static StorageTransferResult Ok(int moved) => new StorageTransferResult(StorageTransferStatus.Ok, moved);
        public static StorageTransferResult Fail(StorageTransferStatus status, string messageLangCode = null) => new StorageTransferResult(status, 0, messageLangCode);
    }
}
