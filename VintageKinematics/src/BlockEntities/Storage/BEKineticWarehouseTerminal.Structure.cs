using System.Collections.Generic;
using System.Linq;
using VintageKinematics.Api.Storage;
using VintageKinematics.Storage.Topology;

namespace VintageKinematics.BlockEntities.Storage
{
    public partial class BEKineticWarehouseTerminal
    {
        private void RebuildStructureNow(StorageChangeReason reason)
        {
            lock (persistenceSync)
            {
                RebuildStructureNowSynchronized(reason);
            }
        }

        private void RebuildStructureNowSynchronized(StorageChangeReason reason)
        {
            rebuildInProgress = true;
            try
            {
                WorldStorageTopologySource source = new WorldStorageTopologySource(Api.World.BlockAccessor);
                PruneConfirmedAbsentMembers(source);
                StorageStructureScanRequest request = new StorageStructureScanRequest(
                    WorldStorageTopologySource.FromBlockPos(Pos),
                    WarehouseId,
                    new StorageTopologyLimits(),
                    knownMembers);
                StorageStructureSnapshot snapshot = structureScanner.Scan(source, request);
                LastStructureSnapshot = snapshot;

                if (!snapshot.IsComplete || !snapshot.IsValid)
                {
                    ApplyStructureUnknown();
                    return;
                }

                List<StorageTopologyPosition> discovered = snapshot.Members
                    .Select(member => member.Position)
                    .ToList();
                bool changed = VerifiedItemCapacity != snapshot.ItemCapacity
                    || VerifiedTypeCapacity != snapshot.TypeCapacity
                    || StructureState == StorageState.StructureUnknown
                    || !knownMembers.SequenceEqual(discovered);

                VerifiedItemCapacity = snapshot.ItemCapacity;
                VerifiedTypeCapacity = snapshot.TypeCapacity;
                if (StructureState == StorageState.StructureUnknown && itemIndex != null)
                {
                    StructureState = StorageState.Online;
                }
                knownMembers.Clear();
                knownMembers.AddRange(discovered);
                RefreshDrivePowerFromMembers();
                UpdateIndexLimitsAndState();
                if (changed) MarkDirty();
            }
            finally
            {
                rebuildInProgress = false;
            }
        }

        internal bool ForgetKnownMember(StorageTopologyPosition removed)
        {
            lock (persistenceSync)
            {
                return knownMembers.RemoveAll(member => member == removed) > 0;
            }
        }

        internal void ConfirmStorageMemberRemoved(
            Vintagestory.API.MathTools.BlockPos removed,
            string warehouseId)
        {
            lock (persistenceSync)
            {
                if (Api?.Side != Vintagestory.API.Common.EnumAppSide.Server
                    || removed == null
                    || warehouseId != WarehouseId)
                {
                    return;
                }

                if (ForgetKnownMember(WorldStorageTopologySource.FromBlockPos(removed)))
                {
                    MarkDirty();
                }
                RequestStructureRebuild(StorageChangeReason.StructureChanged);
            }
        }

        private void PruneConfirmedAbsentMembers(WorldStorageTopologySource source)
        {
            bool changed = knownMembers.RemoveAll(member =>
                member != WorldStorageTopologySource.FromBlockPos(Pos)
                && source.IsConfirmedStorageMemberAbsent(member)) > 0;
            if (changed) MarkDirty();
        }

        private void ApplyStructureUnknown()
        {
            if (StructureState == StorageState.StructureUnknown
                || StructureState == StorageState.RecoveryRequired
                || StructureState == StorageState.Corrupt)
            {
                return;
            }
            StructureState = StorageState.StructureUnknown;
            MarkDirty();
        }
    }
}
