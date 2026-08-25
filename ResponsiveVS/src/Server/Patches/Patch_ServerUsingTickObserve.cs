using System.Collections.Generic;
using HarmonyLib;
using ResponsiveVS.Diagnostics;
using Vintagestory.API.Common;
using Vintagestory.Server;

namespace ResponsiveVS.Server.Patches;

public static class Patch_ServerUsingTickObserve
{
    private static readonly System.Reflection.FieldInfo ServerField =
        AccessTools.Field(AccessTools.TypeByName("Vintagestory.Server.ServerSystem"), "server");

    public static void OnUsingTickPrefix(object __instance, float dt, out Dictionary<string, ServerTimingState> __state)
    {
        __state = CapturePlayers(__instance, dt);
    }

    public static void OnUsingTickPostfix(object __instance, float dt, Dictionary<string, ServerTimingState> __state)
    {
        if (__state == null || !ResponsiveDiagnostics.BasicEnabled)
        {
            return;
        }

        Dictionary<string, ServerTimingState> after = CapturePlayers(__instance, dt);
        foreach (KeyValuePair<string, ServerTimingState> pair in after)
        {
            __state.TryGetValue(pair.Key, out ServerTimingState before);
            ServerTimingState now = pair.Value;
            if (before == null)
            {
                continue;
            }

            bool active = before.HandUse != EnumHandInteract.None || now.HandUse != EnumHandInteract.None;
            int delta = now.UsingCount - before.UsingCount;
            if (!active || delta == 0)
            {
                continue;
            }

            ResponsiveDiagnostics.Basic(
                "WORLD TIMING SERVER usingtick player={0} dt={1:0.0000} hand={2}->{3} using={4}->{5} delta={6} slot={7}",
                now.PlayerName,
                dt,
                before.HandUse,
                now.HandUse,
                before.UsingCount,
                now.UsingCount,
                delta,
                now.ActiveSlot);
        }
    }

    private static Dictionary<string, ServerTimingState> CapturePlayers(object instance, float dt)
    {
        Dictionary<string, ServerTimingState> states = new();
        ServerMain server = ServerField?.GetValue(instance) as ServerMain;
        if (server == null)
        {
            return states;
        }

        foreach (KeyValuePair<string, ServerPlayer> pair in server.PlayersByUid)
        {
            ServerPlayer player = pair.Value;
            states[pair.Key] = new ServerTimingState
            {
                PlayerName = player?.PlayerName,
                HandUse = player?.Entity?.Controls?.HandUse ?? EnumHandInteract.None,
                UsingCount = player?.Entity?.Controls?.UsingCount ?? -1,
                ActiveSlot = InventoryDiagFormat.Slot(player?.InventoryManager?.ActiveHotbarSlot)
            };
        }

        return states;
    }

    public sealed class ServerTimingState
    {
        public string PlayerName;
        public EnumHandInteract HandUse;
        public int UsingCount;
        public string ActiveSlot;
    }
}
