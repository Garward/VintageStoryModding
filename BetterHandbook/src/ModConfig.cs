namespace RecipeExplorer
{
    public class ModConfig
    {
        /// <summary>
        /// Show chat messages when auto-fill succeeds or fails
        /// </summary>
        public bool ShowAutoFillMessages { get; set; } = false;

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
}
