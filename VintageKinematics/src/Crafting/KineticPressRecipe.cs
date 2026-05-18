using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Util;

namespace VintageKinematics.Crafting
{
    /// <summary>
    /// Optional liquid output for an extractor recipe. <see cref="Code"/> is the liquid portion item
    /// (e.g. <c>game:oilportion-olive</c>); <see cref="Litres"/> is litres produced per cycle.
    /// </summary>
    public class ExtractorLiquidOutput
    {
        public AssetLocation Code;
        public float Litres = 0.1f;
    }

    /// <summary>
    /// One JSON extractor recipe. Matches a single ingredient and on each work cycle consumes
    /// 1 of it, producing either solid <see cref="Outputs"/>, a <see cref="Liquid"/> portion,
    /// or both. Liquid goes to the extractor's internal buffer (10L), solid to its output slots.
    /// </summary>
    public class KineticExtractorRecipe
    {
        public JsonItemStack Ingredient;
        public JsonItemStack[] Outputs;
        public ExtractorLiquidOutput Liquid;
        public int PressTicks = 6;

        public bool Matches(ItemStack stack)
        {
            if (stack == null || Ingredient?.Code == null) return false;
            return WildcardUtil.Match(Ingredient.Code, stack.Collectible.Code);
        }

        public bool Resolve(IWorldAccessor world, string sourceForErrors)
        {
            if (Ingredient?.Code == null) return false;
            if (Ingredient.Code.Path?.Contains('*') != true)
            {
                Ingredient.Resolve(world, sourceForErrors);
            }
            if (Outputs != null)
            {
                foreach (var o in Outputs)
                {
                    o?.Resolve(world, sourceForErrors);
                }
            }
            return true;
        }
    }
}
