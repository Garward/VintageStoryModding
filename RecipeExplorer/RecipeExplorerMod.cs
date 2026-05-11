using System;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace RecipeExplorer
{
    /// <summary>
    /// Recipe Explorer - JEI-like recipe lookup mod (Simplified version)
    /// Press U on an item to see what recipes use it
    /// </summary>
    public class RecipeExplorerMod : ModSystem
    {
        private ICoreClientAPI capi;
        private RecipeIndexSystem recipeIndex;
        private Harmony harmony;
        public static ModConfig Config { get; private set; }

        public override void StartClientSide(ICoreClientAPI api)
        {
            base.StartClientSide(api);
            capi = api;

            // Load config
            Config = api.LoadModConfig<ModConfig>("recipeexplorer.json") ?? new ModConfig();
            api.StoreModConfig(Config, "recipeexplorer.json");

            // Initialize Harmony for patching
            harmony = new Harmony("com.recipeexplorer.mod");

            // Initialize recipe indexing system
            recipeIndex = new RecipeIndexSystem(api);

            // Initialize auto-craft system
            AutoCraftSystem.Initialize(api, harmony);

            // Build recipe index after world finalized
            api.Event.LevelFinalize += OnLevelFinalize;

            // Register hotkeys
            RegisterHotkeys();

            api.Logger.Notification("[RecipeExplorer] Mod loaded successfully");
        }

        private void OnLevelFinalize()
        {
            BuildIndexNow();

            // 1.22 timing: grid recipes may not be synced to client at LevelFinalize. Retry once
            // a couple seconds later if the first pass came up empty.
            capi.Event.RegisterCallback(_ =>
            {
                if (recipeIndex.IndexedItemCount == 0)
                {
                    capi.Logger.Notification("[RecipeExplorer] First index pass was empty; retrying after sync delay");
                    BuildIndexNow();
                }
            }, 3000);
        }

        private void BuildIndexNow()
        {
            capi.Logger.Notification("[RecipeExplorer] Building recipe index...");
            var startTime = capi.World.ElapsedMilliseconds;
            recipeIndex.BuildIndex();
            var elapsed = capi.World.ElapsedMilliseconds - startTime;
            capi.Logger.Notification("[RecipeExplorer] Recipe index built in {0}ms", elapsed);
        }

        private void RegisterHotkeys()
        {
            // U key - Show recipes that USE this item (like JEI)
            capi.Input.RegisterHotKey("recipeexplorer_show_uses",
                "Show recipes that use this item",
                GlKeys.U,
                HotkeyType.GUIOrOtherControls);

            // Hook up hotkey handler
            capi.Input.SetHotKeyHandler("recipeexplorer_show_uses", OnShowUsesHotkey);
        }

        /// <summary>
        /// U key - Show recipes that use the currently hovered item as ingredient
        /// </summary>
        private bool OnShowUsesHotkey(KeyCombination keyCombination)
        {
            var slot = capi.World.Player.InventoryManager.CurrentHoveredSlot;
            if (slot?.Itemstack == null)
            {
                return false;
            }

            var ingredientRecipes = recipeIndex.GetRecipesThatUse(slot.Itemstack);
            var toolRecipes = recipeIndex.GetRecipesUsingAsTool(slot.Itemstack);
            var producesRecipes = recipeIndex.GetRecipesThatProduce(slot.Itemstack);
            var machineRecipes = recipeIndex.GetRecipesProducedByMachine(slot.Itemstack);

            var dialog = new GuiDialogRecipeUses(capi, slot.Itemstack, ingredientRecipes, toolRecipes, producesRecipes, machineRecipes);
            dialog.TryOpen();

            return true;
        }

        public override void Dispose()
        {
            harmony?.UnpatchAll("com.recipeexplorer.mod");
            base.Dispose();
        }
    }
}
