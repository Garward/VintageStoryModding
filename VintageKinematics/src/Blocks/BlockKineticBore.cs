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
            string desired = SideFacingPlayer(byPlayer);
            if (desired == null)
            {
                targetPos = clickPos;
                variant = this;
                return true;
            }
            variant = world.GetBlock(CodeWithVariant("side", desired)) ?? this;
            targetPos = ShiftControllerToCenterClick(clickPos, desired);
            return true;
        }

        // Maps a clicked cell to the controller cell that puts the bore's 3x3 footprint
        // centred on the click. Each variant's controller sits at a different corner of
        // the multiblock claim, so the delta from click cell to controller cell is
        // variant-specific.
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

        public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemStack, BlockSelection blockSel, ref string failureCode)
        {
            if (!TryResolvePlacementPreview(world, byPlayer, blockSel, out BlockPos targetPos, out Block variant) || variant == this)
                return base.TryPlaceBlock(world, byPlayer, itemStack, blockSel, ref failureCode);

            BlockSelection shiftedSel = blockSel.Clone();
            shiftedSel.Position = targetPos;
            return variant.TryPlaceBlock(world, byPlayer, itemStack, shiftedSel, ref failureCode);
        }

        // Bore picks the variant whose ShaftStub points TOWARD the player, so the input
        // socket is on the near face. This is opposite of the "block-faces-player" convention
        // used by the sieve/igniter where the visible front should face outward.
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
