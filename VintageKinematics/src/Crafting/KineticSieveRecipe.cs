using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Util;

namespace VintageKinematics.Crafting
{
    /// <summary>
    /// One weighted-output sieve roll. <see cref="Weight"/> is the relative chance among siblings;
    /// <see cref="Nothing"/> = true means this slot is a tax roll (consumes input but yields no item).
    /// All other fields inherit from <see cref="JsonItemStack"/> (type/code/quantity/attributes).
    /// </summary>
    public class KineticSieveOutput : JsonItemStack
    {
        public int Weight = 1;
        public bool Nothing = false;
    }

    /// <summary>
    /// One JSON sieve recipe. <see cref="Ingredient"/> is matched by collectible code with wildcard
    /// support; on match each work cycle consumes 1 input and rolls a single weighted output from
    /// <see cref="Outputs"/>. A roll landing on a <c>Nothing</c> entry produces no drop (input still
    /// consumed).
    /// </summary>
    public class KineticSieveRecipe
    {
        public JsonItemStack Ingredient;
        public KineticSieveOutput[] Outputs;
        public int SieveTicks = 4;

        public bool Matches(ItemStack stack)
        {
            if (stack == null || Ingredient?.Code == null) return false;
            return WildcardUtil.Match(Ingredient.Code, stack.Collectible.Code);
        }

        public bool Resolve(IWorldAccessor world, string sourceForErrors)
        {
            if (Ingredient?.Code == null) return false;
            // Skip resolving wildcard ingredient codes — JsonItemStack can't materialize them and
            // logs a spurious warning. Matching is via WildcardUtil on the raw code.
            if (Ingredient.Code.Path?.Contains('*') != true)
            {
                Ingredient.Resolve(world, sourceForErrors);
            }
            if (Outputs != null)
            {
                foreach (var o in Outputs)
                {
                    if (o == null || o.Nothing) continue;
                    o.Resolve(world, sourceForErrors);
                }
            }
            return true;
        }

        // Returns null when the rolled entry is a Nothing tax roll, no outputs are defined,
        // or all weights are non-positive. Caller should still consume the input either way.
        public ItemStack RollOutput(IWorldAccessor world)
        {
            if (Outputs == null || Outputs.Length == 0) return null;

            int total = 0;
            foreach (var o in Outputs)
            {
                if (o == null) continue;
                if (o.Weight > 0) total += o.Weight;
            }
            if (total <= 0) return null;

            int pick = world.Rand.Next(total);
            int acc = 0;
            foreach (var o in Outputs)
            {
                if (o == null || o.Weight <= 0) continue;
                acc += o.Weight;
                if (pick < acc)
                {
                    if (o.Nothing) return null;
                    return o.ResolvedItemstack?.Clone();
                }
            }
            return null;
        }
    }
}
