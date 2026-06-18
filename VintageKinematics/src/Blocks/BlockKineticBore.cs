using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using VintageKinematics.Api;
using VintageKinematics.BlockEntities;

namespace VintageKinematics.Blocks
{
    public class BlockKineticBore : Block, IPlacementPreviewProvider
    {
        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            if (blockSel == null) return false;
            if (KineticInteractionHelper.ShouldDeferToHeldWrench(byPlayer)) return false;
            BlockEntity be = MultiblockHelper.GetMultiblockAwareBE(world, blockSel.Position);
            if (be is BEKineticBore bore) return bore.OnPlayerRightClick(byPlayer, blockSel);
            return base.OnBlockInteractStart(world, byPlayer, blockSel);
        }

        public bool TryResolvePlacementPreview(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, out BlockPos targetPos, out Block variant)
        {
            targetPos = null;
            variant = null;
            if (blockSel?.Face == null) return false;

            BlockPos clickPos = PlacementPreview.DefaultTargetPos(world, blockSel, this);
            string desired = PlacementPreview.CardinalSideFromPlayerYaw(byPlayer);
            if (desired == null)
            {
                targetPos = clickPos;
                variant = this;
                return true;
            }
            variant = world.GetBlock(CodeWithVariant("side", desired)) ?? this;
            targetPos = PlacementPreview.Centered3x3ControllerPos(clickPos, desired);
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

    }
}
