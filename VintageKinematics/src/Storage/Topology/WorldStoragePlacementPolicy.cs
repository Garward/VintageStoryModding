using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using VintageKinematics.Api.Storage;
using VintageKinematics.BlockEntities.Storage;

namespace VintageKinematics.Storage.Topology
{
    internal static class WorldStoragePlacementPolicy
    {
        public static StoragePlacementDecision Evaluate(
            IWorldAccessor world,
            BlockPos position,
            StoragePlacementRole role)
        {
            List<StoragePlacementNeighbor> neighbors = new List<StoragePlacementNeighbor>();
            foreach (BlockFacing face in BlockFacing.ALLFACES)
            {
                if (world.BlockAccessor.GetBlockEntity(position.AddCopy(face))
                    is not IVKStorageStructureMember member)
                {
                    continue;
                }

                bool linked = ClientCanSubmitLinkedIntent(
                    member.WarehouseId,
                    member.ControllerPos != null);
                bool online = world.Side == EnumAppSide.Client
                    ? linked
                    : linked
                        && world.BlockAccessor.GetBlockEntity(member.ControllerPos)
                            is BEKineticWarehouseTerminal controller
                        && controller.WarehouseId == member.WarehouseId
                        && ControllerAcceptsPlacement(
                            world.Side,
                            controller.StructureState,
                            controller.IsItemIndexReady);
                neighbors.Add(new StoragePlacementNeighbor(
                    member.WarehouseId,
                    member.ControllerPos == null
                        ? null
                        : WorldStorageTopologySource.FromBlockPos(member.ControllerPos),
                    online));
            }
            return StoragePlacementPolicy.Evaluate(role, neighbors);
        }

        internal static bool ControllerAcceptsPlacement(
            EnumAppSide side,
            StorageState state,
            bool serverIndexReady)
        {
            if (state != StorageState.Online) return false;
            return side == EnumAppSide.Client || serverIndexReady;
        }

        internal static bool ClientCanSubmitLinkedIntent(
            string warehouseId,
            bool hasControllerPosition)
        {
            return !string.IsNullOrWhiteSpace(warehouseId) && hasControllerPosition;
        }
    }
}
