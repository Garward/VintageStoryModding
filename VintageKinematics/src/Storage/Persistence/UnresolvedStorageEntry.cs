namespace VintageKinematics.Storage.Persistence
{
    public sealed class UnresolvedStorageEntry
    {
        public PersistedStorageEntry Record { get; }

        public UnresolvedStorageEntry(PersistedStorageEntry record)
        {
            Record = record;
        }
    }
}
