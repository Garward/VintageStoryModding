using System.Collections.Generic;
using VintageKinematics.Api.Storage;
using VintageKinematics.Storage.Topology;

namespace VintageKinematics.BlockEntities.Storage
{
    /// <summary>
    /// Structure owner. Item index and transaction integration remain separate partials.
    /// </summary>
    public partial class BEKineticWarehouseTerminal : BEKineticStorageMember, IVKStorageRemovalGuard
    {
        private readonly List<StorageTopologyPosition> knownMembers =
            new List<StorageTopologyPosition>();
        private readonly StorageStructureScanner structureScanner = new StorageStructureScanner();
        private bool rebuildScheduled;
        private bool structureDirty;
        private bool rebuildInProgress;

        public override bool IsController => true;
        public override long CapacityContribution => 256;
        public StorageState StructureState { get; private set; } = StorageState.StructureUnknown;
        public long VerifiedItemCapacity { get; private set; }
        public int VerifiedTypeCapacity { get; private set; }
        public long SyncedStoredItems { get; private set; }
        public int SyncedEntryCount { get; private set; }
        public StorageStructureSnapshot LastStructureSnapshot { get; private set; }
        public IReadOnlyList<StorageTopologyPosition> KnownMembers => knownMembers;
    }
}
