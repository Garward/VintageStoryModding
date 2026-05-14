using Vintagestory.API.Common;
using Vintagestory.API.Util;

namespace VintageKinematics.Crafting
{
    /// <summary>
    /// One fueled forge-press recipe. Wildcard outputs capture the wildcard value from the input,
    /// e.g. <c>game:ingot-*</c> to <c>game:metalplate-*</c>.
    /// </summary>
    public class KineticForgePressRecipe
    {
        public JsonItemStack Ingredient;
        public JsonItemStack[] Outputs;
        public string[] AllowedVariants;
        public string OperationCode = "press";
        public string OperationName;
        public int PressTicks = 6;
        public float RequiredTemperature = 900f;

        public bool Matches(ItemStack stack, string operationCode)
        {
            if (!string.IsNullOrEmpty(operationCode) && OperationCode != operationCode) return false;
            if (stack == null || Ingredient?.Code == null) return false;
            if (!WildcardUtil.Match(Ingredient.Code, stack.Collectible.Code)) return false;
            if (AllowedVariants == null || AllowedVariants.Length == 0) return true;

            string captured = WildcardUtil.GetWildcardValue(Ingredient.Code, stack.Collectible.Code);
            if (captured == null) return false;
            foreach (string variant in AllowedVariants)
            {
                if (captured == variant) return true;
            }
            return false;
        }

        public string DisplayName => string.IsNullOrEmpty(OperationName) ? OperationCode : OperationName;

        public bool Resolve(IWorldAccessor world, string sourceForErrors)
        {
            if (Ingredient?.Code == null) return false;
            if (Ingredient.Code.Path?.Contains('*') != true)
            {
                Ingredient.Resolve(world, sourceForErrors);
            }
            if (Outputs != null)
            {
                foreach (var output in Outputs)
                {
                    if (output?.Code?.Path?.Contains('*') == true) continue;
                    output?.Resolve(world, sourceForErrors);
                }
            }
            return true;
        }
    }
}
