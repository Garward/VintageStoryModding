using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace HandbookCache
{
    /// <summary>
    /// Prevents barrel handbook rendering from mutating canonical crafting recipe
    /// stacks through CraftingRecipeIngredient.defaultEmptyAttributes.
    /// Temporary compatibility fix for https://github.com/anegostudios/VintageStory-Issues/issues/9175.
    /// </summary>
    [HarmonyPatch(typeof(CollectibleBehaviorHandbookTextAndExtraInfo), "BuildBarrelRecipesText")]
    internal static class HandbookBarrelRecipeStackIsolationPatch
    {
        [HarmonyPostfix]
        private static void Postfix(List<RichTextComponentBase> __result)
        {
            if (__result == null)
            {
                return;
            }

            foreach (SlideshowItemstackTextComponent slideshow in __result.OfType<SlideshowItemstackTextComponent>())
            {
                ItemStack[] source = slideshow.Itemstacks;
                if (source == null)
                {
                    continue;
                }

                ItemStack[] isolated = new ItemStack[source.Length];
                for (int i = 0; i < source.Length; i++)
                {
                    isolated[i] = source[i]?.Clone();
                }

                slideshow.Itemstacks = isolated;
            }
        }
    }
}
