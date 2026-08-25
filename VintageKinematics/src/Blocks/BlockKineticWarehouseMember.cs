using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using VintageKinematics.Api;
using VintageKinematics.BlockEntities.Storage;
using VintageKinematics.Connections;
using VintageKinematics.Storage.Rendering;

namespace VintageKinematics.Blocks
{
    /// <summary>Notifies storage block entities when their visual neighbor mask changes.</summary>
    public sealed class BlockKineticWarehouseMember : Block, IPlacementPreviewProvider
    {
        public bool TryResolvePlacementPreview(
            IWorldAccessor world,
            IPlayer byPlayer,
            BlockSelection blockSel,
            out BlockPos targetPos,
            out Block variant)
        {
            targetPos = null;
            variant = null;
            if (world == null || blockSel?.Face == null) return false;

            targetPos = PlacementPreview.DefaultTargetPos(world, blockSel, this);
            if (Variant == null || !Variant.ContainsKey("side"))
            {
                variant = this;
                return true;
            }

            BlockFacing playerFacing = byPlayer?.Entity == null
                ? null
                : BlockFacing.HorizontalFromYaw(byPlayer.Entity.Pos.Yaw);
            string side = SideFacingPlayer(playerFacing);
            variant = world.GetBlock(CodeWithVariant("side", side)) ?? this;
            return true;
        }

        public override bool TryPlaceBlock(
            IWorldAccessor world,
            IPlayer byPlayer,
            ItemStack itemStack,
            BlockSelection blockSel,
            ref string failureCode)
        {
            if (!TryResolvePlacementPreview(
                world,
                byPlayer,
                blockSel,
                out BlockPos targetPos,
                out Block variant))
            {
                return base.TryPlaceBlock(
                    world,
                    byPlayer,
                    itemStack,
                    blockSel,
                    ref failureCode);
            }

            return PlacementPreview.TryPlaceResolvedVariant(
                variant,
                world,
                byPlayer,
                itemStack,
                blockSel,
                targetPos,
                ref failureCode);
        }

        public override bool OnBlockInteractStart(
            IWorldAccessor world,
            IPlayer byPlayer,
            BlockSelection blockSel)
        {
            if (blockSel == null) return false;
            if (KineticInteractionHelper.ShouldDeferToHeldWrench(byPlayer)) return false;
            if (world.BlockAccessor.GetBlockEntity(blockSel.Position)
                is BEKineticWarehousePort port)
            {
                return port.OnPlayerRightClick(byPlayer);
            }
            if (world.BlockAccessor.GetBlockEntity(blockSel.Position)
                is not BEKineticWarehouseTerminal controller)
            {
                return base.OnBlockInteractStart(world, byPlayer, blockSel);
            }

            return controller.OnPlayerRightClick(byPlayer);
        }

        internal static string SideFacingPlayer(BlockFacing facing)
        {
            if (facing == BlockFacing.NORTH) return "s";
            if (facing == BlockFacing.EAST) return "e";
            if (facing == BlockFacing.SOUTH) return "n";
            if (facing == BlockFacing.WEST) return "w";
            return "s";
        }

        public override void OnBlockPlaced(
            IWorldAccessor world,
            BlockPos blockPos,
            ItemStack byItemStack = null)
        {
            base.OnBlockPlaced(world, blockPos, byItemStack);
            RefreshAtAndNeighbors(world, blockPos);
        }

        public override void OnNeighbourBlockChange(
            IWorldAccessor world,
            BlockPos pos,
            BlockPos neibpos)
        {
            base.OnNeighbourBlockChange(world, pos, neibpos);
            RefreshAt(world, pos);
        }

        public override void OnBlockRemoved(IWorldAccessor world, BlockPos pos)
        {
            base.OnBlockRemoved(world, pos);
            RefreshNeighbors(world, pos);
            RefreshEdgeDiagonals(world, pos);
        }

        private static void RefreshAtAndNeighbors(IWorldAccessor world, BlockPos pos)
        {
            RefreshAt(world, pos);
            RefreshNeighbors(world, pos);
            RefreshEdgeDiagonals(world, pos);
        }

        private static void RefreshNeighbors(IWorldAccessor world, BlockPos pos)
        {
            foreach (BlockFacing face in FaceConnectionMask.Faces)
            {
                RefreshAt(world, pos.AddCopy(face));
            }
        }

        private static void RefreshEdgeDiagonals(IWorldAccessor world, BlockPos pos)
        {
            foreach (StorageConcaveElbow elbow in StorageConcaveElbow.All)
            {
                RefreshAt(world, pos.AddCopy(elbow.First).Add(elbow.Second));
            }
        }

        private static void RefreshAt(IWorldAccessor world, BlockPos pos)
        {
            if (world?.Side != EnumAppSide.Client || pos == null) return;
            if (world.BlockAccessor.GetBlockEntity(pos)
                is BEKineticStorageMember member)
            {
                member.RefreshVisualConnections();
            }
        }
    }
}
