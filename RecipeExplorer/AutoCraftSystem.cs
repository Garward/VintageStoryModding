using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Util;
using Vintagestory.GameContent;
using System.Linq.Expressions;

namespace RecipeExplorer
{
    /// <summary>
    /// Auto-craft system that adds buttons to handbook pages to automatically fill crafting grids
    /// </summary>
    public class AutoCraftSystem
    {
        private static ICoreClientAPI capi;

        // Reflection fields to access private handbook data (initialized dynamically)
        private static FieldInfo BrowseHistoryField = null;

        public static void Initialize(ICoreClientAPI api, Harmony harmony)
        {
            capi = api;

            try
            {
                // Try multiple ways to find GuiDialogHandbook type
                Type handbookType = null;

                // Method 1: AccessTools.TypeByName
                handbookType = AccessTools.TypeByName("Vintagestory.GameContent.GuiDialogHandbook");
                if (handbookType == null)
                {
                    // Method 2: Search all loaded types
                    foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        handbookType = assembly.GetType("Vintagestory.GameContent.GuiDialogHandbook", false);
                        if (handbookType != null) break;
                    }
                }

                if (handbookType == null)
                {
                    capi.Logger.Error("[RecipeExplorer] Could not find GuiDialogHandbook type");
                    return;
                }

                // Patch the handbook detail GUI to add our auto-craft button
                var initDetailGuiMethod = AccessTools.Method(handbookType, "initDetailGui");
                if (initDetailGuiMethod == null)
                {
                    capi.Logger.Error("[RecipeExplorer] Could not find initDetailGui method");
                    return;
                }

                harmony.Patch(
                    initDetailGuiMethod,
                    postfix: new HarmonyMethod(typeof(AutoCraftSystem), nameof(InitDetailGuiPostfix))
                );

                capi.Logger.Notification("[RecipeExplorer] Auto-craft system initialized successfully");
            }
            catch (Exception ex)
            {
                capi.Logger.Error("[RecipeExplorer] Failed to initialize auto-craft system: {0}", ex);
            }
        }

        public static void InitDetailGuiPostfix(object __instance)
        {
            if (__instance == null) return;

            try
            {
                var handbookType = __instance.GetType();

                // Initialize BrowseHistoryField if needed
                if (BrowseHistoryField == null)
                {
                    BrowseHistoryField = AccessTools.Field(handbookType, "browseHistory");
                }

                // Get the detail view composer
                var detailGuiField = AccessTools.Field(handbookType, "detailViewGui");
                var detailViewGui = detailGuiField?.GetValue(__instance) as GuiComposer;
                if (detailViewGui == null) return;

                // Get current page to check if it shows crafting recipes
                var browseHistory = BrowseHistoryField?.GetValue(__instance);
                if (browseHistory == null) return;

                // Use reflection to check count and peek
                int count = (int)(browseHistory.GetType().GetProperty("Count")?.GetValue(browseHistory) ?? 0);
                if (count == 0) return;

                var peekMethod = browseHistory.GetType().GetMethod("Peek");
                var currentElement = peekMethod?.Invoke(browseHistory, null);
                if (currentElement == null) return;

                // Use AccessTools to get the Page field
                var pageField = AccessTools.Field(currentElement.GetType(), "Page");
                if (pageField == null) return;

                var currentPage = pageField.GetValue(currentElement);
                if (currentPage == null) return;

                // Check if it's an ItemStack page
                var stackField = AccessTools.Field(currentPage.GetType(), "Stack");
                if (stackField == null) return;

                var stack = stackField.GetValue(currentPage) as ItemStack;
                if (stack == null) return;

                // Add auto-craft button
                AddAutoCraftButton(__instance, detailViewGui, stack);
            }
            catch (Exception ex)
            {
                capi.Logger.Error("[RecipeExplorer] Failed in InitDetailGuiPostfix: {0}", ex);
            }
        }

