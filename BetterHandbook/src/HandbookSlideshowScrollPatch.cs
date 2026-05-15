using System;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace HandbookCache
{
    [HarmonyPatch(typeof(GuiElement), nameof(GuiElement.OnMouseWheel))]
    internal static class HandbookSlideshowScrollPatch
    {
        private static readonly FieldInfo ItemIndexField = AccessTools.Field(typeof(SlideshowItemstackTextComponent), "curItemIndex");
        private static readonly FieldInfo ItemSecondsVisibleField = AccessTools.Field(typeof(SlideshowItemstackTextComponent), "secondsVisible");
        private static readonly FieldInfo GridIndexField = AccessTools.Field(typeof(SlideshowGridRecipeTextComponent), "currentItemIndex");
        private static readonly FieldInfo GridSecondCounterField = AccessTools.Field(typeof(SlideshowGridRecipeTextComponent), "secondCounter");
        private static readonly FieldInfo GridSecondsVisibleField = AccessTools.Field(typeof(SlideshowGridRecipeTextComponent), "secondsVisible");
        private static readonly FieldInfo GridSizeField = AccessTools.Field(typeof(SlideshowGridRecipeTextComponent), "size");
        private static readonly FieldInfo GridVariantSequenceField = AccessTools.Field(typeof(SlideshowGridRecipeTextComponent), "variantDisplaySequence");

        public static void Prefix(GuiElement __instance, ICoreClientAPI api, MouseWheelEventArgs args)
        {
            if (args.IsHandled || !(__instance is GuiElementRichtext richtext) || api == null)
            {
                return;
            }

            if (!richtext.IsPositionInside(api.Input.MouseX, api.Input.MouseY))
            {
                return;
            }

            int direction = Math.Sign(args.deltaPrecise);
            if (direction == 0)
            {
                direction = Math.Sign(args.delta);
            }
            if (direction == 0)
            {
                return;
            }

            int relX = (int)((double)api.Input.MouseX - richtext.Bounds.absX);
            int relY = (int)((double)api.Input.MouseY - richtext.Bounds.absY);

            RichTextComponentBase[] components = richtext.Components;
            for (int i = 0; i < components.Length; i++)
            {
                RichTextComponentBase component = components[i];
                if (!PointInsideComponent(component, relX, relY))
                {
                    continue;
                }

                if (TryScrollComponent(component, relX, relY, direction))
                {
                    args.SetHandled();
                    return;
                }
            }
        }

        private static bool TryScrollComponent(RichTextComponentBase component, int relX, int relY, int direction)
        {
            if (component is SlideshowItemstackTextComponent itemstackComponent)
            {
                return ScrollItemstackComponent(itemstackComponent, direction);
            }

            if (component is SlideshowGridRecipeTextComponent gridComponent)
            {
                return ScrollGridRecipeComponent(gridComponent, relX, relY, direction);
            }

            return false;
        }

        private static bool ScrollItemstackComponent(SlideshowItemstackTextComponent component, int direction)
        {
            if (component.Itemstacks == null || component.Itemstacks.Length <= 1 || ItemIndexField == null)
            {
                return false;
            }

            int currentIndex = (int)ItemIndexField.GetValue(component);
            ItemIndexField.SetValue(component, Mod(currentIndex + direction, component.Itemstacks.Length));
            ItemSecondsVisibleField?.SetValue(component, 1f);
            return true;
        }

        private static bool ScrollGridRecipeComponent(SlideshowGridRecipeTextComponent component, int relX, int relY, int direction)
        {
            GridRecipeAndUnnamedIngredients[] recipes = component.GridRecipesAndUnnamedIngredients;
            if (recipes == null || recipes.Length == 0 || GridIndexField == null)
            {
                return false;
            }

            int currentIndex = (int)GridIndexField.GetValue(component);
            currentIndex = Mod(currentIndex, recipes.Length);

            if (TryScrollHoveredIngredientVariant(component, recipes[currentIndex], relX, relY, direction))
            {
                GridSecondsVisibleField?.SetValue(component, 1f);
                return true;
            }

            if (recipes.Length <= 1)
            {
                return false;
            }

            GridIndexField.SetValue(component, Mod(currentIndex + direction, recipes.Length));
            GridSecondsVisibleField?.SetValue(component, 1f);
            return true;
        }

        private static bool TryScrollHoveredIngredientVariant(
            SlideshowGridRecipeTextComponent component,
            GridRecipeAndUnnamedIngredients recipeAndIngredients,
            int relX,
            int relY,
            int direction)
        {
            if (recipeAndIngredients == null || recipeAndIngredients.Recipe == null || recipeAndIngredients.UnnamedIngredients == null)
            {
                return false;
            }
            if (GridSizeField == null || GridSecondCounterField == null || GridVariantSequenceField == null)
            {
                return false;
            }

            LineRectangled bounds = component.BoundsPerLine[0];
            double size = (double)GridSizeField.GetValue(component);
            double cellPitch = size + GuiElement.scaled(3.0);
            int cellX = (int)(((double)relX - bounds.X) / cellPitch);
            int cellY = (int)(((double)relY - bounds.Y) / cellPitch);
            if (cellX < 0 || cellX >= 3 || cellY < 0 || cellY >= 3)
            {
                return false;
            }

            double localX = (double)relX - bounds.X - (double)cellX * cellPitch;
            double localY = (double)relY - bounds.Y - (double)cellY * cellPitch;
            if (localX < 0 || localX >= size || localY < 0 || localY >= size)
            {
                return false;
            }

            GridRecipe recipe = recipeAndIngredients.Recipe;
            if (recipe.ResolvedIngredients == null)
            {
                return false;
            }

            int ingredientIndex = recipe.GetGridIndex(cellY, cellX, recipe.ResolvedIngredients, recipe.Width);
            if (!recipeAndIngredients.UnnamedIngredients.TryGetValue(ingredientIndex, out ItemStack[] variants) || variants == null || variants.Length <= 1)
            {
                return false;
            }

            int secondCounter = (int)GridSecondCounterField.GetValue(component);
            int[][,] variantDisplaySequence = (int[][,])GridVariantSequenceField.GetValue(null);
            if (variantDisplaySequence == null || variantDisplaySequence.Length == 0)
            {
                return false;
            }

            int frame = Mod(secondCounter, variantDisplaySequence.Length);
            int[,] displayFrame = variantDisplaySequence[frame];
            int currentVariant = Mod(displayFrame[cellX, cellY], variants.Length);
            displayFrame[cellX, cellY] = Mod(currentVariant + direction, variants.Length);
            return true;
        }

        private static bool PointInsideComponent(RichTextComponentBase component, int relX, int relY)
        {
            if (component?.BoundsPerLine == null)
            {
                return false;
            }

            for (int i = 0; i < component.BoundsPerLine.Length; i++)
            {
                if (component.BoundsPerLine[i].PointInside(relX, relY))
                {
                    return true;
                }
            }

            return false;
        }

        private static int Mod(int value, int divisor)
        {
            int result = value % divisor;
            return result < 0 ? result + divisor : result;
        }
    }
}
