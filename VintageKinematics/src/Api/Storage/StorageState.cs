namespace VintageKinematics.Api.Storage
{
    /// <summary>
    /// Authoritative operating state of an indexed warehouse.
    /// </summary>
    public enum StorageState
    {
        Online,
        ManualLocked,
        OverCapacity,
        StructureUnknown,
        RecoveryRequired,
        Corrupt
    }
}
