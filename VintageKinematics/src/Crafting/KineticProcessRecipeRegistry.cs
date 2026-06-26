using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Util;

namespace VintageKinematics.Crafting
{
    public class KineticProcessRecipe
    {
        public string Code;
        public string Machine;
        public JsonItemStack Ingredient;
        public JsonItemStack[] Outputs;

        public int InputQuantity => Ingredient?.StackSize > 0 ? Ingredient.StackSize : 1;

        public bool Matches(string machineCode, ItemStack stack)
        {
            if (stack?.Collectible == null || Ingredient?.Code == null) return false;
            if (!string.IsNullOrEmpty(Machine) && Machine != machineCode) return false;
            if (stack.StackSize < InputQuantity) return false;
            return WildcardUtil.Match(Ingredient.Code, stack.Collectible.Code);
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
                foreach (JsonItemStack output in Outputs)
                {
                    if (output?.Code == null) continue;
                    if (output.Code.Path?.Contains('*') == true) return false;
                    output.Resolve(world, sourceForErrors);
                }
            }
            return true;
        }
    }

    /// <summary>
    /// Generic recipes for JSON-defined kinetic processors.
    /// Recipes load from assets/*/vkrecipe/process/**/*.json.
    /// </summary>
    public class KineticProcessRecipeRegistry : ModSystem
    {
        private readonly List<KineticProcessRecipe> recipes = new List<KineticProcessRecipe>();

        public IReadOnlyList<KineticProcessRecipe> Recipes => recipes;

        public override double ExecuteOrder() => 1.0;

        public override void AssetsFinalize(ICoreAPI api)
        {
            base.AssetsFinalize(api);
            recipes.Clear();

            var assets = api.Assets.GetMany<KineticProcessRecipe>(api.Logger, "vkrecipe/process/");
            foreach (var entry in assets)
            {
                KineticProcessRecipe recipe = entry.Value;
                if (recipe == null) continue;
                recipe.Code = entry.Key.ToString();
                if (string.IsNullOrEmpty(recipe.Machine)) recipe.Machine = MachineFromAssetPath(entry.Key.Path);
                if (string.IsNullOrEmpty(recipe.Machine)) continue;
                if (recipe.Resolve(api.World, entry.Key.ToString())) recipes.Add(recipe);
            }

            api.Logger.Notification($"[VintageKinematics] Loaded {recipes.Count} generic process recipe(s)");
        }

        public KineticProcessRecipe FindRecipe(string machineCode, ItemStack stack)
        {
            if (string.IsNullOrEmpty(machineCode) || stack == null) return null;
            foreach (KineticProcessRecipe recipe in recipes)
            {
                if (recipe.Matches(machineCode, stack)) return recipe;
            }
            return null;
        }

        private static string MachineFromAssetPath(string path)
        {
            const string prefix = "vkrecipe/process/";
            if (string.IsNullOrEmpty(path) || !path.StartsWith(prefix)) return null;
            string rest = path.Substring(prefix.Length);
            int slash = rest.IndexOf('/');
            return slash > 0 ? rest.Substring(0, slash) : null;
        }
    }
}
