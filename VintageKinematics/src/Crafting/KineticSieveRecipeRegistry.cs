using System.Collections.Generic;
using Vintagestory.API.Common;

namespace VintageKinematics.Crafting
{
    /// <summary>
    /// Loads sieve recipes from <c>assets/&lt;mod&gt;/vkrecipe/sieve/*.json</c> across loaded mods
    /// during <see cref="ModSystem.AssetsFinalize"/>. Public lookup for the kinetic sieve BE.
    /// </summary>
    public class KineticSieveRecipeRegistry : ModSystem
    {
        private readonly List<KineticSieveRecipe> recipes = new List<KineticSieveRecipe>();

        public IReadOnlyList<KineticSieveRecipe> Recipes => recipes;

        public override double ExecuteOrder() => 1.0;

        public override void AssetsFinalize(ICoreAPI api)
        {
            base.AssetsFinalize(api);
            recipes.Clear();
            var assets = api.Assets.GetMany<KineticSieveRecipe>(api.Logger, "vkrecipe/sieve/");
            foreach (var entry in assets)
            {
                var recipe = entry.Value;
                if (recipe == null) continue;
                if (recipe.Resolve(api.World, entry.Key.ToString()))
                {
                    recipes.Add(recipe);
                }
            }
            api.Logger.Notification($"[VintageKinematics] Loaded {recipes.Count} sieve recipe(s)");
        }

        public KineticSieveRecipe FindRecipe(ItemStack stack)
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
