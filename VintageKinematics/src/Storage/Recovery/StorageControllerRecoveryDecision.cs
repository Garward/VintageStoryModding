using VintageKinematics.Storage.Persistence;

namespace VintageKinematics.Storage.Recovery
{
    /// <summary>
    /// Load-time decision from matching full mirrors or explicit recovery.
    /// </summary>
    internal sealed class StorageControllerRecoveryDecision
    {
        public StorageReconciliationResult Reconciliation { get; }
        public StorageRecoveryRecord Record { get; }
        public StorageLoadResult LoadedSnapshot { get; }
        public bool CanOpen => Record != null && LoadedSnapshot != null;
        public bool RequiresRecovery => !CanOpen;

        public StorageControllerRecoveryDecision(
            StorageReconciliationResult reconciliation,
            StorageRecoveryRecord record = null,
            StorageLoadResult loadedSnapshot = null)
        {
            Reconciliation = reconciliation;
            Record = record;
            LoadedSnapshot = loadedSnapshot;
        }
    }
}
