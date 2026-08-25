using System.Collections.Generic;
using VintageKinematics.Api.Storage;
using VintageKinematics.Storage.Index;
using VintageKinematics.Storage.Persistence;
using VintageKinematics.Storage.Recovery;
using VintageKinematics.Api;

namespace VintageKinematics.BlockEntities.Storage
{
    public partial class BEKineticWarehouseTerminal
    {
        private readonly List<UnresolvedStorageEntry> unresolvedEntries =
            new List<UnresolvedStorageEntry>();
        private KineticStorageIndex itemIndex;
        private StorageRecoveryRecord activeRecoveryRecord;
        private StorageSnapshotCopy persistedItemCopy = StorageSnapshotCopy.Missing();
        private StorageRecoveryIndexEntry persistedItemHeader;
        private byte[] persistedItemHeaderBytes = System.Array.Empty<byte>();
        private bool recoveryInitializationRequested;

        public KineticStorageIndex ItemIndex => itemIndex;
        public StorageReconciliationResult LastReconciliation { get; private set; }
        public bool IsItemIndexReady => itemIndex != null && StructureState == StorageState.Online;
        public int EffectiveTypeCapacity => itemIndex?.Limits.TypeCapacity ?? CurrentIndexLimits().TypeCapacity;
        internal StorageSnapshotCopy PersistedItemCopy => persistedItemCopy;
        internal StorageRecoveryIndexEntry PersistedItemHeader => persistedItemHeader;

        private StorageIndexLimits CurrentIndexLimits()
        {
            int configured = System.Math.Max(
                0,
                Api?.ModLoader.GetModSystem<KineticConfigSystem>()?.Config?.StorageMaxTypesPerNetwork
                ?? 0);
            int typeCapacity = VerifiedTypeCapacity <= 0
                ? configured
                : configured <= 0
                    ? VerifiedTypeCapacity
                    : System.Math.Min(VerifiedTypeCapacity, configured);
            return new StorageIndexLimits(VerifiedItemCapacity, typeCapacity);
        }

        private void UpdateIndexLimitsAndState()
        {
            if (itemIndex == null) return;
            itemIndex.UpdateLimits(CurrentIndexLimits());
            if (StructureState == StorageState.StructureUnknown) return;
            if (StructureState == StorageState.RecoveryRequired
                || StructureState == StorageState.Corrupt
                || StructureState == StorageState.ManualLocked)
            {
                return;
            }
            bool overItemCapacity = itemIndex.StoredItems > VerifiedItemCapacity;
            bool overTypeCapacity = itemIndex.Limits.TypeCapacity > 0
                && itemIndex.EntryCount > itemIndex.Limits.TypeCapacity;
            StructureState = overItemCapacity || overTypeCapacity
                ? StorageState.OverCapacity
                : StorageState.Online;
        }
    }
}
