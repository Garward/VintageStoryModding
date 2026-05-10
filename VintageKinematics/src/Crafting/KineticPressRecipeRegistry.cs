using System.Collections.Generic;
using Vintagestory.API.Common;

namespace VintageKinematics.Crafting
{
    /// <summary>
    /// Loads press recipes from <c>assets/&lt;mod&gt;/vkrecipe/press/*.json</c> across loaded mods
    /// during <see cref="ModSystem.AssetsFinalize"/>.
    /// </summary>
    public class KineticPressRecipeRegistry : ModSystem
    {
        private readonly List<KineticPressRecipe> recipes = new List<KineticPressRecipe>();

        public IReadOnlyList<KineticPressRecipe> Recipes => recipes;

        public override double ExecuteOrder() => 1.0;

        public override void AssetsFinalize(ICoreAPI api)
        {
            base.AssetsFinalize(api);
            recipes.Clear();
            var assets = api.Assets.GetMany<KineticPressRecipe>(api.Logger, "vkrecipe/press/");
            foreach (var entry in assets)
            {
                var recipe = entry.Value;
                if (recipe == null) continue;
                if (recipe.Resolve(api.World, entry.Key.ToString()))
                {
                    recipes.Add(recipe);
                }
            }
            api.Logger.Notification($"[VintageKinematics] Loaded {recipes.Count} press recipe(s)");
        }

        public KineticPressRecipe FindRecipe(ItemStack stack)
        {
            if (stack == null) return null;
            foreach (var r in recipes)
            {
                if (r.Matches(stack)) return r;
            }
            return null;
        }
    }
}
