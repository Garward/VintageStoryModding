using HarmonyLib;
using ResponsiveVS.Client.Preview;
using ResponsiveVS.Diagnostics;
using ResponsiveVS.Transactions;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.Common;

namespace ResponsiveVS.Client.Patches;

public static class Patch_ClientInventoryNetworkObserve
{
    private static readonly AccessTools.FieldRef<InventoryNetworkUtil, InventoryBase> InvRef =
        AccessTools.FieldRefAccess<InventoryNetworkUtil, InventoryBase>("inv");

    public static void GetActivateSlotPacketPostfix(InventoryNetworkUtil __instance, int slotId, ItemStackMoveOperation op, object __result)
    {
        if (!ResponsiveDiagnostics.BasicEnabled || __instance?.Api?.Side != EnumAppSide.Client)
        {
            return;
        }

        InventoryBase inv = InvRef(__instance);
        ResponsiveDiagnostics.Basic(
            "CLIENT packet-build activate inv={0}[{1}] button={2} shift={3} ctrl={4} alt={5} requested={6} last={7} packet={8}",
            inv?.InventoryID,
            slotId,
            op.MouseButton,
            op.ShiftDown,
            op.CtrlDown,
            op.AltDown,
            op.RequestedQuantity,
            inv?.LastChanged ?? -1,
            __result is Packet_Client packet ? InventoryDiagFormat.ClientPacket(packet.Id, packet) : __result?.GetType().Name ?? "null");
    }

    public static void GetFlipSlotsPacketPostfix(
        InventoryNetworkUtil __instance,
        IInventory sourceInv,
        int sourceSlotId,
        int targetSlotId,
        object __result)
    {
        if (!ResponsiveDiagnostics.BasicEnabled || __instance?.Api?.Side != EnumAppSide.Client)
        {
            return;
        }

        InventoryBase targetInv = InvRef(__instance);
        ResponsiveDiagnostics.Basic(
            "CLIENT packet-build flip source={0}[{1}] target={2}[{3}] packet={4}",
            sourceInv?.InventoryID,
            sourceSlotId,
            targetInv?.InventoryID,
            targetSlotId,
            __result is Packet_Client packet ? InventoryDiagFormat.ClientPacket(packet.Id, packet) : __result?.GetType().Name ?? "null");
    }

    public static void UpdateFromPacketPrefix(InventoryNetworkUtil __instance, Packet_InventoryUpdate packet, out UpdateObserveState __state)
    {
        if (__instance is PlayerInventoryNetworkUtil)
        {
            __state = null;
            return;
        }

        __state = CaptureUpdate(__instance, packet);
    }

    public static void UpdateFromPacketPostfix(UpdateObserveState __state)
    {
        if (__state != null)
        {
            ClientInventoryPreviewStore.ReconcileServerApplied(
                new SlotKey(__state.InventoryId, __state.SlotId),
                SafeSlot(__state.Inventory, __state.SlotId),
                __state.Inventory?.Api?.World);
        }

        FlushUpdate("CLIENT update-apply", __state);
    }

    public static void PlayerUpdateFromPacketPrefix(PlayerInventoryNetworkUtil __instance, Packet_InventoryUpdate packet, out UpdateObserveState __state)
    {
        __state = CaptureUpdate(__instance, packet);
    }

    public static void PlayerUpdateFromPacketPostfix(UpdateObserveState __state)
    {
        if (__state != null)
        {
            ClientInventoryPreviewStore.ReconcileServerApplied(
                new SlotKey(__state.InventoryId, __state.SlotId),
                SafeSlot(__state.Inventory, __state.SlotId),
                __state.Inventory?.Api?.World);
        }

        FlushUpdate("CLIENT player-update-apply", __state);
    }

    public static void DoubleUpdateFromPacketPrefix(InventoryNetworkUtil __instance, Packet_InventoryDoubleUpdate packet, out DoubleUpdateObserveState __state)
    {
        if (!NeedsClientUpdateState(__instance))
        {
            __state = null;
            return;
        }

        InventoryBase inv = InvRef(__instance);
        __state = new DoubleUpdateObserveState
        {
            Inventory = inv,
            InventoryId = inv?.InventoryID,
            Packet = packet,
            Slot1Before = packet != null && inv?.InventoryID == packet.InventoryId1 ? InventoryDiagFormat.Slot(SafeSlot(inv, packet.SlotId1)) : null,
            Slot2Before = packet != null && inv?.InventoryID == packet.InventoryId2 ? InventoryDiagFormat.Slot(SafeSlot(inv, packet.SlotId2)) : null
        };
    }

    public static void DoubleUpdateFromPacketPostfix(DoubleUpdateObserveState __state)
    {
        if (__state == null)
        {
            return;
        }

        Packet_InventoryDoubleUpdate packet = __state.Packet;
        if (packet != null)
        {
            ReconcilePacketSlot(__state.Inventory, packet.InventoryId1, packet.SlotId1);
            ReconcilePacketSlot(__state.Inventory, packet.InventoryId2, packet.SlotId2);
        }

        if (!ResponsiveDiagnostics.BasicEnabled)
        {
            return;
        }

        ResponsiveDiagnostics.Basic(
            "CLIENT double-update-apply inv={0} pkt=inv1:{1}[{2}]={3} inv2:{4}[{5}]={6} before1={7} after1={8} before2={9} after2={10}",
            __state.InventoryId,
            packet?.InventoryId1,
            packet?.SlotId1 ?? -1,
            InventoryDiagFormat.PacketStack(packet?.ItemStack1),
            packet?.InventoryId2,
            packet?.SlotId2 ?? -1,
            InventoryDiagFormat.PacketStack(packet?.ItemStack2),
            __state.Slot1Before ?? "n/a",
            packet != null && __state.Inventory?.InventoryID == packet.InventoryId1 ? InventoryDiagFormat.Slot(SafeSlot(__state.Inventory, packet.SlotId1)) : "n/a",
            __state.Slot2Before ?? "n/a",
            packet != null && __state.Inventory?.InventoryID == packet.InventoryId2 ? InventoryDiagFormat.Slot(SafeSlot(__state.Inventory, packet.SlotId2)) : "n/a");
    }