        private static void AddAutoCraftButton(object dialog, GuiComposer detailViewGui, ItemStack stack)
        {
            // Check if there's already a button
            if (detailViewGui.GetButton("autocraft-button") != null) return;

            // Find crafting recipes for this item
            var recipes = FindCraftingRecipes(stack);
            if (recipes.Count == 0) return;

            // Try to get existing scrollbar to position relative to it
            var scrollbar = detailViewGui.GetScrollbar("scrollbar");
            ElementBounds buttonBounds;

            if (scrollbar != null)
            {
                buttonBounds = ElementBounds
                    .FixedSize(0, 0)
                    .FixedUnder(scrollbar.Bounds, 2 * 5 + 5)
                    .WithAlignment(EnumDialogArea.LeftFixed)
                    .WithFixedPadding(20, 4)
                    .WithFixedAlignmentOffset(110, 1);
            }
            else
            {
                buttonBounds = ElementBounds.Fixed(EnumDialogArea.LeftBottom, 120, -16.5, 90, 30);
            }

            // Create the auto-craft button
            var button = new GuiElementTextButton(
                capi,
                "Auto-Fill",
                CairoFont.SmallButtonText(EnumButtonStyle.Normal),
                CairoFont.SmallButtonText(EnumButtonStyle.Normal),
                () => OnAutoCraftClicked(recipes),
                buttonBounds,
                EnumButtonStyle.Normal
            );

            button.Bounds.CalcWorldBounds();
            detailViewGui.AddInteractiveElement(button, "autocraft-button");
            detailViewGui.ReCompose();
        }

        private static List<GridRecipe> FindCraftingRecipes(ItemStack output)
        {
            var recipes = new List<GridRecipe>();

            foreach (var recipe in capi.World.GridRecipes)
            {
                if (recipe?.Output?.ResolvedItemStack == null) continue;

                if (recipe.Output.ResolvedItemStack.Collectible.Code.Equals(output.Collectible.Code))
                {
                    recipes.Add(recipe);
                }
            }

            return recipes;
        }

        private static bool OnAutoCraftClicked(List<GridRecipe> recipes)
        {
            if (recipes.Count == 0)
            {
                if (RecipeExplorerMod.Config.ShowAutoFillMessages)
                    capi.ShowChatMessage("No recipes found");
                return true;
            }

            // Find the player's crafting grid
            var craftingGrid = FindPlayerCraftingGrid();
            if (craftingGrid == null)
            {
                if (RecipeExplorerMod.Config.ShowAutoFillMessages)
                    capi.ShowChatMessage("Please open your inventory or a crafting table first");
                return true;
            }

            // Check if shift is held for fill-max mode
            bool shiftHeld = capi.Input.KeyboardKeyStateRaw[(int)GlKeys.ShiftLeft] ||
                             capi.Input.KeyboardKeyStateRaw[(int)GlKeys.ShiftRight];

            bool success = false;

            foreach (var recipe in recipes)
            {
                success = TryFillCraftingGrid(recipe, craftingGrid, shiftHeld);
                if (success) break;
            }

            if (success)
            {
                if (RecipeExplorerMod.Config.ShowAutoFillMessages)
                    capi.ShowChatMessage(shiftHeld ? "Crafting grid filled (max)!" : "Crafting grid filled!");
                capi.Gui.PlaySound("menubutton_press");
            }
            else
            {
                if (RecipeExplorerMod.Config.ShowAutoFillMessages)
                    capi.ShowChatMessage("Missing ingredients or crafting grid is too small");
            }

            return true;
        }

        private static InventoryBase FindPlayerCraftingGrid()
        {
            var player = capi.World.Player;
            if (player?.InventoryManager == null) return null;

            var craftingInv = player.InventoryManager.GetOwnInventory("craftinggrid") as InventoryBase;
            if (craftingInv != null) return craftingInv;

            var charInv = player.InventoryManager.GetOwnInventory("character") as InventoryBase;
            return charInv;
        }

