using HarmonyLib;
using ResponsiveVS.Diagnostics;
using Vintagestory.API.Common;
using Vintagestory.Common;

namespace ResponsiveVS.Server.Patches;

public static class Patch_ServerInventoryNetworkObserve
{
    private static readonly AccessTools.FieldRef<InventoryNetworkUtil, InventoryBase> InvRef =
        AccessTools.FieldRefAccess<InventoryNetworkUtil, InventoryBase>("inv");

    public static void HandleClientPacketPrefix(InventoryNetworkUtil __instance, IPlayer byPlayer, int packetId, Packet_Client packet)
    {
        if (!ResponsiveDiagnostics.BasicEnabled || __instance?.Api?.Side != EnumAppSide.Server)
        {
            return;
        }

        InventoryBase inv = InvRef(__instance);
        ResponsiveDiagnostics.Basic(
            "SERVER packet-recv player={0} handlerInv={1} {2}",
            byPlayer?.PlayerName,
            inv?.InventoryID,
            InventoryDiagFormat.ClientPacket(packetId, packet));
    }

    public static void HandleClientPacketPostfix(InventoryNetworkUtil __instance, IPlayer byPlayer, int packetId, Packet_Client packet)
    {
        if (!ResponsiveDiagnostics.BasicEnabled || __instance?.Api?.Side != EnumAppSide.Server)
        {
            return;
        }

        InventoryBase inv = InvRef(__instance);
        ResponsiveDiagnostics.Basic(
            "SERVER packet-done player={0} handlerInv={1} packetId={2}",
            byPlayer?.PlayerName,
            inv?.InventoryID,
            packetId);
    }

    public static void GetSlotUpdatePacketPostfix(InventoryNetworkUtil __instance, IPlayer player, int slotId, Packet_Server __result)
    {
        if (!ResponsiveDiagnostics.BasicEnabled || __instance?.Api?.Side != EnumAppSide.Server)
        {
            return;
        }

        InventoryBase inv = InvRef(__instance);
        ResponsiveDiagnostics.Basic(
            "SERVER packet-build update player={0} inv={1}[{2}] {3}",
            player?.PlayerName,
            inv?.InventoryID,
            slotId,
            InventoryDiagFormat.ServerPacket(__result));
    }

    public static void GetDoubleUpdatePacketPostfix(IPlayer player, string[] invIds, int[] slotIds, Packet_Server __result)
    {
        if (!ResponsiveDiagnostics.BasicEnabled)
        {
            return;
        }

        ResponsiveDiagnostics.Basic(
            "SERVER packet-build double player={0} {1}[{2}] {3}[{4}] {5}",
            player?.PlayerName,
            invIds != null && invIds.Length > 0 ? invIds[0] : "missing",
            slotIds != null && slotIds.Length > 0 ? slotIds[0] : -1,
            invIds != null && invIds.Length > 1 ? invIds[1] : "missing",
            slotIds != null && slotIds.Length > 1 ? slotIds[1] : -1,
            InventoryDiagFormat.ServerPacket(__result));
    }
}
