using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using VintageKinematics.Api.Storage;
using VintageKinematics.Storage.Recovery;
using VintageKinematics.Storage.Topology;
using VintageKinematics.Storage;

namespace VintageKinematics.BlockEntities.Storage
{
    public partial class BEKineticWarehouseTerminal
    {
        public StorageRemovalCheck CanRemoveStorageBlock(
            BlockPos pos,
            StorageRemovalKind kind,
            IPlayer byPlayer = null)
        {
            lock (persistenceSync)
            {
                return CanRemoveStorageBlockSynchronized(pos, kind, byPlayer);
            }
        }

        private StorageRemovalCheck CanRemoveStorageBlockSynchronized(
            BlockPos pos,
            StorageRemovalKind kind,
            IPlayer byPlayer)
        {
            long stored = itemIndex?.StoredItems ?? 0;
            StorageTopologyPosition target = WorldStorageTopologySource.FromBlockPos(pos);
            StorageTopologyPosition controller = WorldStorageTopologySource.FromBlockPos(Pos);
            if (target == controller
                && (kind == StorageRemovalKind.PlayerBreak
                    || kind == StorageRemovalKind.WrenchPickup))
            {
                return HasSafeRelocationEvidence()
                    ? StorageRemovalCheck.Allow(
                        pos,
                        stored,
                        VerifiedItemCapacity,
                        System.Math.Max(0, VerifiedItemCapacity - CapacityContribution))
                    : DenyUnknown(pos, stored);
            }

            if (!StorageRemovalPolicy.CanEvaluateCapacity(
                StructureState,
                IsItemIndexReady,
                rebuildInProgress,
                kind))
            {
                return DenyUnknown(pos, stored);
            }

            if (target == controller)
            {
                return stored == 0
                    ? StorageRemovalCheck.Allow(pos, 0, VerifiedItemCapacity, 0)
                    : StorageRemovalCheck.Deny(
                        pos,
                        stored,
                        VerifiedItemCapacity,
                        0,
                        "vintagekinematics:storage-removal-would-overflow");
            }
            if (!knownMembers.Contains(target)) return DenyUnknown(pos, stored);

            StorageStructureSnapshot simulated = structureScanner.Scan(
                new WorldStorageTopologySource(Api.World.BlockAccessor),
                new StorageStructureScanRequest(
                    controller,
                    WarehouseId,
                    new StorageTopologyLimits(),
                    knownMembers,
                    target));
            if (!simulated.IsComplete || !simulated.IsValid)
            {
                return DenyUnknown(pos, stored);
            }
            return stored <= simulated.ItemCapacity
                ? StorageRemovalCheck.Allow(
                    pos,
                    stored,
                    VerifiedItemCapacity,
                    simulated.ItemCapacity)
                : StorageRemovalCheck.Deny(
                    pos,
                    stored,
                    VerifiedItemCapacity,
                    simulated.ItemCapacity,
                    "vintagekinematics:storage-removal-would-overflow");
        }

        private bool HasSafeRelocationEvidence()
        {
            if (Api?.Side != EnumAppSide.Server
                || activeRecoveryRecord == null)
            {
                return false;
            }

            KineticStorageRecoverySystem recoverySystem =
                Api.ModLoader.GetModSystem<KineticStorageRecoverySystem>();
            return recoverySystem?.IsLoaded == true
                && recoverySystem.CanPersist
                && recoverySystem.Registry.TryGet(
                    WarehouseId,
                    out StorageRecoveryRecord recovery)
                && recoverySystem.ControllerRegistry.TryGet(
                    WarehouseId,
                    out StorageRecoveryRecord controller)
                && recovery.IsEquivalentTo(activeRecoveryRecord)
                && controller.IsEquivalentTo(activeRecoveryRecord);
        }

        private StorageRemovalCheck DenyUnknown(BlockPos pos, long stored)
        {
            return StorageRemovalCheck.Deny(
                pos,
                stored,
                VerifiedItemCapacity,
                VerifiedItemCapacity,
                "vintagekinematics:storage-structure-unknown");
        }

        private void RecordSafeControllerRemoval()
        {
            lock (persistenceSync)
            {
                RecordSafeControllerRemovalSynchronized();
            }
        }

        private void RecordSafeControllerRemovalSynchronized()
        {
            if (Api?.Side != EnumAppSide.Server
                || activeRecoveryRecord == null
                || itemIndex == null
                || itemIndex.StoredItems != 0
                || knownMembers.Any(member =>
                    member != WorldStorageTopologySource.FromBlockPos(Pos))
                || HasAttachedStorageMembers())
            {
                return;
            }

            KineticStorageRecoverySystem recoverySystem =
                Api.ModLoader.GetModSystem<KineticStorageRecoverySystem>();
            if (recoverySystem?.IsLoaded != true || !recoverySystem.CanPersist) return;
            if (!recoverySystem.Registry.TryGet(WarehouseId, out StorageRecoveryRecord registered)
                || !registered.IsEquivalentTo(activeRecoveryRecord))
            {
                return;
            }
            recoverySystem.TombstoneMirrors(
                WarehouseId,
                checked(activeRecoveryRecord.Revision + 1));
        }

        private bool HasAttachedStorageMembers()
        {
            foreach (BlockFacing face in BlockFacing.ALLFACES)
            {
                if (Api.World.BlockAccessor.GetBlockEntity(Pos.AddCopy(face))
                        is IVKStorageStructureMember member
                    && !member.IsController
                    && member.WarehouseId == WarehouseId
                    && member.ControllerPos != null
                    && member.ControllerPos.Equals(Pos))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
