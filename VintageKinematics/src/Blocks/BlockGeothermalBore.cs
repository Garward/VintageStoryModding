using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using VintageKinematics.Api;
using VintageKinematics.BlockEntities;

namespace VintageKinematics.Blocks
{
    public class BlockGeothermalBore : Block, IPlacementPreviewProvider
    {
        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            if (blockSel == null) return false;
            if (KineticInteractionHelper.ShouldDeferToHeldWrench(byPlayer)) return false;
            BlockEntity be = MultiblockHelper.GetMultiblockAwareBE(world, blockSel.Position);
            if (be is BEGeothermalBore bore) return bore.OnPlayerRightClick(byPlayer, blockSel);
            return base.OnBlockInteractStart(world, byPlayer, blockSel);
        }

        public bool TryResolvePlacementPreview(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, out BlockPos targetPos, out Block variant)
        {
            targetPos = null;
            variant = null;
            if (blockSel?.Face == null) return false;

            BlockPos clickPos = PlacementPreview.DefaultTargetPos(world, blockSel, this);
            string desired = SideFacingPlayer(byPlayer);
            if (desired == null)
            {
                targetPos = clickPos;
                variant = this;
                return true;
            }
            variant = world.GetBlock(CodeWithVariants(new[] { "side", "state" }, new[] { desired, "cool" })) ?? this;
            targetPos = ShiftControllerToCenterClick(clickPos, desired);
            return true;
        }

        public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemStack, BlockSelection blockSel, ref string failureCode)
        {
            if (!TryResolvePlacementPreview(world, byPlayer, blockSel, out BlockPos targetPos, out Block variant) || variant == this)
                return base.TryPlaceBlock(world, byPlayer, itemStack, blockSel, ref failureCode);

            BlockSelection shiftedSel = blockSel.Clone();
            shiftedSel.Position = targetPos;
            return variant.TryPlaceBlock(world, byPlayer, itemStack, shiftedSel, ref failureCode);
        }

        private static BlockPos ShiftControllerToCenterClick(BlockPos clickPos, string side)
        {
            BlockPos p = clickPos.Copy();
            switch (side)
            {
                case "n": p.X -= 1; p.Z -= 1; break;
                case "e": p.X += 1; p.Z -= 1; break;
                case "s": p.X += 1; p.Z += 1; break;
                case "w": p.X -= 1; p.Z += 1; break;
            }
            return p;
        }

        private static string SideFacingPlayer(IPlayer byPlayer)
        {
            if (byPlayer?.Entity == null) return null;
            BlockFacing facing = BlockFacing.HorizontalFromYaw(byPlayer.Entity.Pos.Yaw);
            if (facing == BlockFacing.NORTH) return "n";
            if (facing == BlockFacing.EAST)  return "e";
            if (facing == BlockFacing.SOUTH) return "s";
            if (facing == BlockFacing.WEST)  return "w";
            return null;
        }
    }
}
