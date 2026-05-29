using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VintageKinematics.Blocks
{
    public class BlockGeothermalPipe : Block
    {
        public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemStack, BlockSelection blockSel, ref string failureCode)
        {
            failureCode = "boreplaceonly";
            return false;
        }
    }
}