    public static void ContentsUpdateFromPacketPrefix(InventoryNetworkUtil __instance, Packet_InventoryContents packet, out ContentsObserveState __state)
    {
        if (!NeedsClientUpdateState(__instance))
        {
            __state = null;
            return;
        }

        InventoryBase inv = InvRef(__instance);
        __state = new ContentsObserveState
        {
            Inventory = inv,
            InventoryId = inv?.InventoryID,
            PacketInventoryId = packet?.InventoryId,
            Count = packet?.ItemstacksCount ?? 0
        };
    }

    public static void ContentsUpdateFromPacketPostfix(ContentsObserveState __state)
    {
        if (__state == null || !ResponsiveDiagnostics.BasicEnabled)
        {
            if (__state != null)
            {
                InventoryBase inventory = ResolveInventory(__state.Inventory, __state.PacketInventoryId);
                ClientInventoryPreviewStore.ReconcileInventoryServerApplied(
                    __state.PacketInventoryId,
                    inventory,
                    inventory?.Api?.World ?? __state.Inventory?.Api?.World);
            }
            return;
        }

        InventoryBase packetInventory = ResolveInventory(__state.Inventory, __state.PacketInventoryId);

        ClientInventoryPreviewStore.ReconcileInventoryServerApplied(
            __state.PacketInventoryId,
            packetInventory,
            packetInventory?.Api?.World ?? __state.Inventory?.Api?.World);

        ResponsiveDiagnostics.Basic(
            "CLIENT contents-apply inv={0} packetInv={1} count={2}",
            __state.InventoryId,
            __state.PacketInventoryId,
            __state.Count);
    }

    private static UpdateObserveState CaptureUpdate(InventoryNetworkUtil util, Packet_InventoryUpdate packet)
    {
        if (!NeedsClientUpdateState(util))
        {
            return null;
        }

        InventoryBase inv = InvRef(util);
        ItemSlot slot = SafeSlot(inv, packet?.SlotId ?? -1);
        return new UpdateObserveState
        {
            Inventory = inv,
            InventoryId = inv?.InventoryID,
            SlotId = packet?.SlotId ?? -1,
            Incoming = InventoryDiagFormat.PacketStack(packet?.ItemStack),
            Before = InventoryDiagFormat.Slot(slot)
        };
    }

    private static bool NeedsClientUpdateState(InventoryNetworkUtil util)
    {
        return util?.Api?.Side == EnumAppSide.Client && (ResponsiveDiagnostics.BasicEnabled || ClientInventoryPreviewStore.HasAny);
    }

    private static void ReconcilePacketSlot(InventoryBase contextInventory, string inventoryId, int slotId)
    {
        if (string.IsNullOrEmpty(inventoryId))
        {
            return;
        }

        InventoryBase inventory = ResolveInventory(contextInventory, inventoryId);
        if (inventory != null)
        {
            ClientInventoryPreviewStore.ReconcileServerApplied(
                new SlotKey(inventoryId, slotId),
                SafeSlot(inventory, slotId),
                inventory.Api?.World);
            return;
        }

        ClientInventoryPreviewStore.Remove(new SlotKey(inventoryId, slotId));
    }

    private static InventoryBase ResolveInventory(InventoryBase contextInventory, string inventoryId)
    {
        if (contextInventory == null || string.IsNullOrEmpty(inventoryId))
        {
            return null;
        }

        if (string.Equals(contextInventory.InventoryID, inventoryId, System.StringComparison.Ordinal))
        {
            return contextInventory;
        }

        if (contextInventory.Api is ICoreClientAPI capi
            && capi.World?.Player?.InventoryManager?.GetInventory(inventoryId) is InventoryBase playerInventory)
        {
            return playerInventory;
        }

        return null;
    }

    private static void FlushUpdate(string label, UpdateObserveState state)
    {
        if (state == null || !ResponsiveDiagnostics.BasicEnabled)
        {
            return;
        }

        ResponsiveDiagnostics.Basic(
            "{0} inv={1}[{2}] incoming={3} local={4}->{5}",
            label,
            state.InventoryId,
            state.SlotId,
            state.Incoming,
            state.Before,
            InventoryDiagFormat.Slot(SafeSlot(state.Inventory, state.SlotId)));
    }

    private static ItemSlot SafeSlot(InventoryBase inv, int slotId)
    {
        if (inv == null || slotId < 0 || slotId >= inv.Count)
        {
            return null;
        }

        return inv[slotId];
    }

    public sealed class UpdateObserveState
    {
        public InventoryBase Inventory;
        public string InventoryId;
        public int SlotId;
        public string Incoming;
        public string Before;
    }

    public sealed class DoubleUpdateObserveState
    {
        public InventoryBase Inventory;
        public string InventoryId;
        public Packet_InventoryDoubleUpdate Packet;
        public string Slot1Before;
        public string Slot2Before;
    }

    public sealed class ContentsObserveState
    {
        public InventoryBase Inventory;
        public string InventoryId;
        public string PacketInventoryId;
        public int Count;
    }
}
