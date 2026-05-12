using System;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using VintageKinematics.Api;
using VintageKinematics.BlockEntities;

namespace VintageKinematics.Blocks
{
    /// <summary>
    /// Sawmill: shaft enters horizontally; blade spins on a vertical plane perpendicular to it.
    /// Variant <c>axis=x</c> places the shaft on ±X faces, <c>axis=z</c> on ±Z. Picked from player
    /// look direction at placement.
    /// </summary>
    public class BlockKineticSawmill : BlockAxisOriented
    {
        public override string GetPlacementVariantAxis(IWorldAccessor world, IPlayer byPlayer, ItemStack itemStack, BlockSelection blockSel)
        {
            if (byPlayer?.Entity != null)
            {
                double rad = byPlayer.Entity.Pos.Yaw % (Math.PI * 2);
                if (rad < 0) rad += Math.PI * 2;
                bool eastWestLook = (rad > Math.PI / 4 && rad < 3 * Math.PI / 4)
                                    || (rad > 5 * Math.PI / 4 && rad < 7 * Math.PI / 4);
                return eastWestLook ? "x" : "z";
            }
            return base.GetPlacementVariantAxis(world, byPlayer, itemStack, blockSel);
        }

        public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemStack, BlockSelection blockSel, ref string failureCode)
        {
            BlockPos targetPos = blockSel?.Face == null
                ? blockSel?.Position
                : PlacementPreview.DefaultTargetPos(world, blockSel, this);

            bool placed = base.TryPlaceBlock(world, byPlayer, itemStack, blockSel, ref failureCode);
            if (placed && targetPos != null && world.BlockAccessor.GetBlockEntity(targetPos) is BEKineticSawmill be)
            {
                BlockFacing inputFace = InputFaceFromPlacement(byPlayer, be.Block?.Variant?["axis"]);
                be.SetAutomationInputFace(inputFace);
            }
            return placed;
        }

        // Shaft sits on the variant axis (axis-x → east/west, axis-z → north/south). Automation
        // input must land on the perpendicular axis so logs feed from a non-shaft face. Default to
        // the perpendicular face nearest the player; fall back to a stable side when the player's
        // look direction doesn't pick one.
        private static BlockFacing InputFaceFromPlacement(IPlayer byPlayer, string axis)
        {
            if (byPlayer?.Entity == null)
            {
                return axis == "z" ? BlockFacing.EAST : BlockFacing.SOUTH;
            }

            BlockFacing towardPlayer = BlockFacing.HorizontalFromYaw(byPlayer.Entity.Pos.Yaw).Opposite;
            if (axis == "z")
            {
                return towardPlayer.Axis == EnumAxis.X ? towardPlayer : BlockFacing.EAST;
            }
            return towardPlayer.Axis == EnumAxis.Z ? towardPlayer : BlockFacing.SOUTH;
        }

        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            if (blockSel == null) return false;
            BEKineticSawmill be = world.BlockAccessor.GetBlockEntity(blockSel.Position) as BEKineticSawmill;
            if (be == null) return base.OnBlockInteractStart(world, byPlayer, blockSel);
            return be.OnPlayerRightClick(byPlayer, blockSel);
        }
    }
}
