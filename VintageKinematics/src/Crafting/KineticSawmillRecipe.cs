using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Util;

namespace VintageKinematics.Crafting
{
    public enum SawmillMode
    {
        Plank,
        Shaft,
        Stick,
        CogwheelSection,
        Firewood
    }

    /// <summary>
    /// One JSON sawmill recipe. Matches a log by collectible code (wildcards via <c>*</c>) and on
    /// each completed work cycle consumes 1 log and emits each entry in <see cref="Outputs"/>.
    /// <see cref="Mode"/> selects which sawmill mode (plank/shaft/stick/cogwheelsection/firewood) the recipe is active in.
    /// </summary>
    public class KineticSawmillRecipe
    {
        public JsonItemStack Ingredient;
        public JsonItemStack[] Outputs;
        public int SawTicks = 4;
        public SawmillMode Mode = SawmillMode.Plank;

        public bool Matches(ItemStack stack, SawmillMode mode)
        {
            if (Mode != mode) return false;
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
