using HarmonyLib;
using System;
using System.Reflection;
using Vintagestory.API.Client;
using Vintagestory.Client;
using Vintagestory.Client.NoObf;

namespace VRPG.Client.Patches;

/// <summary>Keeps Vanilla hotbar behavior active while replacing only its rendering.</summary>
public static class HudHotbarPatch
{
    private static readonly MethodInfo OnKeySlotMethod = AccessTools.Method(
        typeof(HudHotbar),
        "OnKeySlot",
        new[] { typeof(int), typeof(bool) });

    public static void Patch(Harmony harmony)
    {
        MethodInfo render = AccessTools.Method(typeof(HudHotbar), nameof(HudHotbar.OnRenderGUI));
        harmony.Patch(render, prefix: new HarmonyMethod(typeof(HudHotbarPatch), nameof(RenderPrefix)));

        MethodInfo texturesLoaded = AccessTools.Method(typeof(HudHotbar), nameof(HudHotbar.OnBlockTexturesLoaded));
        harmony.Patch(texturesLoaded, postfix: new HarmonyMethod(typeof(HudHotbarPatch), nameof(TexturesLoadedPostfix)));

        harmony.Patch(OnKeySlotMethod, prefix: new HarmonyMethod(typeof(HudHotbarPatch), nameof(SlotSelectionPrefix)));

        MethodInfo wheelSelection = AccessTools.Method(typeof(HudHotbar), nameof(HudHotbar.moveToHotbarSlot));
        harmony.Patch(wheelSelection, prefix: new HarmonyMethod(typeof(HudHotbarPatch), nameof(WheelSelectionPrefix)));
    }

    public static bool RenderPrefix()
    {
        return !VrpgClientHudRuntime.HideVanillaHotbar;
    }

    public static void TexturesLoadedPostfix(HudHotbar __instance)
    {
        for (int slot = 0; slot < 10; slot++)
        {
            int capturedSlot = slot;
            string code = "hotbarslot" + (slot + 1);
            HotKey? hotKey = ScreenManager.hotkeyManager.GetHotKeyByCode(code);
            if (hotKey == null)
            {
                continue;
            }

            hotKey.TriggerOnUpAlso = true;
            ScreenManager.hotkeyManager.SetHotKeyHandler(code, combination =>
                HandleSlot(__instance, capturedSlot, combination));
        }
    }

    public static bool SlotSelectionPrefix()
    {
        // Mouse-wheel selection reaches OnKeySlot directly rather than the
        // semantic hotkey handler. Combat profiles do not expose Vanilla items.
        return !VrpgClientHudRuntime.CombatActionBarActive;
    }

    public static bool WheelSelectionPrefix(int delta)
    {
        if (!VrpgClientHudRuntime.CombatActionBarActive)
        {
            return true;
        }

        VrpgClientHudRuntime.DispatchCombatWeaponCycle(delta);
        return false;
    }

    private static bool HandleSlot(HudHotbar hotbar, int slot, KeyCombination combination)
    {
        if (VrpgClientHudRuntime.CombatActionBarActive)
        {
            VrpgClientHudRuntime.DispatchCombatActionSlot(slot, !combination.OnKeyUp);
            return true;
        }

        if (!combination.OnKeyUp)
        {
            OnKeySlotMethod.Invoke(hotbar, new object[] { slot, true });
        }
        return true;
    }
}
