using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;
using Vintagestory.GameContent;

namespace RecipeExplorer
{
    [HarmonyPatch]
    internal static class HandbookRecipeOverlays
    {
        private const long OverlayDurationMs = 3000;

        private static readonly Dictionary<string, SlideshowGridRecipeTextComponent> ComponentByRecipeName =
            new Dictionary<string, SlideshowGridRecipeTextComponent>();

        private static readonly Dictionary<string, CraftingRecipeIngredient[]> IngredientsByRecipeName =
            new Dictionary<string, CraftingRecipeIngredient[]>();

        private static readonly FieldInfo GridSizeField = AccessTools.Field(typeof(SlideshowGridRecipeTextComponent), "size");

#pragma warning disable CS0649
        private static CraftingRecipeIngredient currentIngredient;
#pragma warning restore CS0649

        private static ICoreClientAPI api;
        private static CollectibleObject currentObject;
        private static int durabilityCost;
        private static SlideshowGridRecipeTextComponent lastFailedComponent;
        private static GridRecipe lastFailedRecipe;
        private static List<ItemSlot> lastFailedAvailable;
        private static long lastFailedTick;

        internal static void SetApi(ICoreClientAPI clientApi)
        {
            api = clientApi;
        }

        [HarmonyTranspiler]
        [HarmonyPatch(typeof(SlideshowGridRecipeTextComponent), "RenderInteractiveElements")]
        public static IEnumerable<CodeInstruction> CaptureRenderedGridIngredient(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo getElement = AccessTools.Method(typeof(GridRecipe), "GetElementInGrid", null, new[] { typeof(CraftingRecipeIngredient) });

            foreach (CodeInstruction instruction in instructions)
            {
                yield return instruction;

                if (getElement != null && instruction.Calls(getElement))
                {
                    yield return CodeInstruction.StoreField(typeof(HandbookRecipeOverlays), nameof(currentIngredient));
                    yield return CodeInstruction.LoadField(typeof(HandbookRecipeOverlays), nameof(currentIngredient), false);
                }

                if (instruction.opcode == OpCodes.Ret)
                {
                    yield return new CodeInstruction(OpCodes.Ldnull);
                    yield return CodeInstruction.StoreField(typeof(HandbookRecipeOverlays), nameof(currentIngredient));
                }
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(GuiDialogHandbook), "OnRenderGUI")]
        public static void ResetRenderState()
        {
            currentObject = null;
            durabilityCost = 0;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(CollectibleObject), "OnHandbookRecipeRender")]
        public static void MarkToolIngredients(object[] __args, CollectibleObject __instance)
        {
            if (api == null || currentIngredient == null || !currentIngredient.IsTool)
            {
                return;
            }

            double x;
            double y;
            double size;
            if (__args.Length >= 7)
            {
                x = (double)__args[3];
                y = (double)__args[4];
                size = (double)__args[6];
            }
            else
            {
                x = (double)__args[0];
                y = (double)__args[1];
                size = (double)__args[2];
            }

            LoadedTexture texture = HandbookRecipeAssets.Wrench.Texture;
            if (texture?.TextureId > 0)
            {
                float width = (float)(size * 0.4);
                float height = width / texture.Width * texture.Height;
                double margin = size * 0.05;
                api.Render.Render2DTexture(texture.TextureId, (float)(x - size / 2 + margin), (float)(y - size / 2 + margin), width, height, 110);
            }

            currentObject = __instance;
            durabilityCost = currentIngredient.ToolDurabilityCost;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(CollectibleObject), "GetHeldItemInfo")]
        public static void AddStackAndDurabilityInfo(StringBuilder dsc, CollectibleObject __instance)
        {
            if (!__instance.IsLiquid())
            {
                dsc.Append(Lang.Get("betterhandbook:stackSize", __instance.MaxStackSize));
            }

            if (durabilityCost > 0 && ReferenceEquals(__instance, currentObject))
            {
                dsc.Append(Lang.Get("betterhandbook:durability", durabilityCost));
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(CollectibleBehaviorHandbookTextAndExtraInfo), "addCreatedByInfo")]
        public static void SnapshotCreatedByRecipes(List<RichTextComponentBase> components)
        {
            if (components == null)
            {
                return;
            }

            for (int i = 0; i < components.Count; i++)
            {
                if (!(components[i] is SlideshowGridRecipeTextComponent component))
                {
                    continue;
                }

                GridRecipeAndUnnamedIngredients[] recipeData = component.GridRecipesAndUnnamedIngredients;
                if (recipeData == null)
                {
                    continue;
                }

                for (int recipeIndex = 0; recipeIndex < recipeData.Length; recipeIndex++)
                {
                    GridRecipe recipe = recipeData[recipeIndex]?.Recipe;
                    if (recipe?.Name == null)
                    {
                        continue;
                    }

                    string key = recipe.Name.ToString();
                    ComponentByRecipeName[key] = component;
                    if (recipe.ResolvedIngredients != null)
                    {
                        IngredientsByRecipeName[key] = recipe.ResolvedIngredients;
                    }
                }
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(SlideshowGridRecipeTextComponent), "RenderInteractiveElements")]
        public static void RenderMissingIngredientOverlay(SlideshowGridRecipeTextComponent __instance, double renderX, double renderY)
        {
            if (api == null || lastFailedComponent == null || !ReferenceEquals(__instance, lastFailedComponent))
            {
                return;
            }

            if (api.ElapsedMilliseconds - lastFailedTick > OverlayDurationMs)
            {
                ClearFailedFill();
                return;
            }

            CraftingRecipeIngredient[] ingredients = FindSnapshotIngredients(lastFailedRecipe);
            if (lastFailedRecipe == null || ingredients == null)
            {
                return;
            }

            HashSet<int> missing = ComputeMissingSlots(lastFailedRecipe, ingredients, lastFailedAvailable ?? new List<ItemSlot>());
            if (missing.Count == 0 || __instance.BoundsPerLine == null || __instance.BoundsPerLine.Length == 0)
            {
                return;
            }

            int textureId = HandbookRecipeAssets.RedOverlay.TextureId;
            if (textureId == 0)
            {
                return;
            }

            LineRectangled bounds = __instance.BoundsPerLine[0];
            double cellSize = GridSizeField != null ? (double)GridSizeField.GetValue(__instance) : bounds.Width / 3.2;
            double pitch = cellSize + GuiElement.scaled(3);
            double startX = renderX + bounds.X;
            double startY = renderY + bounds.Y;

            foreach (int slot in missing)
            {
                int row = slot / 3;
                int col = slot % 3;
                if (row >= lastFailedRecipe.Height || col >= lastFailedRecipe.Width) continue;

                api.Render.Render2DTexturePremultipliedAlpha(
                    textureId,
                    (float)(startX + col * pitch - 2),
                    (float)(startY + row * pitch),
                    (float)cellSize,
                    (float)cellSize,
                    105);
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(GuiDialogInventory), "OnGuiClosed")]
        public static void ClearInventoryScopedState()
        {
            ClearFailedFill();
        }

        internal static void ReportFailedFill(IEnumerable<GridRecipe> triedRecipes)
        {
            List<ItemSlot> available = CollectAvailableSlots(includeOpenContainers: true, includeCraftingGrid: true);
            SlideshowGridRecipeTextComponent bestComponent = null;
            GridRecipe bestRecipe = null;
            int bestMissingCount = int.MaxValue;

            foreach (GridRecipe recipe in triedRecipes ?? Enumerable.Empty<GridRecipe>())
            {
                if (recipe?.Name == null)
                {
                    continue;
                }

                string key = recipe.Name.ToString();
                if (!ComponentByRecipeName.TryGetValue(key, out SlideshowGridRecipeTextComponent component))
                {
                    continue;
                }

                CraftingRecipeIngredient[] ingredients = FindSnapshotIngredients(recipe);
                if (ingredients == null)
                {
                    continue;
                }

                int missingCount = ComputeMissingSlots(recipe, ingredients, available).Count;
                if (missingCount >= bestMissingCount)
                {
                    continue;
                }

                bestComponent = component;
                bestRecipe = recipe;
                bestMissingCount = missingCount;
            }

            if (bestComponent == null)
            {
                ClearFailedFill();
                return;
            }

            lastFailedComponent = bestComponent;
            lastFailedRecipe = bestRecipe;
            lastFailedAvailable = available;
            lastFailedTick = api?.ElapsedMilliseconds ?? 0;
        }

        internal static void ClearFailedFill()
        {
            lastFailedComponent = null;
            lastFailedRecipe = null;
            lastFailedAvailable = null;
        }

        private static CraftingRecipeIngredient[] FindSnapshotIngredients(GridRecipe recipe)
        {
            if (recipe?.Name != null && IngredientsByRecipeName.TryGetValue(recipe.Name.ToString(), out CraftingRecipeIngredient[] ingredients))
            {
                return ingredients;
            }

            return recipe?.ResolvedIngredients;
        }

        private static HashSet<int> ComputeMissingSlots(GridRecipe recipe, CraftingRecipeIngredient[] ingredients, List<ItemSlot> available)
        {
            var missing = new HashSet<int>();
            var counts = new Dictionary<string, int>();
            CraftingRecipeIngredient[] matchingIngredients = BuildOriginalIngredients(recipe, ingredients);

            for (int row = 0; row < recipe.Height; row++)
            {
                for (int col = 0; col < recipe.Width; col++)
                {
                    CraftingRecipeIngredient displayIngredient = recipe.GetElementInGrid(row, col, ingredients, recipe.Width);
                    CraftingRecipeIngredient matchingIngredient = recipe.GetElementInGrid(row, col, matchingIngredients, recipe.Width);
                    if (displayIngredient == null || matchingIngredient == null)
                    {
                        continue;
                    }

                    int slotIndex = row * 3 + col;
                    if (displayIngredient.IsTool || matchingIngredient.IsTool || HasDurabilityTool(matchingIngredient, available))
                    {
                        int durabilityCost = Math.Max(displayIngredient.ToolDurabilityCost, matchingIngredient.ToolDurabilityCost);
                        if (!available.Any(slot => SlotSatisfies(slot, matchingIngredient) && slot.Itemstack.Collectible.GetRemainingDurability(slot.Itemstack) >= durabilityCost))
                        {
                            missing.Add(slotIndex);
                        }
                        continue;
                    }

                    string key = IngredientKey.Create(matchingIngredient).Key;
                    if (!counts.TryGetValue(key, out int count))
                    {
                        count = available.Where(slot => SlotSatisfies(slot, matchingIngredient)).Sum(slot => slot.StackSize);
                    }

                    int requiredQuantity = displayIngredient.Quantity;
                    if (count < requiredQuantity)
                    {
                        missing.Add(slotIndex);
                        counts[key] = 0;
                    }
                    else
                    {
                        counts[key] = count - requiredQuantity;
                    }
                }
            }

            return missing;
        }

        private static CraftingRecipeIngredient[] BuildOriginalIngredients(GridRecipe recipe, CraftingRecipeIngredient[] fallbackIngredients)
        {
            if (recipe?.Ingredients == null || fallbackIngredients == null)
            {
                return fallbackIngredients;
            }

            var originalIngredients = new CraftingRecipeIngredient[fallbackIngredients.Length];
            string flatPattern = recipe.IngredientPattern?
                .Replace(",", "")
                .Replace("\t", "")
                .Replace("\r", "")
                .Replace("\n", "") ?? "";

            for (int i = 0; i < fallbackIngredients.Length; i++)
            {
                CraftingRecipeIngredient ingredient = fallbackIngredients[i];
                if (ingredient == null)
                {
                    continue;
                }

                CraftingRecipeIngredient originalIngredient = ingredient;
                if (i < flatPattern.Length && flatPattern[i] != '_')
                {
                    string patternChar = flatPattern[i].ToString();
                    if (recipe.Ingredients.TryGetValue(patternChar, out CraftingRecipeIngredient mappedIngredient) && mappedIngredient != null)
                    {
                        originalIngredient = mappedIngredient;
                    }
                }

                originalIngredients[i] = originalIngredient;
            }

            return originalIngredients;
        }

        private static List<ItemSlot> CollectAvailableSlots(bool includeOpenContainers, bool includeCraftingGrid)
        {
            var slots = new List<ItemSlot>();
            IPlayerInventoryManager manager = api?.World?.Player?.InventoryManager;
            if (manager == null)
            {
                return slots;
            }

            AddSlots(slots, manager.GetOwnInventory("backpack"));
            AddSlots(slots, manager.GetHotbarInventory(), slot => !(slot is ItemSlotBackpack));

            if (includeOpenContainers)
            {
                foreach (InventoryGeneric inventory in manager.OpenedInventories.OfType<InventoryGeneric>())
                {
                    AddSlots(slots, inventory);
                }
            }

            if (includeCraftingGrid)
            {
                AddSlots(slots, manager.GetOwnInventory("craftinggrid"));
            }

            return slots.Where(slot => slot != null && !slot.Empty).ToList();
        }

        private static bool SlotSatisfies(ItemSlot slot, CraftingRecipeIngredient ingredient)
        {
            return slot?.Itemstack != null
                && slot.StackSize > 0
                && ingredient != null
                && AutoCraftSystem.DoesSlotMatchIngredient(slot, ingredient)
                && (!ingredient.IsTool || slot.Itemstack.Collectible.GetRemainingDurability(slot.Itemstack) >= ingredient.ToolDurabilityCost);
        }

        private static bool HasDurabilityTool(CraftingRecipeIngredient ingredient, List<ItemSlot> slots)
        {
            return slots.Any(slot =>
                SlotSatisfies(slot, ingredient)
                && slot.Itemstack.Collectible.MaxStackSize == 1
                && slot.Itemstack.Collectible.GetMaxDurability(slot.Itemstack) > 0);
        }

        private static void AddSlots(List<ItemSlot> slots, IInventory inventory, System.Func<ItemSlot, bool> filter = null)
        {
            if (inventory == null)
            {
                return;
            }

            foreach (ItemSlot slot in inventory)
            {
                if (filter == null || filter(slot))
                {
                    slots.Add(slot);
                }
            }
        }
    }

    internal readonly struct IngredientKey
    {
        private readonly string[] include;
        private readonly string[] exclude;

        public readonly AssetLocation Code;
        public readonly bool Wild;
        public string Key { get; }

        private IngredientKey(CraftingRecipeIngredient ingredient)
        {
            Code = ingredient.Code;
            Wild = ingredient.MatchingType != EnumRecipeMatchType.Exact;
            include = Wild ? ingredient.AllowedVariants : null;
            exclude = Wild ? ingredient.SkipVariants : null;
            Key = MakeKey(Code, include, exclude);
        }

        public static IngredientKey Create(CraftingRecipeIngredient ingredient)
        {
            return new IngredientKey(ingredient);
        }

        private static string MakeKey(AssetLocation code, string[] include, string[] exclude)
        {
            var builder = new StringBuilder();
            builder.Append(code);
            AddArray(builder, include, '[');
            AddArray(builder, exclude, ']');
            return builder.ToString();
        }

        private static void AddArray(StringBuilder builder, string[] values, char prefix)
        {
            if (values == null || values.Length == 0)
            {
                return;
            }

            builder.Append(prefix);
            for (int i = 0; i < values.Length; i++)
            {
                if (i > 0) builder.Append(',');
                builder.Append(values[i]);
            }
        }
    }
}
