using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using VintageKinematics.Api;

namespace VintageKinematics.Blocks
{
    public class BlockKineticCharcoalRetort : Block, IPlacementPreviewProvider
    {
        public bool TryResolvePlacementPreview(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, out BlockPos targetPos, out Block variant)
        {
            targetPos = null;
            variant = null;
            if (blockSel?.Face == null) return false;

            targetPos = PlacementPreview.DefaultTargetPos(world, blockSel, this);
            string desired = SideFacingPlayer(byPlayer);
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

        private static string SideFacingPlayer(IPlayer byPlayer)
        {
            if (byPlayer?.Entity == null) return null;
            BlockFacing toward = BlockFacing.HorizontalFromYaw(byPlayer.Entity.Pos.Yaw).Opposite;
            if (toward == BlockFacing.NORTH) return "n";
            if (toward == BlockFacing.EAST) return "e";
            if (toward == BlockFacing.SOUTH) return "s";
            if (toward == BlockFacing.WEST) return "w";
            return null;
        }
    }
}
