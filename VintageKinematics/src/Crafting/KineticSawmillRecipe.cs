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
        Firewood,
        Gearbox,
        Axle,
        AngledGear
    }

    /// <summary>
    /// One JSON sawmill recipe. Matches a log by collectible code (wildcards via <c>*</c>) and on
    /// each completed work cycle consumes 1 log and emits each entry in <see cref="Outputs"/>.
    /// <see cref="Mode"/> selects which sawmill mode (plank/shaft/stick/cogwheelsection/firewood/gearbox/axle/angledgear) the recipe is active in.
    /// </summary>
    public class KineticSawmillRecipe
    {
        public JsonItemStack Ingredient;
        public JsonItemStack[] Outputs;
        public string[] AllowedVariants;
        public string[] SkipVariants;
        public int SawTicks = 4;
        public SawmillMode Mode = SawmillMode.Plank;

        public bool Matches(ItemStack stack, SawmillMode mode)
        {
            if (Mode != mode) return false;
            if (stack == null || Ingredient?.Code == null) return false;
            if (!WildcardUtil.Match(Ingredient.Code, stack.Collectible.Code)) return false;
            if (MatchesSkippedVariant(stack.Collectible.Code)) return false;
            if (AllowedVariants == null || AllowedVariants.Length == 0) return true;

            return MatchesAllowedVariant(stack.Collectible.Code);
        }

        private bool MatchesAllowedVariant(AssetLocation inputCode)
        {
            return MatchesVariantList(inputCode, AllowedVariants);
        }

        private bool MatchesSkippedVariant(AssetLocation inputCode)
        {
            if (SkipVariants == null || SkipVariants.Length == 0) return false;
            return MatchesVariantList(inputCode, SkipVariants);
        }

        private bool MatchesVariantList(AssetLocation inputCode, string[] variants)
        {
            if (inputCode == null) return false;

            string patternPath = Ingredient.Code.Path;
            int starIndex = patternPath?.IndexOf('*') ?? -1;
            if (starIndex < 0) return false;

            foreach (string variant in variants)
            {
                if (string.IsNullOrEmpty(variant)) continue;
                string variantPath = patternPath.Substring(0, starIndex) + variant + patternPath.Substring(starIndex + 1);
                AssetLocation variantPattern = new AssetLocation(Ingredient.Code.Domain, variantPath);
                if (WildcardUtil.Match(variantPattern, inputCode)) return true;
            }
            return false;
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
                    if (o?.Code?.Path?.Contains('*') == true) continue;
                    o?.Resolve(world, sourceForErrors);
                }
            }
            return true;
        }
    }
}
