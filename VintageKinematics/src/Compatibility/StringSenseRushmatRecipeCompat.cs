using System;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;

namespace VintageKinematics.Compatibility
{
    public class StringSenseRushmatRecipeCompat : ModSystem
    {
        private const string StringSenseModId = "stringsense";
        private const string RecipePath = "recipes/grid";
        private const string Domain = "vintagekinematics";
        private const string VanillaRushmatCode = "game:rushmat";
        private const string CompatRushmatCode = "game:rushmat-*-down";

        public override double ExecuteOrder() => 0.9;

        public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Server;

        public override void AssetsLoaded(ICoreAPI api)
        {
            base.AssetsLoaded(api);

            if (api?.ModLoader?.IsModEnabled(StringSenseModId) != true) return;

            int assetCount = 0;
            int recipeCount = 0;
            int ingredientCount = 0;

            foreach (IAsset asset in api.Assets.GetMany(RecipePath, Domain))
            {
                try
                {
                    JToken root = JToken.Parse(asset.ToText());
                    if (!PatchRecipeFile(root, ref recipeCount, ref ingredientCount)) continue;

                    asset.Data = Encoding.UTF8.GetBytes(root.ToString(Formatting.Indented));
                    asset.IsPatched = true;
                    assetCount++;
                }
                catch (Exception ex)
                {
                    api.Logger.Warning($"[VintageKinematics] String Sense compatibility could not inspect {asset.Location}: {ex.Message}");
                }
            }

            if (ingredientCount <= 0) return;

            api.Logger.Notification(
                $"[VintageKinematics] Loaded compatibility for String Sense. Rewrote {ingredientCount} rushmat ingredient(s) across {recipeCount} recipe(s) in {assetCount} grid recipe asset(s)."
            );
        }

        private static bool PatchRecipeFile(JToken root, ref int recipeCount, ref int ingredientCount)
        {
            bool changed = false;

            if (root is JObject recipe)
            {
                if (PatchRecipe(recipe, ref ingredientCount))
                {
                    recipeCount++;
                    changed = true;
                }

                return changed;
            }

            if (root is not JArray recipes) return false;

            foreach (JToken token in recipes)
            {
                if (token is not JObject recipeObject) continue;
                if (!PatchRecipe(recipeObject, ref ingredientCount)) continue;

                recipeCount++;
                changed = true;
            }

            return changed;
        }

        private static bool PatchRecipe(JObject recipe, ref int ingredientCount)
        {
            if (recipe["ingredients"] is not JObject ingredients) return false;

            bool changed = false;

            foreach (JProperty property in ingredients.Properties())
            {
                if (property.Value is not JObject ingredient) continue;
                if (!IsVanillaRushmatBlockIngredient(ingredient)) continue;

                ingredient["code"] = CompatRushmatCode;
                ingredientCount++;
                changed = true;
            }

            return changed;
        }

        private static bool IsVanillaRushmatBlockIngredient(JObject ingredient)
        {
            string type = ingredient["type"]?.ToString();
            if (!string.IsNullOrEmpty(type) && !type.Equals("block", StringComparison.OrdinalIgnoreCase)) return false;

            return ingredient["code"]?.ToString() == VanillaRushmatCode;
        }
    }
}
