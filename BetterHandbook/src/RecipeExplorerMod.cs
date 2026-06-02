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
            ApplyConfig(BetterHandbookConfigStore.Load(api));
            BetterHandbookConfigLibIntegration.TryInitialize(api);

            // Initialize Harmony for patching
            harmony = new Harmony(HarmonyId);

            // Initialize recipe indexing system
            recipeIndex = new RecipeIndexSystem(api);

            // Initialize auto-craft system
            AutoCraftSystem.Initialize(api, harmony);
            if (BetterHandbookFeatures.RecipeOverlays)
            {
                HandbookRecipeOverlays.SetApi(api);
                HandbookRecipeAssets.Load(api);
            }

            // Build recipe index after world finalized
            if (BetterHandbookFeatures.UsesLookup)
            {
                api.Event.LevelFinalize += OnLevelFinalize;
            }

            // Register hotkeys
            if (BetterHandbookFeatures.UsesHotkey)
            {
                RegisterHotkeys();
            }

            BetterHandbookLog.Info(api, "[BetterHandbook] Recipe Explorer features loaded successfully");
        }

        private void OnLevelFinalize()
        {
            if (!BetterHandbookFeatures.UsesLookup) return;

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
            if (!BetterHandbookFeatures.UsesLookup || instance == null || stack == null || instance.recipeIndex == null)
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

        internal static void ApplyConfig(ModConfig config)
        {
            Config = config ?? new ModConfig();
            BetterHandbookLog.Config = Config;
        }

        public override void Dispose()
        {
            BetterHandbookConfigLibIntegration.Cleanup();
            harmony?.UnpatchAll(HarmonyId);
            if (instance == this)
            {
                instance = null;
            }
            base.Dispose();
        }
    }
}
