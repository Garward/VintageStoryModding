using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

namespace VRPG.Client.Patches;

public static class HudStatbarPatch
{
    private static readonly FieldInfo? HealthbarField = AccessTools.Field(typeof(HudStatbar), "healthbar");
    private static readonly HashSet<GuiElementStatbar> HiddenHealthbars = new HashSet<GuiElementStatbar>();

    public static void Patch(Harmony harmony)
    {
        MethodInfo composeGuis = AccessTools.Method(typeof(HudStatbar), "ComposeGuis");
        MethodInfo updateHealth = AccessTools.Method(typeof(HudStatbar), "UpdateHealth");
        MethodInfo renderStatbar = AccessTools.Method(typeof(GuiElementStatbar), nameof(GuiElementStatbar.RenderInteractiveElements));

        harmony.Patch(composeGuis, postfix: new HarmonyMethod(typeof(HudStatbarPatch), nameof(ComposeGuisPostfix)));
        harmony.Patch(updateHealth, postfix: new HarmonyMethod(typeof(HudStatbarPatch), nameof(UpdateHealthPostfix)));
        harmony.Patch(renderStatbar, prefix: new HarmonyMethod(typeof(HudStatbarPatch), nameof(RenderStatbarPrefix)));
    }

    public static void ComposeGuisPostfix(HudStatbar __instance)
    {
        HideHealthbar(__instance);
    }

    public static void UpdateHealthPostfix(HudStatbar __instance)
    {
        HideHealthbar(__instance);
    }

    public static bool RenderStatbarPrefix(GuiElementStatbar __instance)
    {
        return !VrpgClientHudRuntime.HideVanillaStatbar || !HiddenHealthbars.Contains(__instance);
    }

    private static void HideHealthbar(HudStatbar instance)
    {
        if (!VrpgClientHudRuntime.HideVanillaStatbar)
        {
            return;
        }

        if (HealthbarField?.GetValue(instance) is not GuiElementStatbar healthbar)
        {
            return;
        }

        HiddenHealthbars.Add(healthbar);
        healthbar.HideWhenFull = true;
        healthbar.ShowValueOnHover = false;
        healthbar.ShouldFlash = false;

        if (healthbar.GetValue() != 1f)
        {
            healthbar.SetValues(1f, 0f, 1f);
        }
    }
}
