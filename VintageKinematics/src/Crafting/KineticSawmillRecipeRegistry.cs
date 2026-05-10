using System.Collections.Generic;
using Vintagestory.API.Common;

namespace VintageKinematics.Crafting
{
    /// <summary>
    /// Loads sawmill recipes from <c>assets/&lt;mod&gt;/vkrecipe/sawmill/*.json</c> across loaded mods
    /// during <see cref="ModSystem.AssetsFinalize"/>.
    /// </summary>
    public class KineticSawmillRecipeRegistry : ModSystem
    {
        private readonly List<KineticSawmillRecipe> recipes = new List<KineticSawmillRecipe>();

        public IReadOnlyList<KineticSawmillRecipe> Recipes => recipes;

        public override double ExecuteOrder() => 1.0;

        public override void AssetsFinalize(ICoreAPI api)
        {
            base.AssetsFinalize(api);
            recipes.Clear();
            var assets = api.Assets.GetMany<KineticSawmillRecipe>(api.Logger, "vkrecipe/sawmill/");
            foreach (var entry in assets)
            {
                var recipe = entry.Value;
                if (recipe == null) continue;
                if (recipe.Resolve(api.World, entry.Key.ToString()))
                {
                    recipes.Add(recipe);
                }
            }
            api.Logger.Notification($"[VintageKinematics] Loaded {recipes.Count} sawmill recipe(s)");
        }

        public KineticSawmillRecipe FindRecipe(ItemStack stack, SawmillMode mode)
        {
            if (stack == null) return null;
            foreach (var r in recipes)
            {
                if (r.Matches(stack, mode)) return r;
            }
            return null;
        }
    }
}
