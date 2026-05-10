using System;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VintageKinematics.Blocks
{
    /// <summary>
    /// Crusher head with two opposing horizontal stubs. Variant <c>axis=x</c> places stubs on ±X faces;
    /// <c>axis=z</c> on ±Z faces. Player picks based on look direction at placement.
    /// </summary>
    public class BlockCrusher : BlockAxisOriented
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
    }
}
