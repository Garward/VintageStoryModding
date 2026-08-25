using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using VintageKinematics.Api.Storage;

namespace VintageKinematics.BlockEntities.Storage
{
    public partial class BEKineticWarehouseCell
    {
        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            if (api.Side == EnumAppSide.Server) ScheduleLinkAndRebuild();
        }

        public override void OnBlockPlaced(ItemStack byItemStack = null)
        {
            base.OnBlockPlaced(byItemStack);
            if (Api?.Side == EnumAppSide.Server) ScheduleLinkAndRebuild();
        }

        public override void OnBlockRemoved()
        {
            BlockPos controller = ControllerPos?.Copy();
            string warehouseId = WarehouseId;
            ConfirmRemovalWithController(controller, warehouseId);
            base.OnBlockRemoved();
        }

        private void ScheduleLinkAndRebuild()
        {
            RegisterDelayedCallback(_ =>
            {
                if (ControllerPos == null) TryAdoptSingleNeighborWarehouse();
                NotifyController(ControllerPos, StorageChangeReason.StructureChanged);
            }, 50);
        }

        private void TryAdoptSingleNeighborWarehouse()
        {
            Dictionary<string, LinkCandidate> candidates = new(StringComparer.Ordinal);
            foreach (BlockFacing face in BlockFacing.ALLFACES)
            {
                BlockPos neighborPos = Pos.AddCopy(face);
                if (Api.World.BlockAccessor.GetBlockEntity(neighborPos)
                    is not IVKStorageStructureMember neighbor)
                {
                    continue;
                }
                if (string.IsNullOrWhiteSpace(neighbor.WarehouseId) || neighbor.ControllerPos == null)
                {
                    continue;
                }

                string key = neighbor.WarehouseId + "@" + PositionKey(neighbor.ControllerPos);
                candidates[key] = new LinkCandidate(neighbor.WarehouseId, neighbor.ControllerPos);
            }

            if (candidates.Count != 1) return;
            foreach (LinkCandidate candidate in candidates.Values)
            {
                if (Api.World.BlockAccessor.GetBlockEntity(candidate.ControllerPos)
                    is not BEKineticWarehouseTerminal controller
                    || controller.WarehouseId != candidate.WarehouseId)
                {
                    return;
                }
                SetLink(candidate.WarehouseId, candidate.ControllerPos);
                MarkDirty();
            }
        }

        private void NotifyController(BlockPos controllerPos, StorageChangeReason reason)
        {
            if (Api?.Side != EnumAppSide.Server || controllerPos == null) return;
            if (Api.World.BlockAccessor.GetBlockEntity(controllerPos)
                is BEKineticWarehouseTerminal controller)
            {
                controller.RequestStructureRebuild(reason);
            }
        }

        private void ConfirmRemovalWithController(BlockPos controllerPos, string warehouseId)
        {
            if (Api?.Side != EnumAppSide.Server || controllerPos == null) return;
            if (Api.World.BlockAccessor.GetBlockEntity(controllerPos)
                is BEKineticWarehouseTerminal controller)
            {
                controller.ConfirmStorageMemberRemoved(Pos, warehouseId);
            }
        }

        private static string PositionKey(BlockPos position)
        {
            return position.dimension + ":" + position.X + "," + position.InternalY + "," + position.Z;
        }

        private readonly struct LinkCandidate
        {
            public string WarehouseId { get; }
            public BlockPos ControllerPos { get; }

            public LinkCandidate(string warehouseId, BlockPos controllerPos)
            {
                WarehouseId = warehouseId;
                ControllerPos = controllerPos.Copy();
            }
        }
    }
}
