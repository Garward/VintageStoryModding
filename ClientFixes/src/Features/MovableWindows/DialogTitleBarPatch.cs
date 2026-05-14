using System;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Client;
using ClientFixes.Config;

namespace ClientFixes.Features.MovableWindows
{
    [HarmonyPatch(typeof(GuiElementDialogTitleBar), "SetUpMovableState")]
    internal static class DialogTitleBarPatch
    {
        private static readonly FieldInfo BaseComposerField =
            AccessTools.Field(typeof(GuiElementDialogTitleBar), "baseComposer");

        private static void Prefix(GuiElementDialogTitleBar __instance, ref string val)
        {
            try
            {
                MovableWindowsConfig config = ClientFixesModSystem.Config?.MovableWindows;
                if (config == null || !config.Enabled)
                {
                    return;
                }

                if (val == "auto" && config.PreventFixedModeSelection)
                {
                    val = "manual";
                    return;
                }

                if (val != null || !config.MakeNewWindowsMovable)
                {
                    return;
                }

                GuiComposer composer = BaseComposerField?.GetValue(__instance) as GuiComposer;
                if (composer?.Api?.Gui == null || string.IsNullOrEmpty(composer.DialogName))
                {
                    return;
                }

                if (composer.Api.Gui.GetDialogPosition(composer.DialogName) == null)
                {
                    val = "manual";
                }
            }
            catch
            {
                // Keep vanilla behavior if private titlebar internals change.
            }
        }
    }
}
