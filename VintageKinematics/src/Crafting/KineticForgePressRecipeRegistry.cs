using System.Collections.Generic;
using Vintagestory.API.Common;
using VintageKinematics.Api;

namespace VintageKinematics.Crafting
{
    /// <summary>
    /// Loads forge-press recipes from <c>assets/&lt;mod&gt;/vkrecipe/forgepress/*.json</c>.
    /// </summary>
    public class KineticForgePressRecipeRegistry : ModSystem
    {
        private readonly List<KineticForgePressRecipe> recipes = new List<KineticForgePressRecipe>();
        private readonly List<string> operationCodes = new List<string>();
        private readonly List<string> operationNames = new List<string>();
        private VintageKinematicsConfig config;
        private ICoreAPI api;

        public IReadOnlyList<KineticForgePressRecipe> Recipes => recipes;
        public IReadOnlyList<string> OperationCodes => operationCodes;
        public IReadOnlyList<string> OperationNames => operationNames;

        public override double ExecuteOrder() => 1.0;

        public override void AssetsFinalize(ICoreAPI api)
        {
            base.AssetsFinalize(api);
            this.api = api;
            recipes.Clear();
            operationCodes.Clear();
            operationNames.Clear();
            config = api.ModLoader.GetModSystem<KineticConfigSystem>()?.Config ?? new VintageKinematicsConfig();
            var assets = api.Assets.GetMany<KineticForgePressRecipe>(api.Logger, "vkrecipe/forgepress/");
            foreach (var entry in assets)
            {
                var recipe = entry.Value;
                if (recipe == null) continue;
                if (recipe.RequiresModdedNuggetSmeltingConfig && config.HasForgePressModdedNuggetSmeltingEnabled() != true) continue;
                if (recipe.Resolve(api.World, entry.Key.ToString()))
                {
                    if (string.IsNullOrEmpty(recipe.OperationCode)) recipe.OperationCode = "press";
                    recipes.Add(recipe);
                    AddOperation(recipe.OperationCode, recipe.DisplayName);
                }
            }
            api.Logger.Notification($"[VintageKinematics] Loaded {recipes.Count} forge press recipe(s)");
        }

        private void AddOperation(string code, string name)
        {
            for (int i = 0; i < operationCodes.Count; i++)
            {
                if (operationCodes[i] == code) return;
            }
            operationCodes.Add(code);
            operationNames.Add(string.IsNullOrEmpty(name) ? code : name);
        }

        public KineticForgePressRecipe FindRecipe(ItemStack stack, string operationCode, ItemStack dieStack = null)
        {
            if (stack == null) return null;
            foreach (var recipe in recipes)
            {
                if (recipe.Matches(api?.World, config, stack, operationCode, dieStack)) return recipe;
            }
            return null;
        }

        /// <summary>First recipe defined for the given operation, or null.</summary>
        public KineticForgePressRecipe FindByOperation(string operationCode)
        {
            if (string.IsNullOrEmpty(operationCode)) return null;
            foreach (var recipe in recipes)
            {
                if (recipe.OperationCode == operationCode) return recipe;
            }
            return null;
        }

        /// <summary>
        /// Per-gate facts for the selected operation: whether any recipe exists for it, and
        /// whether the input / die individually satisfy some recipe (each gate checked
        /// independently of the others). Used to explain why a press is idle.
        /// </summary>
        public ForgePressOperationProbe ProbeOperation(ItemStack input, ItemStack die, string operationCode)
        {
            var probe = new ForgePressOperationProbe();
            if (string.IsNullOrEmpty(operationCode)) return probe;
            foreach (var recipe in recipes)
            {
                if (recipe.OperationCode != operationCode) continue;
                probe.Exists = true;
                if (recipe.RequiresDie) probe.RequiresDie = true;
                if (input != null && recipe.MatchesInput(api?.World, config, input)) probe.InputMatches = true;
                if (recipe.MatchesDieStack(die)) probe.DieMatches = true;
            }
            return probe;
        }

        public string FirstOperationCode()
        {
            return operationCodes.Count > 0 ? operationCodes[0] : "";
        }

        public bool HasOperation(string code)
        {
            if (string.IsNullOrEmpty(code)) return false;
            for (int i = 0; i < operationCodes.Count; i++)
            {
                if (operationCodes[i] == code) return true;
            }
            return false;
        }
    }
}
