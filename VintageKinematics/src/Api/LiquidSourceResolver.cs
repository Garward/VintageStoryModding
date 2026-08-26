using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace VintageKinematics.Api
{
    /// <summary>
    /// Resolves liquid owners before considering renewable world liquids.
    /// Block entities are checked first because they own mutable volume and persistence.
    /// </summary>
    public static class LiquidSourceResolver
    {
        public static ILiquidSource FindOwnedSource(IWorldAccessor world, BlockPos pos, Block block = null)
        {
            if (world == null || pos == null) return null;

            if (MultiblockHelper.GetMultiblockAwareBE(world, pos) is ILiquidSource blockEntitySource)
            {
                return blockEntitySource;
            }

            block ??= world.BlockAccessor.GetBlock(pos);
            return block as ILiquidSource ?? block?.GetCollectibleInterface<ILiquidSource>();
        }

        public static bool IsRenewableVanillaWorldWater(Block block)
        {
            return IsRenewableVanillaWorldWater(block?.Code?.Domain, block?.LiquidCode);
        }

        internal static bool IsRenewableVanillaWorldWater(string domain, string liquidCode)
        {
            if (domain != "game") return false;
            return liquidCode == "water" || liquidCode == "saltwater";
        }
    }
}