        private static bool TryFillCraftingGrid(GridRecipe recipe, InventoryBase craftingGrid, bool fillMax = false)
        {
            if (recipe == null || recipe.Ingredients == null || craftingGrid == null) return false;

            // Get resolvedIngredients array
            var resolvedIngredientsField = AccessTools.Field(recipe.GetType(), "resolvedIngredients");
            CraftingRecipeIngredient[] resolvedIngredients = null;

            if (resolvedIngredientsField != null)
            {
                resolvedIngredients = resolvedIngredientsField.GetValue(recipe) as CraftingRecipeIngredient[];
            }

            if (resolvedIngredients == null) return false;

            // Check grid size
            int recipeWidth = recipe.Width;
            int recipeHeight = recipe.Height;
            int gridWidth = (int)Math.Sqrt(craftingGrid.Count);
            int gridHeight = gridWidth;

            if (recipeWidth > gridWidth || recipeHeight > gridHeight) return false;

            // Get player inventory
            var player = capi.World.Player;
            if (player == null) return false;

            var playerInv = player.InventoryManager?.GetOwnInventory("backpack") as InventoryBase;
            var hotbarInv = player.InventoryManager?.GetOwnInventory("hotbar") as InventoryBase;

            if (playerInv == null && hotbarInv == null) return false;

            // Build ingredient info list
            var ingredientSlots = new ItemSlot[resolvedIngredients.Length];
            var originalIngredients = new CraftingRecipeIngredient[resolvedIngredients.Length];
            var missingIngredients = new List<string>();

            string pattern = recipe.IngredientPattern;
            string flatPattern = pattern?.Replace(",", "") ?? "";

            // Find matching items in player inventory
            for (int i = 0; i < resolvedIngredients.Length; i++)
            {
                var ingredient = resolvedIngredients[i];

                if (ingredient == null)
                {
                    ingredientSlots[i] = null;
                    originalIngredients[i] = null;
                    continue;
                }

                // Get the original ingredient from recipe.Ingredients dictionary
                CraftingRecipeIngredient originalIngredient = ingredient;
                if (recipe.Ingredients != null && i < flatPattern.Length && flatPattern[i] != '_')
                {
                    string patternChar = flatPattern[i].ToString();
                    if (recipe.Ingredients.TryGetValue(patternChar, out var origIng) && origIng != null)
                    {
                        originalIngredient = origIng;
                    }
                }
                originalIngredients[i] = originalIngredient;

                // Search in both backpack and hotbar
                ItemSlot foundSlot = FindMatchingIngredient(originalIngredient, ingredient.Quantity, playerInv, hotbarInv);

                if (foundSlot == null || foundSlot.Empty)
                {
                    string ingredientName = ingredient.ResolvedItemStack?.GetName() ?? ingredient.Code?.ToString() ?? "Unknown";
                    missingIngredients.Add(ingredientName);
                }
                else
                {
                    ingredientSlots[i] = foundSlot;
                }
            }

            // If missing ingredients, fail silently (we're trying multiple recipes)
            if (missingIngredients.Count > 0) return false;

            // For fillMax, calculate even distribution per pattern character
            var quantitiesPerSlot = new int[resolvedIngredients.Length];
            if (fillMax)
            {
                // Group slots by pattern character to split evenly
                var patternGroups = new Dictionary<char, List<int>>();
                for (int i = 0; i < resolvedIngredients.Length; i++)
                {
                    if (resolvedIngredients[i] == null) continue;
                    char patternChar = (i < flatPattern.Length) ? flatPattern[i] : '_';
                    if (patternChar == '_') continue;

                    if (!patternGroups.ContainsKey(patternChar))
                        patternGroups[patternChar] = new List<int>();
                    patternGroups[patternChar].Add(i);
                }

                // Calculate quantities for each group
                foreach (var group in patternGroups)
                {
                    var positions = group.Value;
                    if (positions.Count == 0) continue;

                    // Count total available from all matching inventory slots
                    var firstOriginal = originalIngredients[positions[0]];
                    int totalAvailable = CountTotalMatchingItems(firstOriginal, playerInv, hotbarInv);
                    int maxStack = ingredientSlots[positions[0]]?.Itemstack?.Collectible?.MaxStackSize ?? 64;

                    // Calculate per-slot quantity (evenly divided)
                    int perSlot = Math.Min(totalAvailable / positions.Count, maxStack);

                    foreach (int pos in positions)
                    {
                        quantitiesPerSlot[pos] = Math.Max(perSlot, resolvedIngredients[pos].Quantity);
                    }
                }
            }

            // Clear the crafting grid first
            for (int i = 0; i < craftingGrid.Count; i++)
            {
                if (!craftingGrid[i].Empty)
                {
                    ItemStackMoveOperation clearOp = new ItemStackMoveOperation(
                        capi.World,
                        EnumMouseButton.Left,
                        0,
                        EnumMergePriority.AutoMerge,
                        craftingGrid[i].StackSize
                    );
                    clearOp.ActingPlayer = player;

                    object[] packets = player.InventoryManager.TryTransferAway(craftingGrid[i], ref clearOp, false);

                    if (packets != null)
                    {
                        foreach (var packet in packets)
                        {
                            if (packet != null) capi.Network.SendPacketClient(packet);
                        }
                    }
                }
            }

            // Fill the crafting grid with ingredients
            int ingredientIndex = 0;
            for (int row = 0; row < recipeHeight; row++)
            {
                for (int col = 0; col < recipeWidth; col++)
                {
                    if (ingredientIndex >= resolvedIngredients.Length) break;

                    var ingredient = resolvedIngredients[ingredientIndex];
                    var foundSlot = ingredientSlots[ingredientIndex];

                    int currentIndex = ingredientIndex;
                    ingredientIndex++;

                    if (ingredient == null || foundSlot == null) continue;

                    int gridSlot = row * gridWidth + col;
                    if (gridSlot >= craftingGrid.Count) continue;

                    var targetSlot = craftingGrid[gridSlot];

                    int quantity = ingredient.Quantity;
                    if (ingredient.IsTool)
                    {
                        quantity = 1;
                    }
                    else if (fillMax && quantitiesPerSlot[currentIndex] > 0)
                    {
                        quantity = quantitiesPerSlot[currentIndex];
                    }

                    ItemStackMoveOperation op = new ItemStackMoveOperation(
                        capi.World,
                        EnumMouseButton.Left,
                        0,
                        EnumMergePriority.AutoMerge,
                        quantity
                    );
                    op.ActingPlayer = player;

                    object packet = player.InventoryManager.TryTransferTo(foundSlot, targetSlot, ref op);

                    if (packet != null) capi.Network.SendPacketClient(packet);

                    // For fillMax, we just need to move at least the recipe amount
                    int minRequired = ingredient.IsTool ? 1 : ingredient.Quantity;
                    if (op.MovedQuantity < minRequired) return false;
                }
            }

            return true;
        }

