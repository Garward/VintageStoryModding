using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
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
        private static FieldInfo InteractiveElementsField = null;

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
                    capi.Logger.Error("[BetterHandbook/RecipeExplorer] Could not find GuiDialogHandbook type");
                    return;
                }

                // Patch the handbook detail GUI to add our auto-craft button
                var initDetailGuiMethod = AccessTools.Method(handbookType, "initDetailGui");
                if (initDetailGuiMethod == null)
                {
                    capi.Logger.Error("[BetterHandbook/RecipeExplorer] Could not find initDetailGui method");
                    return;
                }

                harmony.Patch(
                    initDetailGuiMethod,
                    postfix: new HarmonyMethod(typeof(AutoCraftSystem), nameof(InitDetailGuiPostfix))
                );

                BetterHandbookLog.Info(capi, "[BetterHandbook/RecipeExplorer] Auto-craft system initialized successfully");
            }
            catch (Exception ex)
            {
                capi.Logger.Error("[BetterHandbook/RecipeExplorer] Failed to initialize auto-craft system: {0}", ex);
            }
        }

        private static bool postfixDumped = false;

        public static void InitDetailGuiPostfix(object __instance)
        {
            if (__instance == null) { BetterHandbookLog.Diagnostic(capi, "[BetterHandbook/RecipeExplorer/Postfix] __instance null"); return; }

            try
            {
                var handbookType = __instance.GetType();

                if (BrowseHistoryField == null)
                {
                    BrowseHistoryField = AccessTools.Field(handbookType, "browseHistory");
                }

                var detailGuiField = AccessTools.Field(handbookType, "detailViewGui");
                var detailViewGui = detailGuiField?.GetValue(__instance) as GuiComposer;
                if (detailViewGui == null) { if (!postfixDumped) BetterHandbookLog.Diagnostic(capi, "[BetterHandbook/RecipeExplorer/Postfix] detailViewGui null (field={0})", detailGuiField?.Name ?? "missing"); return; }

                var browseHistory = BrowseHistoryField?.GetValue(__instance);
                if (browseHistory == null) { if (!postfixDumped) BetterHandbookLog.Diagnostic(capi, "[BetterHandbook/RecipeExplorer/Postfix] browseHistory null"); return; }

                int count = (int)(browseHistory.GetType().GetProperty("Count")?.GetValue(browseHistory) ?? 0);
                if (count == 0) { if (!postfixDumped) BetterHandbookLog.Diagnostic(capi, "[BetterHandbook/RecipeExplorer/Postfix] browseHistory count=0"); return; }

                var peekMethod = browseHistory.GetType().GetMethod("Peek");
                var currentElement = peekMethod?.Invoke(browseHistory, null);
                if (currentElement == null) { if (!postfixDumped) BetterHandbookLog.Diagnostic(capi, "[BetterHandbook/RecipeExplorer/Postfix] Peek returned null"); return; }

                if (!postfixDumped)
                {
                    postfixDumped = true;
                    var et = currentElement.GetType();
                    var bf = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
                    var sb = new System.Text.StringBuilder("[BetterHandbook/RecipeExplorer/Postfix] BrowseHistory element type=" + et.FullName + " FIELDS:");
                    foreach (var f in et.GetFields(bf)) sb.Append(' ').Append(f.Name).Append('=').Append(f.FieldType.Name);
                    sb.Append(" PROPS:");
                    foreach (var p in et.GetProperties(bf)) sb.Append(' ').Append(p.Name).Append('=').Append(p.PropertyType.Name);
                    BetterHandbookLog.Diagnostic(capi, sb.ToString());
                }

                var pageField = AccessTools.Field(currentElement.GetType(), "Page")
                             ?? AccessTools.Field(currentElement.GetType(), "page");
                var pageProp = currentElement.GetType().GetProperty("Page");
                object currentPage = pageField?.GetValue(currentElement) ?? pageProp?.GetValue(currentElement);
                if (currentPage == null) { BetterHandbookLog.Diagnostic(capi, "[BetterHandbook/RecipeExplorer/Postfix] Page null (had pageField={0} pageProp={1})", pageField != null, pageProp != null); return; }

                if (postfixDumped)
                {
                    var pt = currentPage.GetType();
                    var bf2 = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
                    var sb2 = new System.Text.StringBuilder("[BetterHandbook/RecipeExplorer/Postfix] Page type=" + pt.FullName + " FIELDS:");
                    foreach (var f in pt.GetFields(bf2)) sb2.Append(' ').Append(f.Name).Append('=').Append(f.FieldType.Name);
                    sb2.Append(" PROPS:");
                    foreach (var p in pt.GetProperties(bf2)) sb2.Append(' ').Append(p.Name).Append('=').Append(p.PropertyType.Name);
                    BetterHandbookLog.Diagnostic(capi, sb2.ToString());
                    postfixDumped = false; // dump once per page-type
                }

                var stackField = AccessTools.Field(currentPage.GetType(), "Stack")
                              ?? AccessTools.Field(currentPage.GetType(), "stack")
                              ?? AccessTools.Field(currentPage.GetType(), "Itemstack");
                var stackProp = currentPage.GetType().GetProperty("Stack")
                             ?? currentPage.GetType().GetProperty("Itemstack");
                ItemStack stack = (stackField?.GetValue(currentPage) ?? stackProp?.GetValue(currentPage)) as ItemStack;
                if (stack == null) { BetterHandbookLog.Diagnostic(capi, "[BetterHandbook/RecipeExplorer/Postfix] stack null (page type={0})", currentPage.GetType().Name); return; }

                AddAutoCraftButton(__instance, detailViewGui, stack);
            }
            catch (Exception ex)
            {
                capi.Logger.Error("[BetterHandbook/RecipeExplorer] Failed in InitDetailGuiPostfix: {0}", ex);
            }
        }

        private static void AddAutoCraftButton(object dialog, GuiComposer detailViewGui, ItemStack stack)
        {
            bool hasAutoCraftButton = detailViewGui.GetButton("autocraft-button") != null;
            bool hasUsesButton = detailViewGui.GetButton("recipeuses-button") != null;
            if (hasAutoCraftButton && hasUsesButton)
            {
                BetterHandbookLog.Diagnostic(capi, "[BetterHandbook/RecipeExplorer/Postfix] Buttons already present for {0}", stack.Collectible?.Code);
                return;
            }

            var recipes = FindCraftingRecipes(stack);
            BetterHandbookLog.Diagnostic(capi, "[BetterHandbook/RecipeExplorer/Postfix] Page item={0} matchedRecipes={1}", stack.Collectible?.Code, recipes.Count);

            if (!hasAutoCraftButton && recipes.Count > 0)
            {
                BetterHandbookLog.Diagnostic(capi, "[BetterHandbook/RecipeExplorer/Postfix] Adding Auto-Fill button");
                AddAutoFillFooterButton(
                    detailViewGui,
                    () => OnAutoCraftClicked(recipes));
            }

            if (!hasUsesButton)
            {
                AddUsesFooterButton(
                    detailViewGui,
                    () => RecipeExplorerMod.ShowUsesForStack(stack));
            }

            detailViewGui.ReCompose();

            var verify = detailViewGui.GetButton("autocraft-button");
            var usesVerify = detailViewGui.GetButton("recipeuses-button");
            BetterHandbookLog.Diagnostic(capi, "[BetterHandbook/RecipeExplorer/Postfix] Button post-add auto={0} uses={1}",
                verify != null, usesVerify != null);
        }

        private static void AddAutoFillFooterButton(GuiComposer detailViewGui, ActionConsumable onClick)
        {
            var footerButtons = FindVanillaFooterButtons(detailViewGui);
            GuiElementTextButton backButton = footerButtons.Count > 0 ? footerButtons[0] : FindButtonByText(detailViewGui, Lang.Get("general-back"));
            GuiElementTextButton overviewButton = footerButtons.Count > 1 ? footerButtons[1] : FindButtonByText(detailViewGui, Lang.Get("handbook-overview"));

            GuiElementTextButton button = CreateFooterButton("Auto-Fill", onClick);
            PositionBetweenButtons(detailViewGui, button, backButton, overviewButton, -105, "autocraft-button");
            ElementBounds hoverBounds = button.Bounds.FlatCopy();
            detailViewGui.AddInteractiveElement(button, "autocraft-button");
            Vintagestory.API.Client.GuiComposerHelpers.AddHoverText(
                detailViewGui,
                Lang.Get("betterhandbook:autofilltip"),
                CairoFont.WhiteSmallText(),
                260,
                hoverBounds,
                "autocraft-button-hover");
        }

        private static void AddUsesFooterButton(GuiComposer detailViewGui, ActionConsumable onClick)
        {
            var footerButtons = FindVanillaFooterButtons(detailViewGui);
            GuiElementTextButton overviewButton = footerButtons.Count > 1 ? footerButtons[1] : FindButtonByText(detailViewGui, Lang.Get("handbook-overview"));
            GuiElementTextButton closeButton = footerButtons.Count > 2 ? footerButtons[2] : FindButtonByText(detailViewGui, Lang.Get("general-close"));

            GuiElementTextButton button = CreateFooterButton("Uses", onClick);
            PositionBetweenButtons(detailViewGui, button, overviewButton, closeButton, 105, "recipeuses-button");
            detailViewGui.AddInteractiveElement(button, "recipeuses-button");
        }

        private static GuiElementTextButton CreateFooterButton(string text, ActionConsumable onClick)
        {
            ElementBounds buttonBounds = ElementBounds
                .FixedSize(0, 0)
                .WithAlignment(EnumDialogArea.None)
                .WithFixedPadding(20, 4);

            var button = new GuiElementTextButton(
                capi,
                text,
                CairoFont.SmallButtonText(EnumButtonStyle.Normal),
                CairoFont.SmallButtonText(EnumButtonStyle.Normal),
                onClick,
                buttonBounds,
                EnumButtonStyle.Normal
            );

            button.SetOrientation(CairoFont.SmallButtonText(EnumButtonStyle.Normal).Orientation);
            button.BeforeCalcBounds();
            return button;
        }

        private static void PositionBetweenButtons(GuiComposer detailViewGui, GuiElementTextButton button, GuiElementTextButton leftButton, GuiElementTextButton rightButton, double centerFallbackOffset, string key)
        {
            if (leftButton != null && rightButton != null)
            {
                double scale = RuntimeEnv.GUIScale;
                double buttonWidth = GetOuterWidth(button.Bounds) * scale;
                double desiredLeft = (leftButton.Bounds.absX + leftButton.Bounds.OuterWidth + rightButton.Bounds.absX - buttonWidth) / 2;
                SetAbsolutePosition(detailViewGui, button.Bounds, desiredLeft, rightButton.Bounds.absY);
                return;
            }

            capi.Logger.Debug("[BetterHandbook/RecipeExplorer] Could not find vanilla footer buttons while positioning {0}; using center fallback", key);
            PositionFromCenter(detailViewGui, button, centerFallbackOffset);
        }

        private static void PositionFromCenter(GuiComposer detailViewGui, GuiElementTextButton button, double offset)
        {
            GuiElementTextButton backButton = detailViewGui.GetButton("backButton");
            double y = backButton?.Bounds.absY ?? (detailViewGui.Bounds.absY + detailViewGui.Bounds.OuterHeight - 48 * RuntimeEnv.GUIScale);
            double width = GetOuterWidth(button.Bounds) * RuntimeEnv.GUIScale;
            double desiredLeft = detailViewGui.Bounds.absX + detailViewGui.Bounds.OuterWidth / 2 - width / 2 + offset * RuntimeEnv.GUIScale;
            SetAbsolutePosition(detailViewGui, button.Bounds, desiredLeft, y);
        }

        private static void SetAbsolutePosition(GuiComposer detailViewGui, ElementBounds bounds, double absX, double absY)
        {
            double scale = RuntimeEnv.GUIScale;
            ElementBounds parent = detailViewGui.Bounds;
            bounds.Alignment = EnumDialogArea.None;
            bounds.fixedOffsetX = 0;
            bounds.fixedOffsetY = 0;
            bounds.fixedX = (absX - parent.absX - parent.absPaddingX) / scale;
            bounds.fixedY = (absY - parent.absY - parent.absPaddingY) / scale;
        }

        private static double GetOuterWidth(ElementBounds bounds)
        {
            return bounds.fixedWidth + 2 * bounds.fixedPaddingX;
        }

        private static GuiElementTextButton FindButtonByText(GuiComposer composer, string text)
        {
            if (composer == null || string.IsNullOrEmpty(text))
            {
                return null;
            }

            InteractiveElementsField ??= AccessTools.Field(typeof(GuiComposer), "interactiveElements");
            if (InteractiveElementsField?.GetValue(composer) is not Dictionary<string, GuiElement> elements)
            {
                return null;
            }

            foreach (GuiElement element in elements.Values)
            {
                if (element is GuiElementTextButton button && button.Text == text)
                {
                    return button;
                }
            }

            return null;
        }

        private static List<GuiElementTextButton> FindVanillaFooterButtons(GuiComposer composer)
        {
            var footerButtons = new List<GuiElementTextButton>();
            if (composer == null)
            {
                return footerButtons;
            }

            InteractiveElementsField ??= AccessTools.Field(typeof(GuiComposer), "interactiveElements");
            if (InteractiveElementsField?.GetValue(composer) is not Dictionary<string, GuiElement> elements)
            {
                return footerButtons;
            }

            double footerThreshold = composer.Bounds.absY + composer.Bounds.OuterHeight - 90 * RuntimeEnv.GUIScale;
            foreach (var entry in elements)
            {
                if (entry.Key == "autocraft-button" || entry.Key == "recipeuses-button")
                {
                    continue;
                }

                if (entry.Value is GuiElementTextButton button && button.Bounds.absY >= footerThreshold)
                {
                    footerButtons.Add(button);
                }
            }

            return footerButtons
                .OrderBy(button => button.Bounds.absX)
                .Take(3)
                .ToList();
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

            BetterHandbookLog.Diagnostic(capi, "[BetterHandbook/RecipeExplorer/Find] target={0} totalRecipes={1} nullOutput={2} nullResolved={3} matched={4}",
                output.Collectible?.Code, total, nullOutput, nullResolved, recipes.Count);
            return recipes;
        }

        private static bool OnAutoCraftClicked(List<GridRecipe> recipes)
        {
            BetterHandbookLog.Diagnostic(capi, "[BetterHandbook/RecipeExplorer/Click] Auto-Fill clicked, recipes={0}", recipes.Count);
            if (recipes.Count == 0)
            {
                if (RecipeExplorerMod.Config.ShowAutoFillMessages)
                    capi.ShowChatMessage("No recipes found");
                return true;
            }

            // Find the player's crafting grid
            var craftingGrid = FindPlayerCraftingGrid();
            BetterHandbookLog.Diagnostic(capi, "[BetterHandbook/RecipeExplorer/Click] craftingGrid={0} className={1} count={2}",
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
                HandbookRecipeOverlays.ClearFailedFill();
                if (RecipeExplorerMod.Config.ShowAutoFillMessages)
                    capi.ShowChatMessage(shiftHeld ? "Crafting grid filled (max)!" : "Crafting grid filled!");
                capi.Gui.PlaySound("menubutton_press");
            }
            else
            {
                HandbookRecipeOverlays.ReportFailedFill(recipes);
                if (RecipeExplorerMod.Config.ShowAutoFillMessages)
                    capi.ShowChatMessage("Missing ingredients or crafting grid is too small");
            }

            return true;
        }

        internal static InventoryBase FindPlayerCraftingGrid()
        {
            var player = capi.World.Player;
            if (player?.InventoryManager == null) return null;

            var craftingInv = player.InventoryManager.GetOwnInventory("craftinggrid") as InventoryBase;
            if (craftingInv != null) { BetterHandbookLog.Diagnostic(capi, "[BetterHandbook/RecipeExplorer/Click] found craftinggrid count={0}", craftingInv.Count); return craftingInv; }

            // Try other known crafting grid inventory ids
            foreach (var id in new[] { "character", "crafting", "creativeInventory" })
            {
                var inv = player.InventoryManager.GetOwnInventory(id) as InventoryBase;
                if (inv != null) BetterHandbookLog.Diagnostic(capi, "[BetterHandbook/RecipeExplorer/Click] candidate id={0} count={1}", id, inv.Count);
            }

            // Dump all opened inventories to find the right id in 1.22
            var sb = new System.Text.StringBuilder("[BetterHandbook/RecipeExplorer/Click] opened inventories:");
            foreach (var inv in player.InventoryManager.OpenedInventories)
            {
                sb.Append(' ').Append(inv.InventoryID).Append("(class=").Append(inv.ClassName).Append(",n=").Append(inv.Count).Append(')');
            }
            BetterHandbookLog.Diagnostic(capi, sb.ToString());

            var charInv = player.InventoryManager.GetOwnInventory("character") as InventoryBase;
            return charInv;
        }

        internal static bool TryFillCraftingGrid(GridRecipe recipe, InventoryBase craftingGrid, bool fillMax = false)
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
            int craftingInputSlotCount = gridWidth * gridHeight;

            if (recipeWidth > gridWidth || recipeHeight > gridHeight) return false;

            // Get player inventory
            var player = capi.World.Player;
            if (player == null) return false;

            var playerInv = player.InventoryManager?.GetOwnInventory("backpack") as InventoryBase;
            var hotbarInv = player.InventoryManager?.GetOwnInventory("hotbar") as InventoryBase;

            if (playerInv == null && hotbarInv == null) return false;

            BetterHandbookLog.Diagnostic(capi, "[BetterHandbook/RecipeExplorer/AutoFill] Trying recipe: output={0} ingCount={1} W={2} H={3}",
                recipe.Output?.ResolvedItemStack?.Collectible?.Code, resolvedIngredients.Length, recipeWidth, recipeHeight);
            TraceAutoFill("BEGIN fillMax={0} output={1} ingredientCount={2} recipe={3}x{4} gridCount={5} gridWidth={6} inputSlots={7}",
                fillMax,
                recipe.Output?.ResolvedItemStack?.Collectible?.Code,
                resolvedIngredients.Length,
                recipeWidth,
                recipeHeight,
                craftingGrid.Count,
                gridWidth,
                craftingInputSlotCount);
            if (fillMax)
            {
                TraceGrid("initial-grid", craftingGrid, craftingInputSlotCount);
            }

            for (int dbgI = 0; dbgI < resolvedIngredients.Length; dbgI++)
            {
                var ing = resolvedIngredients[dbgI];
                if (ing == null) continue;
                BetterHandbookLog.Diagnostic(capi, "[BetterHandbook/RecipeExplorer/AutoFill]   ing[{0}] Code={1} ResolvedStack={2} MatchingType={3} AllowedVariants={4} Quantity={5} IsTool={6}",
                    dbgI, ing.Code, ing.ResolvedItemStack?.Collectible?.Code,
                    ing.MatchingType, ing.AllowedVariants?.Length ?? 0, ing.Quantity, ing.IsTool);
            }

            // Build ingredient info list
            var originalIngredients = new CraftingRecipeIngredient[resolvedIngredients.Length];

            string pattern = recipe.IngredientPattern;
            string flatPattern = pattern?
                .Replace(",", "")
                .Replace("\t", "")
                .Replace("\r", "")
                .Replace("\n", "") ?? "";

            for (int i = 0; i < resolvedIngredients.Length; i++)
            {
                var ingredient = resolvedIngredients[i];

                if (ingredient == null)
                {
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
            }

            // For fillMax, calculate even distribution per pattern character
            var quantitiesPerSlot = new int[resolvedIngredients.Length];
            if (fillMax)
            {
                // Group by actual ingredient, not pattern character. Some recipes use
                // multiple pattern letters for the same stack, and each group must share
                // one availability pool.
                var ingredientGroups = new Dictionary<string, List<int>>();
                for (int i = 0; i < resolvedIngredients.Length; i++)
                {
                    if (resolvedIngredients[i] == null) continue;

                    string ingredientKey = GetIngredientGroupKey(originalIngredients[i]);
                    if (!ingredientGroups.ContainsKey(ingredientKey))
                        ingredientGroups[ingredientKey] = new List<int>();
                    ingredientGroups[ingredientKey].Add(i);
                }

                // Calculate quantities for each group
                foreach (var group in ingredientGroups)
                {
                    var positions = group.Value;
                    if (positions.Count == 0) continue;

                    var firstOriginal = originalIngredients[positions[0]];
                    int totalAvailable = CountTotalMatchingItems(firstOriginal, playerInv, hotbarInv, craftingGrid, craftingInputSlotCount);
                    int maxStack = MaxStackSizeForIngredient(firstOriginal, playerInv, hotbarInv, craftingGrid, craftingInputSlotCount);

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

                    TraceAutoFill("GROUP ingredient={0} positions=[{1}] total={2} maxStack={3} budget={4} base={5} remainder={6} targets=[{7}]",
                        DescribeIngredient(firstOriginal),
                        string.Join(",", positions),
                        totalAvailable,
                        maxStack,
                        budget,
                        basePerSlot,
                        remainder,
                        string.Join(",", positions.Select(pos => pos + ":" + quantitiesPerSlot[pos])));
                    TraceMatchingSources("GROUP-SOURCES before-clear", firstOriginal, playerInv, hotbarInv, craftingGrid, craftingInputSlotCount);
                }
            }

            // Normal fill preserves matching target slots. Shift-fill is stricter: clear the
            // input grid first, then refill exact target quantities for deterministic spread.
            bool[] keepCraftingSlot = new bool[craftingGrid.Count];
            if (!fillMax)
            {
                int preserveIndex = 0;
                for (int row = 0; row < recipeHeight; row++)
                {
                    for (int col = 0; col < recipeWidth; col++)
                    {
                        if (preserveIndex >= resolvedIngredients.Length) break;

                        var ingredient = resolvedIngredients[preserveIndex];
                        int currentIndex = preserveIndex;
                        preserveIndex++;

                        if (ingredient == null) continue;

                        int gridSlot = row * gridWidth + col;
                        if (gridSlot >= craftingInputSlotCount) continue;

                        if (DoesSlotMatchIngredient(craftingGrid[gridSlot], originalIngredients[currentIndex]))
                        {
                            keepCraftingSlot[gridSlot] = true;
                        }
                    }
                }
            }

            for (int i = 0; i < craftingInputSlotCount; i++)
            {
                if (!keepCraftingSlot[i] && !craftingGrid[i].Empty)
                {
                    TraceAutoFill("CLEAR slot={0} before={1}", i, DescribeSlot(craftingGrid[i]));
                    int stackSize = craftingGrid[i].StackSize;
                    int moved = TransferAwayToInventory(craftingGrid[i], stackSize, player, playerInv, hotbarInv);
                    TraceAutoFill("CLEAR slot={0} requested={1} moved={2} after={3}", i, stackSize, moved, DescribeSlot(craftingGrid[i]));
                    if (moved < stackSize || !craftingGrid[i].Empty) return false;
                }
            }
            if (fillMax)
            {
                TraceGrid("after-clear-grid", craftingGrid, craftingInputSlotCount);
            }

            // Fill the crafting grid with ingredients
            int ingredientIndex = 0;
            for (int row = 0; row < recipeHeight; row++)
            {
                for (int col = 0; col < recipeWidth; col++)
                {
                    if (ingredientIndex >= resolvedIngredients.Length) break;

                    var ingredient = resolvedIngredients[ingredientIndex];

                    int currentIndex = ingredientIndex;
                    ingredientIndex++;

                    if (ingredient == null) continue;

                    int gridSlot = row * gridWidth + col;
                    if (gridSlot >= craftingInputSlotCount) continue;

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

                    int alreadyPresent = fillMax ? 0 : CountMatchingItemsInSlot(targetSlot, originalIngredients[currentIndex]);
                    int remaining = Math.Max(0, quantity - alreadyPresent);
                    TraceAutoFill("TARGET idx={0} gridSlot={1} ingredient={2} targetQty={3} already={4} remaining={5} before={6}",
                        currentIndex,
                        gridSlot,
                        DescribeIngredient(originalIngredients[currentIndex]),
                        quantity,
                        alreadyPresent,
                        remaining,
                        DescribeSlot(targetSlot));
                    int totalAvailable = alreadyPresent + TransferFromAnySource(originalIngredients[currentIndex], targetSlot, remaining, player, playerInv, hotbarInv);
                    TraceAutoFill("TARGET idx={0} gridSlot={1} totalAvailable={2} minRequired={3} after={4}",
                        currentIndex,
                        gridSlot,
                        totalAvailable,
                        ingredient.IsTool ? 1 : ingredient.Quantity,
                        DescribeSlot(targetSlot));

                    int minRequired = ingredient.IsTool ? 1 : ingredient.Quantity;
                    if (totalAvailable < minRequired) return false;
                }
            }

            if (fillMax)
            {
                TraceGrid("final-grid", craftingGrid, craftingInputSlotCount);
            }
            return true;
        }

        private static string GetIngredientGroupKey(CraftingRecipeIngredient ingredient)
        {
            if (ingredient == null) return "";

            string variants = ingredient.AllowedVariants == null
                ? ""
                : string.Join(",", ingredient.AllowedVariants.OrderBy(value => value));

            return string.Format("{0}|{1}|{2}|{3}|{4}",
                ingredient.Type,
                ingredient.Code,
                ingredient.MatchingType,
                ingredient.Quantity,
                variants);
        }

        private static void TraceAutoFill(string message, params object[] args)
        {
            if (RecipeExplorerMod.Config?.EnableAutoFillTraceLogging == true)
            {
                capi?.Logger.Notification("[BetterHandbook/AutoFillTrace] " + message, args);
            }
        }

        private static void TraceGrid(string phase, InventoryBase craftingGrid, int inputSlotCount)
        {
            if (RecipeExplorerMod.Config?.EnableAutoFillTraceLogging != true) return;
            var sb = new System.Text.StringBuilder();
            sb.Append(phase).Append(':');
            for (int i = 0; i < inputSlotCount; i++)
            {
                sb.Append(' ').Append(i).Append('=').Append(DescribeSlot(craftingGrid[i]));
            }
            TraceAutoFill(sb.ToString());
        }

        private static void TraceMatchingSources(string phase, CraftingRecipeIngredient ingredient, InventoryBase backpack, InventoryBase hotbar, InventoryBase craftingGrid, int craftingInputSlotCount)
        {
            if (RecipeExplorerMod.Config?.EnableAutoFillTraceLogging != true) return;
            var sb = new System.Text.StringBuilder();
            sb.Append(phase).Append(" ingredient=").Append(DescribeIngredient(ingredient)).Append(':');
            foreach (var slot in EnumerateUniqueSlots(backpack, hotbar, craftingGrid))
            {
                if (craftingGrid != null && slot?.Inventory == craftingGrid && craftingGrid.GetSlotId(slot) >= craftingInputSlotCount) continue;
                if (slot == null || slot.Empty || !DoesSlotMatchIngredient(slot, ingredient)) continue;
                sb.Append(' ').Append(DescribeSlot(slot));
            }
            TraceAutoFill(sb.ToString());
        }

        private static string DescribeIngredient(CraftingRecipeIngredient ingredient)
        {
            if (ingredient == null) return "<null>";
            return string.Format("{0}:{1} qty={2} match={3}", ingredient.Type, ingredient.Code, ingredient.Quantity, ingredient.MatchingType);
        }

        private static string DescribeSlot(ItemSlot slot)
        {
            if (slot == null) return "<null>";
            string location = GetSlotKey(slot);
            if (slot.Empty) return location + ":<empty>";
            return string.Format("{0}:{1}x{2}", location, slot.Itemstack?.Collectible?.Code, slot.StackSize);
        }

        private static int TransferFromAnySource(CraftingRecipeIngredient ingredient, ItemSlot targetSlot, int desired, IPlayer player, InventoryBase backpack, InventoryBase hotbar)
        {
            if (desired <= 0) return 0;

            int moved = 0;
            foreach (var slot in EnumerateUniqueSlots(backpack, hotbar))
            {
                if (moved >= desired) break;
                if (slot == null || slot.Empty) continue;
                if (!DoesSlotMatchIngredient(slot, ingredient)) continue;

                int want = desired - moved;
                var op = new ItemStackMoveOperation(capi.World, EnumMouseButton.Left, 0, EnumMergePriority.AutoMerge, want);
                op.ActingPlayer = player;

                string sourceBefore = DescribeSlot(slot);
                string targetBefore = DescribeSlot(targetSlot);
                object packet = player.InventoryManager.TryTransferTo(slot, targetSlot, ref op);
                if (packet != null) capi.Network.SendPacketClient(packet);
                TraceAutoFill("MOVE-IN sourceBefore={0} targetBefore={1} want={2} moved={3} sourceAfter={4} targetAfter={5}",
                    sourceBefore,
                    targetBefore,
                    want,
                    op.MovedQuantity,
                    DescribeSlot(slot),
                    DescribeSlot(targetSlot));

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
            return moved;
        }

        private static int TransferAwayToInventory(ItemSlot sourceSlot, int desired, IPlayer player, InventoryBase backpack, InventoryBase hotbar)
        {
            if (sourceSlot == null || sourceSlot.Empty || desired <= 0) return 0;

            int moved = 0;
            foreach (var targetSlot in EnumerateUniqueSlots(backpack, hotbar))
            {
                if (moved >= desired) break;
                if (targetSlot == null || targetSlot == sourceSlot) continue;
                if (!targetSlot.Empty && !targetSlot.CanHold(sourceSlot)) continue;

                int want = desired - moved;
                var op = new ItemStackMoveOperation(capi.World, EnumMouseButton.Left, 0, EnumMergePriority.AutoMerge, want);
                op.ActingPlayer = player;

                string sourceBefore = DescribeSlot(sourceSlot);
                string targetBefore = DescribeSlot(targetSlot);
                object packet = player.InventoryManager.TryTransferTo(sourceSlot, targetSlot, ref op);
                if (packet != null) capi.Network.SendPacketClient(packet);
                TraceAutoFill("MOVE-OUT sourceBefore={0} targetBefore={1} want={2} moved={3} sourceAfter={4} targetAfter={5}",
                    sourceBefore,
                    targetBefore,
                    want,
                    op.MovedQuantity,
                    DescribeSlot(sourceSlot),
                    DescribeSlot(targetSlot));

                if (op.MovedQuantity <= 0) continue;
                moved += op.MovedQuantity;
            }

            return moved;
        }

        private static int CountTotalMatchingItems(CraftingRecipeIngredient ingredient, InventoryBase backpack, InventoryBase hotbar, InventoryBase craftingGrid = null, int craftingInputSlotCount = 0)
        {
            int total = 0;

            foreach (var slot in EnumerateUniqueSlots(backpack, hotbar, craftingGrid))
            {
                if (craftingGrid != null && slot?.Inventory == craftingGrid && craftingGrid.GetSlotId(slot) >= craftingInputSlotCount) continue;
                if (slot == null || slot.Empty) continue;
                if (DoesSlotMatchIngredient(slot, ingredient))
                {
                    total += slot.StackSize;
                }
            }

            return total;
        }

        private static int CountMatchingItemsInSlot(ItemSlot slot, CraftingRecipeIngredient ingredient)
        {
            if (slot == null || slot.Empty) return 0;
            return DoesSlotMatchIngredient(slot, ingredient) ? slot.StackSize : 0;
        }

        private static int MaxStackSizeForIngredient(CraftingRecipeIngredient ingredient, InventoryBase backpack, InventoryBase hotbar, InventoryBase craftingGrid, int craftingInputSlotCount)
        {
            foreach (var slot in EnumerateUniqueSlots(backpack, hotbar, craftingGrid))
            {
                if (craftingGrid != null && slot?.Inventory == craftingGrid && craftingGrid.GetSlotId(slot) >= craftingInputSlotCount) continue;
                if (slot == null || slot.Empty) continue;
                if (DoesSlotMatchIngredient(slot, ingredient))
                {
                    return slot.Itemstack?.Collectible?.MaxStackSize ?? 64;
                }
            }

            return ingredient?.ResolvedItemStack?.Collectible?.MaxStackSize ?? 64;
        }

        private static IEnumerable<ItemSlot> EnumerateUniqueSlots(params InventoryBase[] inventories)
        {
            var seen = new HashSet<string>();

            foreach (var inventory in inventories)
            {
                if (inventory == null) continue;

                foreach (var slot in inventory)
                {
                    if (slot == null) continue;

                    string key = GetSlotKey(slot);
                    if (!seen.Add(key)) continue;

                    yield return slot;
                }
            }
        }

        private static string GetSlotKey(ItemSlot slot)
        {
            if (slot?.Inventory != null)
            {
                return slot.Inventory.InventoryID + ":" + slot.Inventory.GetSlotId(slot);
            }

            return "slot:" + slot.GetHashCode();
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

        private static bool DoesSlotMatchIngredient(ItemSlot slot, CraftingRecipeIngredient ingredient)
        {
            if (slot?.Itemstack == null || ingredient == null) return false;

            // First try the game's built-in matching logic
            bool matches = ingredient.SatisfiesAsIngredient(slot.Itemstack, checkStackSize: false);

            // If no match, try legacy wildcard matching, but never broaden
            // tags-only ingredients like tool-axe to the default *:* code.
            if (!matches)
            {
                if (ingredient.MatchingType == EnumRecipeMatchType.TagsOnly || ingredient.Code == null)
                {
                    return false;
                }

                if (!ingredient.CheckTags(slot.Itemstack, slot.Itemstack.Collectible))
                {
                    return false;
                }

                bool typeMatches = (ingredient.Type == EnumItemClass.Block && slot.Itemstack.Class == EnumItemClass.Block) ||
                                   (ingredient.Type == EnumItemClass.Item && slot.Itemstack.Class == EnumItemClass.Item);

                if (typeMatches)
                {
                    if (ingredient.Code != null && ingredient.Code.Path.Contains('*'))
                    {
                        matches = WildcardUtil.Match(ingredient.Code, slot.Itemstack.Collectible.Code, ingredient.AllowedVariants);
                    }
                    else if (ingredient.MatchingType != EnumRecipeMatchType.Exact)
                    {
                        matches = TryWildcardBaseMatch(slot, ingredient);
                    }

                    if (matches && ingredient.SkipVariants != null && WildcardUtil.Match(ingredient.Code, slot.Itemstack.Collectible.Code, ingredient.SkipVariants))
                    {
                        matches = false;
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

        private class IngredientInfo
        {
            public string SlotKey { get; set; }
            public CraftingRecipeIngredient Ingredient { get; set; }
            public ItemSlot FoundSlot { get; set; }
        }
    }
}
