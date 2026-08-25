using VintageKinematics.Storage.Index;
using VintageKinematics.Storage.Persistence;

namespace VintageKinematics.Storage.Recovery
{
    /// <summary>Proves when abandoned recovery evidence can be retired without item risk.</summary>
    internal static class StorageOrphanRecoveryPolicy
    {
        public static bool CanTombstoneEmptyMirrors(
            StorageRecoveryRecord recovery,
            StorageRecoveryRecord controller,
            KineticStoragePersistence persistence)
        {
            if (recovery == null
                || controller == null
                || persistence == null
                || recovery.IsTombstone
                || controller.IsTombstone
                || !recovery.IsEquivalentTo(controller)
                || !recovery.HasValidChecksum)
            {
                return false;
            }

            StorageLoadResult loaded = persistence.Load(
                recovery.SnapshotBytes,
                new StorageIndexLimits(long.MaxValue));
            return !loaded.HasCorruption
                && loaded.Index.StoredItems == 0
                && loaded.UnresolvedEntries.Count == 0;
        }
    }
}
