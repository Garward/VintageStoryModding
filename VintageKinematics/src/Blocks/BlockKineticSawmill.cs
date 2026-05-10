using System;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
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

        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            if (blockSel == null) return false;
            BEKineticSawmill be = world.BlockAccessor.GetBlockEntity(blockSel.Position) as BEKineticSawmill;
            if (be == null) return base.OnBlockInteractStart(world, byPlayer, blockSel);
            return be.OnPlayerRightClick(byPlayer, blockSel);
        }
    }
}
