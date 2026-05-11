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

        private static bool postfixDumped = false;

        public static void InitDetailGuiPostfix(object __instance)
        {
            if (__instance == null) { capi?.Logger.Notification("[RecipeExplorer/Postfix] __instance null"); return; }

            try
            {
                var handbookType = __instance.GetType();

                if (BrowseHistoryField == null)
                {
                    BrowseHistoryField = AccessTools.Field(handbookType, "browseHistory");
                }

                var detailGuiField = AccessTools.Field(handbookType, "detailViewGui");
                var detailViewGui = detailGuiField?.GetValue(__instance) as GuiComposer;
                if (detailViewGui == null) { if (!postfixDumped) capi.Logger.Notification("[RecipeExplorer/Postfix] detailViewGui null (field={0})", detailGuiField?.Name ?? "missing"); return; }

                var browseHistory = BrowseHistoryField?.GetValue(__instance);
                if (browseHistory == null) { if (!postfixDumped) capi.Logger.Notification("[RecipeExplorer/Postfix] browseHistory null"); return; }

                int count = (int)(browseHistory.GetType().GetProperty("Count")?.GetValue(browseHistory) ?? 0);
                if (count == 0) { if (!postfixDumped) capi.Logger.Notification("[RecipeExplorer/Postfix] browseHistory count=0"); return; }

                var peekMethod = browseHistory.GetType().GetMethod("Peek");
                var currentElement = peekMethod?.Invoke(browseHistory, null);
                if (currentElement == null) { if (!postfixDumped) capi.Logger.Notification("[RecipeExplorer/Postfix] Peek returned null"); return; }

                if (!postfixDumped)
                {
                    postfixDumped = true;
                    var et = currentElement.GetType();
                    var bf = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
                    var sb = new System.Text.StringBuilder("[RecipeExplorer/Postfix] BrowseHistory element type=" + et.FullName + " FIELDS:");
                    foreach (var f in et.GetFields(bf)) sb.Append(' ').Append(f.Name).Append('=').Append(f.FieldType.Name);
                    sb.Append(" PROPS:");
                    foreach (var p in et.GetProperties(bf)) sb.Append(' ').Append(p.Name).Append('=').Append(p.PropertyType.Name);
                    capi.Logger.Notification(sb.ToString());
                }

                var pageField = AccessTools.Field(currentElement.GetType(), "Page")
                             ?? AccessTools.Field(currentElement.GetType(), "page");
                var pageProp = currentElement.GetType().GetProperty("Page");
                object currentPage = pageField?.GetValue(currentElement) ?? pageProp?.GetValue(currentElement);
                if (currentPage == null) { capi.Logger.Notification("[RecipeExplorer/Postfix] Page null (had pageField={0} pageProp={1})", pageField != null, pageProp != null); return; }

                if (postfixDumped)
                {
                    var pt = currentPage.GetType();
                    var bf2 = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
                    var sb2 = new System.Text.StringBuilder("[RecipeExplorer/Postfix] Page type=" + pt.FullName + " FIELDS:");
                    foreach (var f in pt.GetFields(bf2)) sb2.Append(' ').Append(f.Name).Append('=').Append(f.FieldType.Name);
                    sb2.Append(" PROPS:");
                    foreach (var p in pt.GetProperties(bf2)) sb2.Append(' ').Append(p.Name).Append('=').Append(p.PropertyType.Name);
                    capi.Logger.Notification(sb2.ToString());
                    postfixDumped = false; // dump once per page-type
                }

                var stackField = AccessTools.Field(currentPage.GetType(), "Stack")
                              ?? AccessTools.Field(currentPage.GetType(), "stack")
                              ?? AccessTools.Field(currentPage.GetType(), "Itemstack");
                var stackProp = currentPage.GetType().GetProperty("Stack")
                             ?? currentPage.GetType().GetProperty("Itemstack");
                ItemStack stack = (stackField?.GetValue(currentPage) ?? stackProp?.GetValue(currentPage)) as ItemStack;
                if (stack == null) { capi.Logger.Notification("[RecipeExplorer/Postfix] stack null (page type={0})", currentPage.GetType().Name); return; }

                AddAutoCraftButton(__instance, detailViewGui, stack);
            }
            catch (Exception ex)
            {
                capi.Logger.Error("[RecipeExplorer] Failed in InitDetailGuiPostfix: {0}", ex);
            }
        }

        private static void AddAutoCraftButton(object dialog, GuiComposer detailViewGui, ItemStack stack)
        {
            if (detailViewGui.GetButton("autocraft-button") != null)
            {
                capi.Logger.Notification("[RecipeExplorer/Postfix] Button already present for {0}", stack.Collectible?.Code);
                return;
            }

            var recipes = FindCraftingRecipes(stack);
            capi.Logger.Notification("[RecipeExplorer/Postfix] Page item={0} matchedRecipes={1}", stack.Collectible?.Code, recipes.Count);
            if (recipes.Count == 0) return;
            capi.Logger.Notification("[RecipeExplorer/Postfix] Adding Auto-Fill button");

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

            var verify = detailViewGui.GetButton("autocraft-button");
            var b = button.Bounds;
            capi.Logger.Notification("[RecipeExplorer/Postfix] Button post-add verify={0} bounds X={1} Y={2} W={3} H={4} renderX={5} renderY={6}",
                verify != null, b.fixedX, b.fixedY, b.fixedWidth, b.fixedHeight, b.renderX, b.renderY);
        }

        private static List<GridRecipe> FindCraftingRecipes(ItemStack output)
        {
            var recipes = new List<GridRecipe>();
            int total = 0, nullOutput = 0, nullResolved = 0;

            foreach (var recipe in capi.World.GridRecipes)
            {
                total++;
                if (recipe?.Output == null) { nullOutput++; continue; }
                if (recipe.Output.ResolvedItemStack == null) { nullResolved++; continue; }

                if (recipe.Output.ResolvedItemStack.Collectible.Code.Equals(output.Collectible.Code))
                {
                    recipes.Add(recipe);
                }
            }

            capi.Logger.Notification("[RecipeExplorer/Find] target={0} totalRecipes={1} nullOutput={2} nullResolved={3} matched={4}",
                output.Collectible?.Code, total, nullOutput, nullResolved, recipes.Count);
            return recipes;
        }

        private static bool OnAutoCraftClicked(List<GridRecipe> recipes)
        {
            capi.Logger.Notification("[RecipeExplorer/Click] Auto-Fill clicked, recipes={0}", recipes.Count);
            if (recipes.Count == 0)
            {
                if (RecipeExplorerMod.Config.ShowAutoFillMessages)
                    capi.ShowChatMessage("No recipes found");
                return true;
            }

            // Find the player's crafting grid
            var craftingGrid = FindPlayerCraftingGrid();
            capi.Logger.Notification("[RecipeExplorer/Click] craftingGrid={0} className={1} count={2}",
                craftingGrid != null, craftingGrid?.ClassName, craftingGrid?.Count);
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
            if (craftingInv != null) { capi.Logger.Notification("[RecipeExplorer/Click] found craftinggrid count={0}", craftingInv.Count); return craftingInv; }

            // Try other known crafting grid inventory ids
            foreach (var id in new[] { "character", "crafting", "creativeInventory" })
            {
                var inv = player.InventoryManager.GetOwnInventory(id) as InventoryBase;
                if (inv != null) capi.Logger.Notification("[RecipeExplorer/Click] candidate id={0} count={1}", id, inv.Count);
            }

            // Dump all opened inventories to find the right id in 1.22
            var sb = new System.Text.StringBuilder("[RecipeExplorer/Click] opened inventories:");
            foreach (var inv in player.InventoryManager.OpenedInventories)
            {
                sb.Append(' ').Append(inv.InventoryID).Append("(class=").Append(inv.ClassName).Append(",n=").Append(inv.Count).Append(')');
            }
            capi.Logger.Notification(sb.ToString());

            var charInv = player.InventoryManager.GetOwnInventory("character") as InventoryBase;
            return charInv;
        }

        private static bool TryFillCraftingGrid(GridRecipe recipe, InventoryBase craftingGrid, bool fillMax = false)
        {
            if (recipe == null || craftingGrid == null) return false;

            // VS 1.22: use the public ResolvedIngredients property (the old private field is gone).
            CraftingRecipeIngredient[] resolvedIngredients = recipe.ResolvedIngredients;
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

            capi.Logger.Notification("[RecipeExplorer/AutoFill] Trying recipe: output={0} ingCount={1} W={2} H={3}",
                recipe.Output?.ResolvedItemStack?.Collectible?.Code, resolvedIngredients.Length, recipeWidth, recipeHeight);
            for (int dbgI = 0; dbgI < resolvedIngredients.Length; dbgI++)
            {
                var ing = resolvedIngredients[dbgI];
                if (ing == null) continue;
                capi.Logger.Notification("[RecipeExplorer/AutoFill]   ing[{0}] Code={1} ResolvedStack={2} IsWildCard={3} AllowedVariants={4} Quantity={5} IsTool={6}",
                    dbgI, ing.Code, ing.ResolvedItemStack?.Collectible?.Code,
                    ing.IsWildCard, ing.AllowedVariants?.Length ?? 0, ing.Quantity, ing.IsTool);
            }

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
                    capi.Logger.Notification("[RecipeExplorer/AutoFill]   MISS for ing[{0}] Code={1} (looking for qty>={2})", i, ingredient.Code, ingredient.Quantity);
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

                    var firstOriginal = originalIngredients[positions[0]];
                    int totalAvailable = CountTotalMatchingItems(firstOriginal, playerInv, hotbarInv);
                    int maxStack = ingredientSlots[positions[0]]?.Itemstack?.Collectible?.MaxStackSize ?? 64;

                    // Cap by per-slot stack size first, then distribute total evenly with remainder spread to first N slots.
                    int budget = Math.Min(totalAvailable, maxStack * positions.Count);
                    int basePerSlot = budget / positions.Count;
                    int remainder = budget - (basePerSlot * positions.Count);

                    for (int p = 0; p < positions.Count; p++)
                    {
                        int extra = p < remainder ? 1 : 0;
                        int q = Math.Min(maxStack, basePerSlot + extra);
                        quantitiesPerSlot[positions[p]] = Math.Max(q, resolvedIngredients[positions[p]].Quantity);
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

                    int totalMoved = TransferFromAnySource(originalIngredients[currentIndex], targetSlot, quantity, player, playerInv, hotbarInv);

                    int minRequired = ingredient.IsTool ? 1 : ingredient.Quantity;
                    if (totalMoved < minRequired) return false;
                }
            }

            return true;
        }

        private static int TransferFromAnySource(CraftingRecipeIngredient ingredient, ItemSlot targetSlot, int desired, IPlayer player, InventoryBase backpack, InventoryBase hotbar)
        {
            if (desired <= 0) return 0;

            int moved = 0;
            foreach (var inv in new[] { backpack, hotbar })
            {
                if (inv == null) continue;
                foreach (var slot in inv)
                {
                    if (moved >= desired) break;
                    if (slot == null || slot.Empty) continue;
                    if (!DoesSlotMatchIngredient(slot, ingredient)) continue;

                    int want = desired - moved;
                    var op = new ItemStackMoveOperation(capi.World, EnumMouseButton.Left, 0, EnumMergePriority.AutoMerge, want);
                    op.ActingPlayer = player;

                    object packet = player.InventoryManager.TryTransferTo(slot, targetSlot, ref op);
                    if (packet != null) capi.Network.SendPacketClient(packet);

                    if (op.MovedQuantity > 0)
                    {
                        moved += op.MovedQuantity;
                    }
                    else
                    {
                        // target full or incompatible; no point continuing
                        break;
                    }
                }
                if (moved >= desired) break;
            }
            return moved;
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
