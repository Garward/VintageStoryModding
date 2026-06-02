using System;
using System.IO;
using System.Text.Json;
using Vintagestory.API.Common;

namespace RecipeExplorer
{
    public class ModConfig
    {
        /// <summary>
        /// Show chat messages when auto-fill succeeds or fails
        /// </summary>
        public bool ShowAutoFillMessages { get; set; } = false;

        /// <summary>
        /// Enable cached/lazy handbook overview filtering, fast reopen/close behavior, and related open-page reuse.
        /// Disable this when another mod owns handbook performance patches.
        /// </summary>
        public bool EnableHandbookPerformanceOptimizations { get; set; } = true;

        /// <summary>
        /// Enable the grid-recipe candidate filter used while composing vanilla "Created by" handbook sections.
        /// </summary>
        public bool EnableCreatedByRecipeFilter { get; set; } = true;

        /// <summary>
        /// Collapse repeated material/geology variant rows in handbook overview/search lists.
        /// </summary>
        public bool EnableVariantGrouping { get; set; } = true;

        /// <summary>
        /// Enable the per-mod handbook category tab and mod list.
        /// </summary>
        public bool EnableModCategoryTab { get; set; } = true;

        /// <summary>
        /// Enable bookmark storage, bookmark tab, and bookmark detail-page button.
        /// </summary>
        public bool EnableBookmarks { get; set; } = true;

        /// <summary>
        /// Enable the locked-page detail button and automatic locked-page opening.
        /// </summary>
        public bool EnableLockedPage { get; set; } = true;

        /// <summary>
        /// Enable the handbook Auto-Fill button.
        /// </summary>
        public bool EnableAutoFillButton { get; set; } = true;

        /// <summary>
        /// Enable the handbook Uses button.
        /// </summary>
        public bool EnableUsesButton { get; set; } = true;

        /// <summary>
        /// Enable the U hotkey for opening the Uses dialog.
        /// </summary>
        public bool EnableUsesHotkey { get; set; } = true;

        /// <summary>
        /// Enable recipe overlays such as tool/durability markings and failed Auto-Fill highlights.
        /// </summary>
        public bool EnableRecipeOverlays { get; set; } = true;

        /// <summary>
        /// Enable mouse-wheel control for handbook item/recipe slideshow components.
        /// </summary>
        public bool EnableSlideshowScroll { get; set; } = true;

        /// <summary>
        /// Enable mouse-wheel selection of alternate crafting-grid outputs when several recipes match the same inputs.
        /// </summary>
        public bool EnableCraftingOutputConflictScroll { get; set; } = true;

        /// <summary>
        /// Enable the safety fix for generic typed container handbook drops with no stored type.
        /// </summary>
        public bool EnableGenericContainerDropFix { get; set; } = true;

        /// <summary>
        /// Log one-time BetterHandbook startup and index summary messages.
        /// </summary>
        public bool EnableInfoLogging { get; set; } = false;

        /// <summary>
        /// Log detailed recipe, button, and autofill diagnostics. This is very noisy.
        /// </summary>
        public bool EnableDiagnosticLogging { get; set; } = false;

        /// <summary>
        /// Log every step of shift-left-click Auto-Fill. Only enable while reproducing autofill bugs.
        /// </summary>
        public bool EnableAutoFillTraceLogging { get; set; } = false;

        /// <summary>
        /// Log recoverable warnings. Errors are always logged.
        /// </summary>
        public bool EnableFailureLogging { get; set; } = true;
    }

    internal static class BetterHandbookLog
    {
        public static ModConfig Config { get; set; } = new ModConfig();

        public static void Info(Vintagestory.API.Common.ICoreAPI api, string message, params object[] args)
        {
            if (Config?.EnableInfoLogging == true)
            {
                api?.Logger.Notification(message, args);
            }
        }

        public static void Diagnostic(Vintagestory.API.Common.ICoreAPI api, string message, params object[] args)
        {
            if (Config?.EnableDiagnosticLogging == true)
            {
                api?.Logger.Notification(message, args);
            }
        }

        public static void Failure(Vintagestory.API.Common.ICoreAPI api, string message, params object[] args)
        {
            if (Config?.EnableFailureLogging != false)
            {
                api?.Logger.Warning(message, args);
            }
        }
    }

    internal static class BetterHandbookFeatures
    {
        private static ModConfig Config => BetterHandbookLog.Config ?? new ModConfig();

        public static bool HandbookPerformance => Config.EnableHandbookPerformanceOptimizations;
        public static bool CreatedByRecipeFilter => HandbookPerformance && Config.EnableCreatedByRecipeFilter;
        public static bool VariantGrouping => Config.EnableVariantGrouping;
        public static bool ModCategoryTab => Config.EnableModCategoryTab;
        public static bool Bookmarks => Config.EnableBookmarks;
        public static bool LockedPage => Config.EnableLockedPage;
        public static bool AutoFillButton => Config.EnableAutoFillButton;
        public static bool UsesButton => Config.EnableUsesButton;
        public static bool UsesHotkey => Config.EnableUsesHotkey;
        public static bool UsesLookup => UsesButton || UsesHotkey;
        public static bool RecipeOverlays => Config.EnableRecipeOverlays;
        public static bool SlideshowScroll => Config.EnableSlideshowScroll;
        public static bool CraftingOutputConflictScroll => Config.EnableCraftingOutputConflictScroll;
        public static bool GenericContainerDropFix => Config.EnableGenericContainerDropFix;
    }

    internal static class BetterHandbookConfigStore
    {
        public const string ConfigFileName = "betterhandbook.json";

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            IncludeFields = true
        };

        public static ModConfig Load(ICoreAPI api)
        {
            if (api == null) return new ModConfig();

            string path = ConfigPath(api);
            try
            {
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    ModConfig loaded = JsonSerializer.Deserialize<ModConfig>(json, JsonOptions);
                    if (loaded != null)
                    {
                        Save(api, loaded);
                        return loaded;
                    }
                }
            }
            catch (Exception ex)
            {
                api.Logger.Warning("[BetterHandbook] Failed to load {0}; using defaults: {1}", ConfigFileName, ex.Message);
            }

            ModConfig config = new ModConfig();
            Save(api, config);
            return config;
        }

        public static void Save(ICoreAPI api, ModConfig config)
        {
            if (api == null) return;

            try
            {
                string path = ConfigPath(api);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, JsonSerializer.Serialize(config ?? new ModConfig(), JsonOptions));
            }
            catch (Exception ex)
            {
                api.Logger.Warning("[BetterHandbook] Failed to save {0}: {1}", ConfigFileName, ex.Message);
            }
        }

        private static string ConfigPath(ICoreAPI api)
        {
            return Path.Combine(api.DataBasePath, "ModConfig", ConfigFileName);
        }
    }
}
