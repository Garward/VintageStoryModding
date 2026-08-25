namespace VintageKinematics.Storage.Recovery
{
    public enum StorageReconciliationOutcome
    {
        Identical,
        IdenticalMirrorsWithStaleHeader,
        SingleValidCopy,
        Divergent,
        IdentityConflict,
        TombstoneConflict,
        NoValidCopy
    }
}
