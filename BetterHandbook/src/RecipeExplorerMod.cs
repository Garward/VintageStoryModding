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
        private const string HarmonyId = "garward.betterhandbook.recipeexplorer";
        private static RecipeExplorerMod instance;
        private ICoreClientAPI capi;
        private RecipeIndexSystem recipeIndex;
        private Harmony harmony;
        public static ModConfig Config { get; private set; }

        public override void StartClientSide(ICoreClientAPI api)
        {
            base.StartClientSide(api);
            instance = this;
            capi = api;

            // Load config
            Config = api.LoadModConfig<ModConfig>("betterhandbook.json") ?? new ModConfig();
            BetterHandbookLog.Config = Config;
            api.StoreModConfig(Config, "betterhandbook.json");

            // Initialize Harmony for patching
            harmony = new Harmony(HarmonyId);

            // Initialize recipe indexing system
            recipeIndex = new RecipeIndexSystem(api);

            // Initialize auto-craft system
            AutoCraftSystem.Initialize(api, harmony);

            // Build recipe index after world finalized
            api.Event.LevelFinalize += OnLevelFinalize;

            // Register hotkeys
            RegisterHotkeys();

            BetterHandbookLog.Info(api, "[BetterHandbook] Recipe Explorer features loaded successfully");
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
                    BetterHandbookLog.Info(capi, "[BetterHandbook/RecipeExplorer] First index pass was empty; retrying after sync delay");
                    BuildIndexNow();
                }
            }, 3000);
        }

        private void BuildIndexNow()
        {
            BetterHandbookLog.Info(capi, "[BetterHandbook/RecipeExplorer] Building recipe index...");
            var startTime = capi.World.ElapsedMilliseconds;
            recipeIndex.BuildIndex();
            var elapsed = capi.World.ElapsedMilliseconds - startTime;
            BetterHandbookLog.Info(capi, "[BetterHandbook/RecipeExplorer] Recipe index built in {0}ms", elapsed);
        }

        private void RegisterHotkeys()
        {
            // U key - Show recipes that USE this item (like JEI)
            capi.Input.RegisterHotKey("betterhandbook_show_uses",
                "Show recipes that use this item",
                GlKeys.U,
                HotkeyType.GUIOrOtherControls);

            // Hook up hotkey handler
            capi.Input.SetHotKeyHandler("betterhandbook_show_uses", OnShowUsesHotkey);
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

            return ShowUsesForStack(slot.Itemstack);
        }

        public static bool ShowUsesForStack(ItemStack stack)
        {
            if (instance == null || stack == null)
            {
                return false;
            }

            var ingredientRecipes = instance.recipeIndex.GetRecipesThatUse(stack);
            var toolRecipes = instance.recipeIndex.GetRecipesUsingAsTool(stack);
            var producesRecipes = instance.recipeIndex.GetRecipesThatProduce(stack);
            var machineRecipes = instance.recipeIndex.GetRecipesProducedByMachine(stack);

            var dialog = new GuiDialogRecipeUses(instance.capi, stack, ingredientRecipes, toolRecipes, producesRecipes, machineRecipes);
            dialog.TryOpen();

            return true;
        }

        public override void Dispose()
        {
            harmony?.UnpatchAll(HarmonyId);
            if (instance == this)
            {
                instance = null;
            }
            base.Dispose();
        }
    }
}
