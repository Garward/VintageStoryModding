using HarmonyLib;
using System.Reflection;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.Client.NoObf;

namespace VRPG.Client.Patches;

/// <summary>Prevents Vanilla block placement and mining from leaking into combat context.</summary>
public static class CombatPlacementPatch
{
    public static void Patch(Harmony harmony)
    {
        HarmonyMethod placementPrefix = new(typeof(CombatPlacementPatch), nameof(PlacementPrefix));
        HarmonyMethod blockBreakPrefix = new(typeof(CombatPlacementPatch), nameof(BlockBreakPrefix));

        harmony.Patch(
            AccessTools.Method(typeof(ClientMain), nameof(ClientMain.OnPlayerTryPlace)),
            prefix: placementPrefix);
        harmony.Patch(
            AccessTools.Method(typeof(ClientMain), nameof(ClientMain.OnPlayerTryDestroyBlock)),
            prefix: blockBreakPrefix);
        harmony.Patch(
            AccessTools.Method(typeof(SystemMouseInWorldInteractions), "InitBlockBreakSurvival"),
            prefix: blockBreakPrefix);
        harmony.Patch(
            AccessTools.Method(typeof(SystemMouseInWorldInteractions), "ContinueBreakSurvival"),
            prefix: blockBreakPrefix);
        harmony.Patch(
            AccessTools.Method(
                typeof(SystemMouseInWorldInteractions),
                "TryBeginUseBlock",
                new[] { typeof(Block), typeof(BlockSelection) }),
            prefix: new HarmonyMethod(typeof(CombatPlacementPatch), nameof(BlockUsePrefix)));
        harmony.Patch(
            AccessTools.Method(
                typeof(SystemMouseInWorldInteractions),
                "TryBeginUseActiveSlotItem",
                new[] { typeof(BlockSelection), typeof(EntitySelection) }),
            prefix: new HarmonyMethod(typeof(CombatPlacementPatch), nameof(HeldBlockUsePrefix)));
    }

    public static bool PlacementPrefix(ref string failureCode, ref bool __result)
    {
        if (!VrpgClientHudRuntime.CombatActionBarActive)
        {
            return true;
        }

        failureCode = "vrpg-combat-build-disabled";
        __result = false;
        return false;
    }

    public static bool BlockBreakPrefix()
    {
        return !VrpgClientHudRuntime.CombatActionBarActive;
    }

    public static bool BlockUsePrefix(ref bool __result)
    {
        if (!VrpgClientHudRuntime.CombatActionBarActive)
        {
            return true;
        }

        __result = false;
        return false;
    }

    public static bool HeldBlockUsePrefix(BlockSelection? blockSel, ref bool __result)
    {
        if (!VrpgClientHudRuntime.CombatActionBarActive || blockSel == null)
        {
            return true;
        }

        __result = false;
        return false;
    }
}
