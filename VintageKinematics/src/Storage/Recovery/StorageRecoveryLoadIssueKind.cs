namespace VintageKinematics.Storage.Recovery
{
    public enum StorageRecoveryLoadIssueKind
    {
        IndexMalformed,
        IndexUnsupportedSchema,
        IndexTooLarge,
        IndexWarehouseLimitExceeded,
        IndexDuplicateWarehouseId,
        RecordMissing,
        RecordMalformed,
        RecordUnsupportedSchema,
        RecordTooLarge,
        RecordIndexMismatch,
        RecordInvalidChecksum
    }
}
