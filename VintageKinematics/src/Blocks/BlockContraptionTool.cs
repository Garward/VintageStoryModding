using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using VintageKinematics.Api;

namespace VintageKinematics.Blocks
{
    public class BlockContraptionTool : Block, IPlacementPreviewProvider
    {
        public bool TryResolvePlacementPreview(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, out BlockPos targetPos, out Block variant)
        {
            targetPos = null;
            variant = null;
            if (blockSel?.Face == null) return false;

            targetPos = PlacementPreview.DefaultTargetPos(world, blockSel, this);
            string desired = SideFromFace(blockSel.Face) ?? SideAwayFromPlayer(byPlayer);
            if (desired == null)
            {
                variant = this;
                return true;
            }

            variant = world.GetBlock(CodeWithVariant("side", desired)) ?? this;
            return true;
        }

        public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemStack, BlockSelection blockSel, ref string failureCode)
        {
            if (!TryResolvePlacementPreview(world, byPlayer, blockSel, out _, out Block variant) || variant == this)
            {
                return base.TryPlaceBlock(world, byPlayer, itemStack, blockSel, ref failureCode);
            }

            return variant.TryPlaceBlock(world, byPlayer, itemStack, blockSel, ref failureCode);
        }

        private static string SideFromFace(BlockFacing facing)
        {
            if (facing == BlockFacing.NORTH) return "n";
            if (facing == BlockFacing.EAST) return "e";
            if (facing == BlockFacing.SOUTH) return "s";
            if (facing == BlockFacing.WEST) return "w";
            if (facing == BlockFacing.UP) return "u";
            if (facing == BlockFacing.DOWN) return "d";
            return null;
        }

        private static string SideAwayFromPlayer(IPlayer byPlayer)
        {
            if (byPlayer?.Entity == null) return null;
            BlockFacing away = BlockFacing.HorizontalFromYaw(byPlayer.Entity.Pos.Yaw);
            if (away == BlockFacing.NORTH) return "n";
            if (away == BlockFacing.EAST) return "e";
            if (away == BlockFacing.SOUTH) return "s";
            if (away == BlockFacing.WEST) return "w";
            return null;
        }
    }
}
