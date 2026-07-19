namespace VintageKinematics.Api.Storage
{
    public enum StorageChangeReason
    {
        Unknown,
        Insert,
        Extract,
        StructureChanged,
        ManualRebuild,
        Loaded,
        Recovery,
        AdminLock,
        AdminUnlock
    }
}
