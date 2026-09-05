using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using VintageKinematics.Api.Storage;
using VintageKinematics.Storage.Index;
using VintageKinematics.Storage.Persistence;
using VintageKinematics.Storage.Recovery;

namespace VintageKinematics.Storage.Topology
{
    /// <summary>
    /// Capacity and connectivity check used while a warehouse controller is being moved.
    /// </summary>
    internal static class OrphanedStorageRemovalEvaluator
    {
        public static StorageRemovalCheck Check(
            IWorldAccessor world,
            BlockPos targetPos,
            IVKStorageStructureMember target,
            StorageRemovalKind kind)
        {
            if (kind != StorageRemovalKind.PlayerBreak
                && kind != StorageRemovalKind.WrenchPickup)
            {
                return Deny(targetPos);
            }
            if (world?.Side != EnumAppSide.Server
                || target == null
                || target.IsController
                || string.IsNullOrWhiteSpace(target.WarehouseId)
                || target.ControllerPos == null)
            {
                return Deny(targetPos);
            }

            KineticStorageRecoverySystem recoverySystem =
                world.Api.ModLoader.GetModSystem<KineticStorageRecoverySystem>();
            StorageControllerLocation expectedController = ToLocation(target.ControllerPos);
            if (recoverySystem?.IsLoaded != true
                || !recoverySystem.CanPersist
                || !recoverySystem.Registry.TryGet(
                    target.WarehouseId,
                    out StorageRecoveryRecord recovery)
                || !recoverySystem.ControllerRegistry.TryGet(
                    target.WarehouseId,
                    out StorageRecoveryRecord controller)
                || recovery.IsTombstone
                || recovery.Controller != expectedController
                || !recovery.IsEquivalentTo(controller))
            {
                return Deny(targetPos);
            }

            StorageLoadResult loaded = new KineticStoragePersistence(world).Load(
                recovery.SnapshotBytes,
                new StorageIndexLimits(long.MaxValue));
            if (loaded.HasCorruption)
            {
                return Deny(targetPos);
            }

            if (!TryCollectMembers(
                    world,
                    target.WarehouseId,
                    target.ControllerPos,
                    out Dictionary<StorageTopologyPosition, long> members)
                || !members.ContainsKey(WorldStorageTopologySource.FromBlockPos(targetPos)))
            {
                return Deny(targetPos, loaded.Index.StoredItems);
            }

            return Evaluate(
                targetPos,
                WorldStorageTopologySource.FromBlockPos(targetPos),
                members,
                loaded.Index.StoredItems,
                WorldStorageTopologySource.FromBlockPos(target.ControllerPos));
        }

        internal static StorageRemovalCheck Evaluate(
            BlockPos targetPos,
            StorageTopologyPosition target,
            IReadOnlyDictionary<StorageTopologyPosition, long> members,
            long storedItems,
            StorageTopologyPosition? orphanController = null)
        {
            long currentCapacity = 0;
            long capacityAfterRemoval = 0;
            try
            {
                foreach (KeyValuePair<StorageTopologyPosition, long> member in members)
                {
                    currentCapacity = checked(currentCapacity + member.Value);
                    if (member.Key != target)
                    {
                        capacityAfterRemoval = checked(capacityAfterRemoval + member.Value);
                    }
                }
            }
            catch (OverflowException)
            {
                return Deny(targetPos, storedItems);
            }

            if (!RemainingMembersAreConnected(target, members)
                || RemovesLastControllerAnchor(target, members, orphanController))
            {
                return StorageRemovalCheck.Deny(
                    targetPos,
                    storedItems,
                    currentCapacity,
                    capacityAfterRemoval,
                    "vintagekinematics:storage-removal-would-split");
            }
            return storedItems <= capacityAfterRemoval
                ? StorageRemovalCheck.Allow(
                    targetPos,
                    storedItems,
                    currentCapacity,
                    capacityAfterRemoval)
                : StorageRemovalCheck.Deny(
                    targetPos,
                    storedItems,
                    currentCapacity,
                    capacityAfterRemoval,
                    "vintagekinematics:storage-removal-would-overflow");
        }

