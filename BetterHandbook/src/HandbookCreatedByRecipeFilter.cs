using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace HandbookCache
{
    internal static class HandbookCreatedByRecipeFilter
    {
        [ThreadStatic]
        private static List<GridRecipe> activeRecipes;

        private static readonly object BuildLock = new object();
        private static bool built;
        private static Dictionary<int, List<GridRecipe>> recipesByOutputId = new Dictionary<int, List<GridRecipe>>();
        private static Dictionary<int, List<GridRecipe>> recipesByReturnedStackId = new Dictionary<int, List<GridRecipe>>();
        private static readonly List<GridRecipe> EmptyRecipes = new List<GridRecipe>(0);

        internal static void Begin(ItemSlot inSlot, ICoreClientAPI capi)
        {
            activeRecipes = null;
            if (!RecipeExplorer.BetterHandbookFeatures.CreatedByRecipeFilter) return;
            if (inSlot?.Itemstack?.Collectible == null || capi == null) return;

            EnsureBuilt(capi);

            int collectibleId = inSlot.Itemstack.Collectible.Id;
            recipesByOutputId.TryGetValue(collectibleId, out List<GridRecipe> outputRecipes);
            recipesByReturnedStackId.TryGetValue(collectibleId, out List<GridRecipe> returnedRecipes);

            if (outputRecipes == null && returnedRecipes == null)
            {
                activeRecipes = EmptyRecipes;
                return;
            }

            if (outputRecipes == null)
            {
                activeRecipes = returnedRecipes;
                return;
            }

            if (returnedRecipes == null)
            {
                activeRecipes = outputRecipes;
                return;
            }

            List<GridRecipe> combined = new List<GridRecipe>(outputRecipes.Count + returnedRecipes.Count);
            HashSet<GridRecipe> seen = new HashSet<GridRecipe>();
            for (int i = 0; i < outputRecipes.Count; i++)
            {
                if (seen.Add(outputRecipes[i])) combined.Add(outputRecipes[i]);
            }
            for (int i = 0; i < returnedRecipes.Count; i++)
            {
                if (seen.Add(returnedRecipes[i])) combined.Add(returnedRecipes[i]);
            }

            activeRecipes = combined;
        }

        internal static void End()
        {
            activeRecipes = null;
        }

        public static List<GridRecipe> GetFilteredGridRecipes(IWorldAccessor world)
        {
            if (!RecipeExplorer.BetterHandbookFeatures.CreatedByRecipeFilter)
            {
                return world?.GridRecipes ?? EmptyRecipes;
            }

            if (activeRecipes != null)
            {
                return activeRecipes;
            }

            return world?.GridRecipes ?? EmptyRecipes;
        }

        internal static void Clear()
        {
            lock (BuildLock)
            {
                built = false;
                recipesByOutputId = new Dictionary<int, List<GridRecipe>>();
                recipesByReturnedStackId = new Dictionary<int, List<GridRecipe>>();
                activeRecipes = null;
            }
        }

        private static void EnsureBuilt(ICoreClientAPI capi)
        {
            if (built) return;

            lock (BuildLock)
            {
                if (built) return;

                Stopwatch stopwatch = Stopwatch.StartNew();
                Dictionary<int, List<GridRecipe>> outputIndex = new Dictionary<int, List<GridRecipe>>();
                Dictionary<int, List<GridRecipe>> returnedIndex = new Dictionary<int, List<GridRecipe>>();
                int indexedRecipes = 0;

                foreach (GridRecipe recipe in ResolveGridRecipes(capi))
                {
                    if (recipe == null || !recipe.ShowInCreatedBy) continue;
                    bool indexed = false;

                    ItemStack outputStack = recipe.Output?.ResolvedItemStack;
                    if (outputStack?.Collectible != null)
                    {
                        Add(outputIndex, outputStack.Collectible.Id, recipe);
                        indexed = true;
                    }

                    CraftingRecipeIngredient[] ingredients = recipe.ResolvedIngredients;
                    if (ingredients != null)
                    {
                        for (int i = 0; i < ingredients.Length; i++)
                        {
                            ItemStack returnedStack = ingredients[i]?.ReturnedStack?.ResolvedItemStack;
                            if (returnedStack?.Collectible == null) continue;

                            Add(returnedIndex, returnedStack.Collectible.Id, recipe);
                            indexed = true;
                        }
                    }

                    if (indexed) indexedRecipes++;
                }

                recipesByOutputId = outputIndex;
                recipesByReturnedStackId = returnedIndex;
                built = true;

                HandbookCacheDiagnostics.Log(
                    capi,
                    "Created-by grid recipe filter built recipes={0} outputKeys={1} returnedKeys={2} elapsed={3}ms",
                    indexedRecipes,
                    outputIndex.Count,
                    returnedIndex.Count,
                    stopwatch.ElapsedMilliseconds);
            }
        }

        private static IEnumerable<GridRecipe> ResolveGridRecipes(ICoreClientAPI capi)
        {
            if (capi?.World?.GridRecipes != null && capi.World.GridRecipes.Count > 0)
            {
                return capi.World.GridRecipes;
            }

            try
            {
                object recipeRegistry = capi?.ModLoader.GetModSystem("Vintagestory.GameContent.RecipeRegistrySystem");
                if (recipeRegistry == null) return EmptyRecipes;

                PropertyInfo[] properties = recipeRegistry.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
                for (int i = 0; i < properties.Length; i++)
                {
                    object value = properties[i].GetValue(recipeRegistry);
                    if (!(value is System.Collections.IEnumerable enumerable)) continue;

                    List<GridRecipe> recipes = new List<GridRecipe>();
                    foreach (object entry in enumerable)
                    {
                        if (entry is GridRecipe recipe) recipes.Add(recipe);
                    }

                    if (recipes.Count > 0) return recipes;
                }
            }
            catch (Exception ex)
            {
                HandbookCacheDiagnostics.LogFailure(capi, "Created-by grid recipe filter failed to resolve registry recipes: {0}", ex.Message);
            }

            return EmptyRecipes;
        }

        private static void Add(Dictionary<int, List<GridRecipe>> index, int collectibleId, GridRecipe recipe)
        {
            if (!index.TryGetValue(collectibleId, out List<GridRecipe> recipes))
            {
                recipes = new List<GridRecipe>(4);
                index[collectibleId] = recipes;
            }

            if (recipes.Count == 0 || recipes[recipes.Count - 1] != recipe)
            {
                recipes.Add(recipe);
            }
        }
    }

    [HarmonyPatch(typeof(CollectibleBehaviorHandbookTextAndExtraInfo), "GetHandbookInfo")]
    internal static class HandbookGetInfoCreatedByFilterPatch
    {
        public static void Prefix(ItemSlot inSlot, ICoreClientAPI capi)
        {
            HandbookCreatedByRecipeFilter.Begin(inSlot, capi);
        }

        public static Exception Finalizer(Exception __exception)
        {
            HandbookCreatedByRecipeFilter.End();
            return __exception;
        }
    }

    [HarmonyPatch(typeof(CollectibleBehaviorHandbookTextAndExtraInfo), "addCreatedByInfo")]
    internal static class HandbookCreatedByGridRecipesPatch
    {
        private static readonly MethodInfo GridRecipesGetter = AccessTools.PropertyGetter(typeof(IWorldAccessor), nameof(IWorldAccessor.GridRecipes));
        private static readonly MethodInfo FilteredGridRecipesGetter = AccessTools.Method(typeof(HandbookCreatedByRecipeFilter), nameof(HandbookCreatedByRecipeFilter.GetFilteredGridRecipes));

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            bool patched = false;
            foreach (CodeInstruction instruction in instructions)
            {
                if (!patched && GridRecipesGetter != null && instruction.Calls(GridRecipesGetter))
                {
                    patched = true;
                    yield return new CodeInstruction(OpCodes.Call, FilteredGridRecipesGetter);
                    continue;
                }

                yield return instruction;
            }
        }
    }

    [HarmonyPatch(typeof(GuiDialogHandbook), "loadEntries")]
    internal static class HandbookCreatedByRecipeFilterInvalidationPatch
    {
        public static void Prefix()
        {
            HandbookCreatedByRecipeFilter.Clear();
        }
    }
}
