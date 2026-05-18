using System.Collections.Generic;
using Vintagestory.API.Common;

namespace VintageKinematics.Crafting
{
    /// <summary>
    /// Loads extractor recipes from <c>assets/&lt;mod&gt;/vkrecipe/extractor/*.json</c> across
    /// loaded mods during <see cref="ModSystem.AssetsFinalize"/>.
    /// </summary>
    public class KineticExtractorRecipeRegistry : ModSystem
    {
        private readonly List<KineticExtractorRecipe> recipes = new List<KineticExtractorRecipe>();

        public IReadOnlyList<KineticExtractorRecipe> Recipes => recipes;

        public override double ExecuteOrder() => 1.0;

        public override void AssetsFinalize(ICoreAPI api)
        {
            base.AssetsFinalize(api);
            recipes.Clear();
            LoadRecipes(api, "vkrecipe/extractor/");
            api.Logger.Notification($"[VintageKinematics] Loaded {recipes.Count} extractor recipe(s)");
        }

        private void LoadRecipes(ICoreAPI api, string path)
        {
            var assets = api.Assets.GetMany<KineticExtractorRecipe>(api.Logger, path);
            foreach (var entry in assets)
            {
                var recipe = entry.Value;
                if (recipe == null) continue;
                if (recipe.Resolve(api.World, entry.Key.ToString()))
                {
                    recipes.Add(recipe);
                }
            }
        }

        public KineticExtractorRecipe FindRecipe(ItemStack stack)
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
