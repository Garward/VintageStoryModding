using System;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.Server;

namespace VRPG.Patches;

public static partial class NativeInventoryMutationGuardPatch
{
    [ThreadStatic] private static WipeAttempt? wipeAttempt;
    private static readonly FieldInfo CommandServer = AccessTools.Field(typeof(ServerSystem), "server");
    private sealed record WipeAttempt(TextCommandCallingArgs Args, ServerWorldPlayerData? Data, WipeAttempt? Previous)
    {
        public bool Refused { get; set; }
    }

    private static bool WipeInventoryPrefix(CmdPlayer __instance, PlayerUidName targetPlayer, TextCommandCallingArgs args,
        ref TextCommandResult __result, out WipeAttempt? __state)
    {
        __state = null;
        try
        {
            if (AllowsUid(targetPlayer?.Uid) && CommandServer.GetValue(__instance) is ServerMain server)
            {
                __state = new(args, server.GetClientByUID(targetPlayer!.Uid)?.WorldData, wipeAttempt);
                wipeAttempt = __state;
                return true;
            }
        }
        catch { }
        RecordWipeRefusal(args);
        __result = ClearRefusal();
        return false;
    }

    private static void RecordClearRefusal(InventoryBase inventory)
    {
        if (wipeAttempt?.Data is not { } data) return;
        var inventories = LoadedInventories.GetValue(data) as Vintagestory.API.Datastructures.OrderedDictionary<string, InventoryBase>;
        if (inventories?.TryGetValue(inventory.InventoryID, out var selected) == true && ReferenceEquals(selected, inventory))
            wipeAttempt.Refused = true;
    }

    private static void RecordWipeRefusal(TextCommandCallingArgs args)
    {
        if (clearCommandInvocation is { } invocation && ReferenceEquals(invocation.Args, args)) invocation.Refused = true;
    }

    private static void WipeInventoryPostfix(WipeAttempt? __state, ref TextCommandResult __result)
    {
        if (__state?.Refused != true) return;
        RecordWipeRefusal(__state.Args);
        // Earlier permitted inventories may already have cleared. Do not claim rollback.
        __result = TextCommandResult.Error("vrpg:inventory-clear-interrupted", "inventory-clear-interrupted");
    }

    private static Exception? WipeInventoryFinalizer(Exception? __exception, WipeAttempt? __state)
    {
        if (__state != null) wipeAttempt = __state.Previous;
        return __exception;
    }
}
