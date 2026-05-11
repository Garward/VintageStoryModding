using System;
using System.Collections.Generic;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace VintageKinematics.Patches
{
    // Vanilla CookingRecipe.GenerateRandomMeal indexes validStacks[Rand.Next(validStacks.Count)]
    // without first checking Count > 0. If any ingredient with MinQuantity > 0 ends up with zero
    // matched stacks (e.g. a wildcard or item code that doesn't resolve in the player's modset),
    // the handbook crashes the client when it tries to render the meal recipe page.
    //
    // This finalizer wraps the call: on throw, log the recipe + per-ingredient diagnostics once,
    // then return an empty meal array so the handbook continues running. The log identifies which
    // mod's recipe is to blame so the offending content can be fixed at the source.
    [HarmonyPatch(typeof(CookingRecipe), nameof(CookingRecipe.GenerateRandomMeal))]
    public static class CookingRecipeHandbookSafetyPatch
    {
        private static readonly HashSet<string> loggedRecipes = new();

        static Exception Finalizer(Exception __exception, ref ItemStack[] __result, CookingRecipe __instance, ICoreAPI api, ItemStack[] allstacks, int slots)
        {
            if (__exception == null) return null;

            string code = __instance?.Code ?? "<null>";
            if (loggedRecipes.Add(code))
            {
                ILogger log = api?.Logger;
                log?.Error("[VK] CookingRecipe.GenerateRandomMeal threw {0} for recipe '{1}' (slots={2}, allstacks={3}). Suppressing to keep handbook alive.",
                    __exception.GetType().Name, code, slots, allstacks?.Length ?? 0);
                log?.Error("[VK] Exception: {0}", __exception.Message);

                if (__instance?.Ingredients != null && allstacks != null)
                {
                    foreach (CookingRecipeIngredient ing in __instance.Ingredients)
                    {
                        int matched = 0;
                        foreach (ItemStack astack in allstacks)
                        {
                            try { if (ing.GetMatchingStack(astack) != null) matched++; }
                            catch { }
                        }
                        log?.Error("[VK]   ingredient code='{0}' minQty={1} maxQty={2} portionSizeLitres={3} validStacks={4} matchedInAllstacks={5}",
                            ing.Code, ing.MinQuantity, ing.MaxQuantity, ing.PortionSizeLitres, ing.ValidStacks?.Length ?? 0, matched);

                        if (ing.ValidStacks != null)
                        {
                            foreach (CookingRecipeStack vs in ing.ValidStacks)
                            {
                                log?.Error("[VK]     validStack code='{0}' resolved={1}", vs?.Code, vs?.ResolvedItemstack != null);
                            }
                        }
                    }
                }
            }

            __result = new ItemStack[slots];
            return null;
        }
    }
}
