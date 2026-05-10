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

            string itemName = slot.Itemstack.GetName();
            string itemKey = string.Format("{0}:{1}", slot.Itemstack.Class, slot.Itemstack.Collectible.Code);

            capi.Logger.Debug("[RecipeExplorer] Show uses for: {0} (key: {1})", itemName, itemKey);

            // Get recipes that use this item
            var recipes = recipeIndex.GetRecipesThatUse(slot.Itemstack);
            capi.Logger.Debug("[RecipeExplorer] Found {0} recipes", recipes != null ? recipes.Count : -1);

            if (recipes != null && recipes.Count > 0)
            {
                capi.Logger.Debug("[RecipeExplorer] First recipe: {0}", recipes[0]?.Name ?? "NULL");
            }

            // Open GUI dialog showing recipes
            capi.Logger.Debug("[RecipeExplorer] Creating dialog");
            var dialog = new GuiDialogRecipeUses(capi, slot.Itemstack, recipes);
            capi.Logger.Debug("[RecipeExplorer] Calling TryOpen");
            dialog.TryOpen();
            capi.Logger.Debug("[RecipeExplorer] TryOpen complete");

            return true;
        }

        public override void Dispose()
        {
            harmony?.UnpatchAll("com.recipeexplorer.mod");
            base.Dispose();
        }
    }
}
