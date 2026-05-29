using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using VintageKinematics.Api;
using VintageKinematics.BlockEntities;

namespace VintageKinematics.Blocks
{
    public class BlockKineticIgniter : Block, IPlacementPreviewProvider
    {
        public bool TryResolvePlacementPreview(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, out BlockPos targetPos, out Block variant)
        {
            targetPos = null;
            variant = null;
            if (blockSel?.Face == null) return false;

            targetPos = PlacementPreview.DefaultTargetPos(world, blockSel, this);
            string desired = SideAwayFromPlayer(byPlayer);
            if (desired == null)
            {
                variant = this;
                return true;
            }
            AssetLocation code = CodeWithVariants(new[] { "side", "state" }, new[] { desired, "cool" });
            variant = world.GetBlock(code) ?? this;
            return true;
        }

        public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemStack, BlockSelection blockSel, ref string failureCode)
        {
            if (!TryResolvePlacementPreview(world, byPlayer, blockSel, out _, out Block variant) || variant == this)
                return base.TryPlaceBlock(world, byPlayer, itemStack, blockSel, ref failureCode);
            return variant.TryPlaceBlock(world, byPlayer, itemStack, blockSel, ref failureCode);
        }

        // Front face points away from the placing player so the rod faces the block they
        // were aiming at when they placed it.
        private static string SideAwayFromPlayer(IPlayer byPlayer)
        {
            if (byPlayer?.Entity == null) return null;
            BlockFacing away = BlockFacing.HorizontalFromYaw(byPlayer.Entity.Pos.Yaw);
            if (away == BlockFacing.NORTH) return "n";
            if (away == BlockFacing.EAST)  return "e";
            if (away == BlockFacing.SOUTH) return "s";
            if (away == BlockFacing.WEST)  return "w";
            return null;
        }

        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            if (blockSel == null) return base.OnBlockInteractStart(world, byPlayer, blockSel);
            if (KineticInteractionHelper.ShouldDeferToHeldWrench(byPlayer)) return false;
            BEKineticIgniter be = world.BlockAccessor.GetBlockEntity(blockSel.Position) as BEKineticIgniter;
            if (be != null && be.TryRelightHeldTorch(byPlayer)) return true;
            return base.OnBlockInteractStart(world, byPlayer, blockSel);
        }
    }
}