        private static int CountTotalMatchingItems(CraftingRecipeIngredient ingredient, InventoryBase backpack, InventoryBase hotbar)
        {
            int total = 0;

            if (backpack != null)
            {
                foreach (var slot in backpack)
                {
                    if (slot == null || slot.Empty) continue;
                    if (DoesSlotMatchIngredient(slot, ingredient))
                    {
                        total += slot.StackSize;
                    }
                }
            }

            if (hotbar != null)
            {
                foreach (var slot in hotbar)
                {
                    if (slot == null || slot.Empty) continue;
                    if (DoesSlotMatchIngredient(slot, ingredient))
                    {
                        total += slot.StackSize;
                    }
                }
            }

            return total;
        }

        private static ItemSlot FindMatchingIngredient(CraftingRecipeIngredient ingredient, int requiredQuantity, InventoryBase backpack, InventoryBase hotbar)
        {
            if (ingredient == null) return null;

            // Search backpack
            if (backpack != null)
            {
                foreach (var slot in backpack)
                {
                    if (slot == null || slot.Empty) continue;

                    if (DoesSlotMatchIngredient(slot, ingredient) && slot.StackSize >= requiredQuantity)
                    {
                        return slot;
                    }
                }
            }

            // Search hotbar
            if (hotbar != null)
            {
                foreach (var slot in hotbar)
                {
                    if (slot == null || slot.Empty) continue;

                    if (DoesSlotMatchIngredient(slot, ingredient) && slot.StackSize >= requiredQuantity)
                    {
                        return slot;
                    }
                }
            }

            return null;
        }

