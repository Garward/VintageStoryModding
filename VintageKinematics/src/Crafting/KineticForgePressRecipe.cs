using Vintagestory.API.Common;
using Vintagestory.API.Util;
using VintageKinematics.Api;

namespace VintageKinematics.Crafting
{
    /// <summary>
    /// One fueled forge-press recipe. Wildcard outputs capture the wildcard value from the input,
    /// e.g. <c>game:ingot-*</c> to <c>game:metalplate-*</c>.
    /// </summary>
    public class KineticForgePressRecipe
    {
        public JsonItemStack Ingredient;
        public JsonItemStack Die;
        public JsonItemStack[] Outputs;
        public string[] AllowedVariants;
        public bool UseInputSmeltedStack;
        public bool UseInputSmeltingTemperature;
        public bool RequiresModdedNuggetSmeltingConfig;
        public bool RequireNonGameDomain;
        public bool MatchAnyDomain;
        public string OperationCode = "press";
        public string OperationName;
        public int PressTicks = 6;
        public float RequiredTemperature = 900f;

        public bool Matches(IWorldAccessor world, VintageKinematicsConfig config, ItemStack stack, string operationCode, ItemStack dieStack)
        {
            if (!string.IsNullOrEmpty(operationCode) && OperationCode != operationCode) return false;
            if (stack == null || Ingredient?.Code == null) return false;
            if (!MatchesIngredientCode(stack.Collectible.Code)) return false;
            if (!MatchesDie(dieStack)) return false;
            if (RequireNonGameDomain && stack.Collectible.Code?.Domain == "game") return false;
            if (RequiresModdedNuggetSmeltingConfig && config?.IsForgePressModdedNuggetSmeltingAllowed(stack.Collectible.Code) != true) return false;
            if (UseInputSmeltedStack && !HasInputSmeltedStack(world, stack)) return false;
            if (AllowedVariants == null || AllowedVariants.Length == 0) return true;

            string captured = WildcardUtil.GetWildcardValue(Ingredient.Code, stack.Collectible.Code);
            if (captured == null) return false;
            foreach (string variant in AllowedVariants)
            {
                if (captured == variant) return true;
            }
            return false;
        }

        private bool MatchesIngredientCode(AssetLocation inputCode)
        {
            if (inputCode == null) return false;
            if (WildcardUtil.Match(Ingredient.Code, inputCode)) return true;
            return MatchAnyDomain && WildcardUtil.Match(Ingredient.Code.Path, inputCode.Path);
        }

        private static bool HasInputSmeltedStack(IWorldAccessor world, ItemStack stack)
        {
            if (world == null || stack?.Collectible == null) return false;
            CombustibleProperties props = stack.Collectible.GetCombustibleProperties(world, stack, null);
            return props?.SmeltedStack?.ResolvedItemstack != null && props.SmeltedRatio > 0;
        }

        private bool MatchesDie(ItemStack dieStack)
        {
            if (Die?.Code == null) return true;
            if (dieStack == null || dieStack.Collectible?.Code == null) return false;
            return WildcardUtil.Match(Die.Code, dieStack.Collectible.Code);
        }

        public string DisplayName => string.IsNullOrEmpty(OperationName) ? OperationCode : OperationName;

        public bool Resolve(IWorldAccessor world, string sourceForErrors)
        {
            if (Ingredient?.Code == null) return false;
            if (Ingredient.Code.Path?.Contains('*') != true)
            {
                Ingredient.Resolve(world, sourceForErrors);
            }
            if (Die?.Code != null && Die.Code.Path?.Contains('*') != true)
            {
                Die.Resolve(world, sourceForErrors);
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
