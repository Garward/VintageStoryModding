using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Util;

namespace VintageKinematics.Crafting
{
    /// <summary>
    /// One kinetic mixer recipe. Solid ingredients are matched unordered against the mixer input
    /// slots; liquid input is consumed from the mixer's buffer or an adjacent liquid container.
    /// </summary>
    public class KineticMixerRecipe
    {
        public string Code;
        public JsonItemStack[] Ingredients;
        public JsonItemStack[] Outputs;
        public AssetLocation LiquidCode;
        public float LiquidLitres;
        public int MixTicks = 6;

        public bool Matches(ItemStack[] inputs, ItemStack liquidStack, float liquidLitres)
        {
            if (!HasRequiredLiquid(liquidStack, liquidLitres)) return false;
            return TryMapIngredients(inputs, null);
        }

        public bool TryMapIngredients(ItemStack[] inputs, int[] slotByIngredient)
        {
            if (Ingredients == null || Ingredients.Length == 0) return false;
            if (inputs == null) return false;
            if (slotByIngredient != null && slotByIngredient.Length < Ingredients.Length) return false;

            bool[] used = new bool[inputs.Length];
            for (int i = 0; i < Ingredients.Length; i++)
            {
                JsonItemStack ingredient = Ingredients[i];
                if (ingredient?.Code == null) return false;

                int slotId = FindSlotFor(ingredient, inputs, used);
                if (slotId < 0) return false;

                used[slotId] = true;
                if (slotByIngredient != null) slotByIngredient[i] = slotId;
            }
            return true;
        }

        public bool IsPotentialIngredient(ItemStack stack)
        {
            if (stack == null || Ingredients == null) return false;
            foreach (JsonItemStack ingredient in Ingredients)
            {
                if (ingredient?.Code == null) continue;
                if (WildcardUtil.Match(ingredient.Code, stack.Collectible.Code)) return true;
            }
            return false;
        }

        public bool HasRequiredLiquid(ItemStack liquidStack, float liquidLitres)
        {
            if (LiquidCode == null || LiquidLitres <= 0f) return true;
            if (liquidStack?.Collectible == null || liquidLitres + 0.0001f < LiquidLitres) return false;
            return WildcardUtil.Match(LiquidCode, liquidStack.Collectible.Code);
        }

        public bool MatchesLiquid(ItemStack stack)
        {
            if (LiquidCode == null || stack?.Collectible == null) return false;
            return WildcardUtil.Match(LiquidCode, stack.Collectible.Code);
        }

        public bool Resolve(IWorldAccessor world, string sourceForErrors)
        {
            if (Ingredients == null || Ingredients.Length == 0) return false;

            foreach (JsonItemStack ingredient in Ingredients)
            {
                if (ingredient?.Code == null) return false;
                if (ingredient.Code.Path?.Contains('*') != true)
                {
                    ingredient.Resolve(world, sourceForErrors);
                }
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

            if (LiquidCode != null && LiquidCode.Path?.Contains('*') != true)
            {
                Item item = world.GetItem(LiquidCode);
                if (item == null || item.IsMissing) return false;
            }

            return true;
        }

        private static int FindSlotFor(JsonItemStack ingredient, ItemStack[] inputs, bool[] used)
        {
            for (int i = 0; i < inputs.Length; i++)
            {
                if (used[i]) continue;
                ItemStack stack = inputs[i];
                if (stack?.Collectible == null) continue;
                if (stack.StackSize < ingredient.StackSize) continue;
                if (WildcardUtil.Match(ingredient.Code, stack.Collectible.Code)) return i;
            }
            return -1;
        }
    }

    /// <summary>
    /// Loads mixer recipes from <c>assets/&lt;mod&gt;/vkrecipe/mixer/*.json</c> across loaded mods.
    /// </summary>
    public class KineticMixerRecipeRegistry : ModSystem
    {
        private readonly List<KineticMixerRecipe> recipes = new List<KineticMixerRecipe>();

        public IReadOnlyList<KineticMixerRecipe> Recipes => recipes;

        public override double ExecuteOrder() => 1.0;

        public override void AssetsFinalize(ICoreAPI api)
        {
            base.AssetsFinalize(api);
            recipes.Clear();

            var assets = api.Assets.GetMany<KineticMixerRecipe>(api.Logger, "vkrecipe/mixer/");
            foreach (var entry in assets)
            {
                KineticMixerRecipe recipe = entry.Value;
                if (recipe == null) continue;
                if (recipe.Resolve(api.World, entry.Key.ToString()))
                {
                    recipe.Code = entry.Key.ToString();
                    recipes.Add(recipe);
                }
            }

            api.Logger.Notification($"[VintageKinematics] Loaded {recipes.Count} mixer recipe(s)");
        }

        public KineticMixerRecipe FindRecipe(ItemStack[] inputs, ItemStack liquidStack, float liquidLitres)
        {
            KineticMixerRecipe best = null;
            foreach (KineticMixerRecipe recipe in recipes)
            {
                if (!recipe.Matches(inputs, liquidStack, liquidLitres)) continue;
                if (IsMoreSpecific(recipe, best)) best = recipe;
            }
            return best;
        }

        public KineticMixerRecipe FindRecipeByCode(string code)
        {
            if (string.IsNullOrEmpty(code)) return null;
            foreach (KineticMixerRecipe recipe in recipes)
            {
                if (recipe?.Code == code) return recipe;
            }
            return null;
        }

        public KineticMixerRecipe FindPotentialRecipeFor(ItemStack stack)
        {
            if (stack == null) return null;
            foreach (KineticMixerRecipe recipe in recipes)
            {
                if (recipe.IsPotentialIngredient(stack)) return recipe;
            }
            return null;
        }

        public KineticMixerRecipe FindPotentialRecipeForLiquid(ItemStack stack)
        {
            if (stack == null) return null;
            foreach (KineticMixerRecipe recipe in recipes)
            {
                if (recipe.MatchesLiquid(stack)) return recipe;
            }
            return null;
        }

        private static bool IsMoreSpecific(KineticMixerRecipe candidate, KineticMixerRecipe current)
        {
            if (current == null) return true;

            int candidateQuantity = TotalIngredientQuantity(candidate);
            int currentQuantity = TotalIngredientQuantity(current);
            if (candidateQuantity != currentQuantity) return candidateQuantity > currentQuantity;

            int candidateCount = candidate?.Ingredients?.Length ?? 0;
            int currentCount = current?.Ingredients?.Length ?? 0;
            return candidateCount > currentCount;
        }

        private static int TotalIngredientQuantity(KineticMixerRecipe recipe)
        {
            if (recipe?.Ingredients == null) return 0;
            int total = 0;
            for (int i = 0; i < recipe.Ingredients.Length; i++)
            {
                total += System.Math.Max(1, recipe.Ingredients[i]?.StackSize ?? 1);
            }
            return total;
        }
    }
}