        // Common variant suffixes that indicate the ingredient was expanded from a wildcard
        private static readonly string[] VariantSuffixes = new[]
        {
            // Directional
            "-north", "-south", "-east", "-west", "-up", "-down",
            "-ne", "-nw", "-se", "-sw",
            // Colors
            "-red", "-blue", "-green", "-yellow", "-orange", "-purple", "-pink", "-white", "-black", "-brown", "-gray", "-grey",
            // Wood types
            "-oak", "-maple", "-birch", "-pine", "-acacia", "-kapok", "-baldcypress", "-larch", "-redwood", "-ebony", "-walnut", "-aged",
            // Metals
            "-copper", "-bronze", "-iron", "-steel", "-gold", "-silver", "-tin", "-lead", "-zinc", "-bismuth", "-titanium",
            // Stone types
            "-granite", "-andesite", "-basalt", "-limestone", "-sandstone", "-shale", "-slate", "-chalk", "-clayite", "-bauxite",
            // Crop variants
            "-flax", "-hemp", "-cotton",
            // Generic numbered variants
            "-1", "-2", "-3", "-4", "-5", "-6", "-7", "-8", "-9",
        };

        private static bool DoesSlotMatchIngredient(ItemSlot slot, CraftingRecipeIngredient ingredient)
        {
            if (slot?.Itemstack == null || ingredient == null) return false;

            // First try the game's built-in matching logic
            bool matches = ingredient.SatisfiesAsIngredient(slot.Itemstack, checkStackSize: false);

            // If no match, try wildcard and variant matching
            if (!matches)
            {
                bool typeMatches = (ingredient.Type == EnumItemClass.Block && slot.Itemstack.Class == EnumItemClass.Block) ||
                                   (ingredient.Type == EnumItemClass.Item && slot.Itemstack.Class == EnumItemClass.Item);

                if (typeMatches)
                {
                    if (ingredient.Code != null && ingredient.Code.Path.Contains('*'))
                    {
                        matches = WildcardUtil.Match(ingredient.Code, slot.Itemstack.Collectible.Code, ingredient.AllowedVariants);
                    }
                    else if (ingredient.IsWildCard)
                    {
                        matches = TryWildcardBaseMatch(slot, ingredient);
                    }
                    else
                    {
                        matches = TryVariantSuffixMatch(slot, ingredient);
                    }
                }
            }

            return matches;
        }

        private static bool TryWildcardBaseMatch(ItemSlot slot, CraftingRecipeIngredient ingredient)
        {
            string ingredientPath = ingredient.Code.Path;
            int lastDash = ingredientPath.LastIndexOf('-');
            if (lastDash > 0)
            {
                string basePath = ingredientPath.Substring(0, lastDash) + "-*";
                var wildcardCode = new AssetLocation(ingredient.Code.Domain, basePath);
                if (WildcardUtil.Match(wildcardCode, slot.Itemstack.Collectible.Code, ingredient.AllowedVariants))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool TryVariantSuffixMatch(ItemSlot slot, CraftingRecipeIngredient ingredient)
        {
            string ingredientPath = ingredient.Code.Path;
            string slotPath = slot.Itemstack.Collectible.Code.Path;

            // Check if ingredient code ends with a known variant suffix
            foreach (var suffix in VariantSuffixes)
            {
                if (ingredientPath.EndsWith(suffix))
                {
                    string basePath = ingredientPath.Substring(0, ingredientPath.Length - suffix.Length);

                    if (slotPath.StartsWith(basePath))
                    {
                        string slotRemainder = slotPath.Substring(basePath.Length);
                        if (slotRemainder.Length > 0 && slotRemainder.StartsWith("-"))
                        {
                            return true;
                        }
                    }
                }
            }

            // Fallback: strip last dash-segment from both and compare
            int ingredientLastDash = ingredientPath.LastIndexOf('-');
            int slotLastDash = slotPath.LastIndexOf('-');

            if (ingredientLastDash > 0 && slotLastDash > 0)
            {
                string ingredientBase = ingredientPath.Substring(0, ingredientLastDash);
                string slotBase = slotPath.Substring(0, slotLastDash);

                if (ingredientBase == slotBase && ingredient.Code.Domain == slot.Itemstack.Collectible.Code.Domain)
                {
                    return true;
                }
            }

            return false;
        }

        private class IngredientInfo
        {
            public string SlotKey { get; set; }
            public CraftingRecipeIngredient Ingredient { get; set; }
            public ItemSlot FoundSlot { get; set; }
        }
    }
}
