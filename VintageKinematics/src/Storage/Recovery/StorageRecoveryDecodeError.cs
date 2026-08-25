namespace VintageKinematics.Storage.Recovery
{
    internal enum StorageRecoveryDecodeError
    {
        None,
        MissingData,
        TooLarge,
        UnsupportedSchema,
        WarehouseLimitExceeded,
        DuplicateWarehouseId,
        Malformed
    }
}
