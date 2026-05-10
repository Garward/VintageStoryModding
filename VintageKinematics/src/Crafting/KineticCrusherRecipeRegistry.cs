using System.Collections.Generic;
using Vintagestory.API.Common;

namespace VintageKinematics.Crafting
{
    /// <summary>
    /// Loads all crusher recipes from <c>assets/&lt;mod&gt;/vkrecipe/crusher/*.json</c> across loaded mods
    /// during <see cref="ModSystem.AssetsFinalize"/>. Public lookup for the crusher's BE behavior.
    /// </summary>
    public class KineticCrusherRecipeRegistry : ModSystem
    {
        private readonly List<KineticCrusherRecipe> recipes = new List<KineticCrusherRecipe>();

        /// <summary>All resolved recipes; rarely needed by callers (use <see cref="FindRecipe"/>).</summary>
        public IReadOnlyList<KineticCrusherRecipe> Recipes => recipes;

        public override double ExecuteOrder() => 1.0;

        public override void AssetsFinalize(ICoreAPI api)
        {
            base.AssetsFinalize(api);
            // Loaded on both sides so the client can independently detect "is this slot crushable?"
            // for synced impact effects (sound + particles) on the wave's bottom-out frame.
            recipes.Clear();
            var assets = api.Assets.GetMany<KineticCrusherRecipe>(api.Logger, "vkrecipe/crusher/");
            foreach (var entry in assets)
            {
                var recipe = entry.Value;
                if (recipe == null) continue;
                if (recipe.Resolve(api.World, entry.Key.ToString()))
                {
                    recipes.Add(recipe);
                }
            }
            api.Logger.Notification($"[VintageKinematics] Loaded {recipes.Count} crusher recipe(s)");
        }

        /// <summary>Returns the first recipe whose ingredient matches the given stack, or null.</summary>
        public KineticCrusherRecipe FindRecipe(ItemStack stack)
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
