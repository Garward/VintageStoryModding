namespace VintageKinematics.Storage.Persistence
{
    public enum StorageQuarantineReason
    {
        MalformedSnapshot,
        UnsupportedSchema,
        MalformedRecord,
        ChecksumMismatch,
        InvalidEntryId,
        DuplicateEntryId,
        InvalidItemClass,
        MissingCollectibleCode,
        InvalidQuantity,
        QuantityOverflow,
        InvalidAttributes,
        UnsafeItemState,
        InvalidNextEntryId,
        EntryLimitExceeded
    }
}