        private static bool RemovesLastControllerAnchor(
            StorageTopologyPosition removed,
            IReadOnlyDictionary<StorageTopologyPosition, long> members,
            StorageTopologyPosition? controller)
        {
            if (!controller.HasValue || members.Count <= 1) return false;
            if (!StorageTopologyRules.AreFaceAdjacent(removed, controller.Value)) return false;

            foreach (StorageTopologyPosition position in members.Keys)
            {
                if (position != removed
                    && StorageTopologyRules.AreFaceAdjacent(position, controller.Value))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool TryCollectMembers(
            IWorldAccessor world,
            string warehouseId,
            BlockPos controllerPos,
            out Dictionary<StorageTopologyPosition, long> members)
        {
            members = new Dictionary<StorageTopologyPosition, long>();
            var visited = new HashSet<StorageTopologyPosition>();
            var open = new Queue<BlockPos>();
            foreach (BlockFacing face in BlockFacing.ALLFACES)
            {
                open.Enqueue(controllerPos.AddCopy(face));
            }

            var source = new WorldStorageTopologySource(world.BlockAccessor);
            while (open.Count > 0)
            {
                BlockPos position = open.Dequeue();
                StorageTopologyPosition topologyPosition =
                    WorldStorageTopologySource.FromBlockPos(position);
                if (!visited.Add(topologyPosition)) continue;
                if (!source.IsChunkLoaded(source.GetChunk(topologyPosition))) return false;
                if (world.BlockAccessor.GetBlockEntity(position)
                        is not IVKStorageStructureMember member
                    || member.IsController
                    || member.WarehouseId != warehouseId
                    || member.ControllerPos == null
                    || !member.ControllerPos.Equals(controllerPos))
                {
                    continue;
                }

                if (members.Count >= StorageTopologyLimits.DefaultMaxNonControllerMembers)
                {
                    return false;
                }
                members.Add(topologyPosition, member.CapacityContribution);
                foreach (BlockFacing face in BlockFacing.ALLFACES)
                {
                    open.Enqueue(position.AddCopy(face));
                }
            }
            return true;
        }

        private static bool RemainingMembersAreConnected(
            StorageTopologyPosition removed,
            IReadOnlyDictionary<StorageTopologyPosition, long> members)
        {
            StorageTopologyPosition? first = null;
            foreach (StorageTopologyPosition position in members.Keys)
            {
                if (position != removed)
                {
                    first = position;
                    break;
                }
            }
            if (!first.HasValue) return true;

            var reached = new HashSet<StorageTopologyPosition>();
            var open = new Queue<StorageTopologyPosition>();
            open.Enqueue(first.Value);
            while (open.Count > 0)
            {
                StorageTopologyPosition position = open.Dequeue();
                if (position == removed
                    || !members.ContainsKey(position)
                    || !reached.Add(position))
                {
                    continue;
                }
                foreach (StorageTopologyPosition neighbor
                    in StorageTopologyRules.FaceNeighbors(position))
                {
                    open.Enqueue(neighbor);
                }
            }
            return reached.Count == members.Count - 1;
        }

        private static StorageControllerLocation ToLocation(BlockPos position)
        {
            return new StorageControllerLocation(
                position.X,
                position.InternalY,
                position.Z,
                position.dimension);
        }

        private static StorageRemovalCheck Deny(
            BlockPos pos,
            long stored = 0,
            long currentCapacity = 0,
            long capacityAfterRemoval = 0)
        {
            return StorageRemovalCheck.Deny(
                pos,
                stored,
                currentCapacity,
                capacityAfterRemoval,
                "vintagekinematics:storage-structure-unknown");
        }
    }
}
