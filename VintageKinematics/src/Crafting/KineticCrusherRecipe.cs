using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Util;

namespace VintageKinematics.Crafting
{
    /// <summary>
    /// One JSON crusher recipe. <see cref="Ingredient"/> is matched against the basin's input slot top
    /// item by collectible code (wildcards supported via <c>*</c> — e.g.
    /// <c>game:ore-medium-nativecopper-*</c> matches any rock variant); on match, after
    /// <see cref="CrushTicks"/> piston bottom-outs the input is decremented by 1 and each output is
    /// deposited into the basin's side- or bottom-output slot.
    /// </summary>
    public class KineticCrusherRecipe
    {
        public JsonItemStack Ingredient;
        public JsonItemStack[] Outputs;
        public int CrushTicks = 4;

        public bool Matches(ItemStack stack)
        {
            if (stack == null || Ingredient?.Code == null) return false;
            return WildcardUtil.Match(Ingredient.Code, stack.Collectible.Code);
        }

        public bool Resolve(IWorldAccessor world, string sourceForErrors)
        {
            if (Ingredient?.Code == null) return false;
            // Wildcard ingredient codes can't materialize into a single ResolvedItemstack and would
            // log a spurious "Failed resolving" warning at load time. Matching is done via
            // WildcardUtil on the raw code, so skip the resolve in that case.
            if (Ingredient.Code.Path?.Contains('*') != true)
            {
                Ingredient.Resolve(world, sourceForErrors);
            }
            if (Outputs != null)
            {
                foreach (var o in Outputs)
                {
                    // Skip wildcard output codes: they are resolved per-craft against the
                    // ingredient's captured wildcard value (see BEBehaviorCrusherProcess).
                    if (o?.Code?.Path?.Contains('*') == true) continue;
                    o?.Resolve(world, sourceForErrors);
                }
            }
            return true;
        }
    }
}
