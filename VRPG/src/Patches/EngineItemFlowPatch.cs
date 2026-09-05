using System;
using System.Reflection;
using HarmonyLib;
using VRPG.Inventory;
using Vintagestory.API.Common;

namespace VRPG.Patches;

/// <summary>
/// Redirects engine grants before Vanilla selects or mutates a player slot. Ownership remains
/// disabled until the experimental inventory transaction explicitly enables it.
/// </summary>
public static class EngineItemFlowPatch
{
    private static FieldInfo? playerField;

    public static System.Func<string, bool>? OwnershipResolver { get; set; }
    public static System.Func<string, ItemStack, EngineItemIngressResult>? Route { get; set; }

    public static void Patch(Harmony harmony, ILogger logger)
    {
        Type? managerType = AccessTools.TypeByName("Vintagestory.Common.PlayerInventoryManager");
        MethodInfo? target = managerType == null
            ? null
            : AccessTools.Method(managerType, "TryGiveItemstack", new[] { typeof(ItemStack), typeof(bool) });
        playerField = managerType == null ? null : AccessTools.Field(managerType, "player");
        MethodInfo? prefix = AccessTools.Method(typeof(EngineItemFlowPatch), nameof(TryGiveItemstackPrefix));
        if (target == null || playerField == null || prefix == null)
        {
            logger.Error("[VRPG/Inventory] Engine item-flow bridge could not find the supported inventory grant boundary.");
            return;
        }

        harmony.Patch(target, prefix: new HarmonyMethod(prefix));
    }

    public static bool TryGiveItemstackPrefix(object __instance, ItemStack itemstack, ref bool __result)
    {
        if (itemstack == null || itemstack.StackSize <= 0)
        {
            return true;
        }

        object? owner = playerField?.GetValue(__instance);
        string playerUid = owner switch
        {
            IPlayer player => player.PlayerUID,
            _ => ""
        };
        if (string.IsNullOrWhiteSpace(playerUid)
            || OwnershipResolver?.Invoke(playerUid) != true
            || Route == null)
        {
            return true;
        }

        EngineItemIngressResult result = Route(playerUid, itemstack);
        if (result.Disposition == EngineItemIngressDisposition.PassThrough)
        {
            return true;
        }

        __result = result.Disposition == EngineItemIngressDisposition.Accepted;
        if (__result)
        {
            itemstack.StackSize = 0;
        }
        return false;
    }

    public static void Reset()
    {
        OwnershipResolver = null;
        Route = null;
        playerField = null;
    }
}
