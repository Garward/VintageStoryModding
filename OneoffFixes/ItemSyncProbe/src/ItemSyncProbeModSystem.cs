using System;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.Client.NoObf;
using Vintagestory.Common;

namespace ItemSyncProbe;

public sealed class ItemSyncProbeModSystem : ModSystem
{
    private const string HarmonyId = "garward.itemsyncprobe";
    private static int patched;
    private Harmony harmony;

    public override void Start(ICoreAPI api)
    {
        ItemSyncProbeLog.RegisterApi(api);

        if (Interlocked.Exchange(ref patched, 1) == 0)
        {
            harmony = new Harmony(HarmonyId);
            harmony.PatchAll(Assembly.GetExecutingAssembly());
        }

        api.Logger.Notification("[ItemSyncProbe] Log-only inventory sync probe active on {0}.", api.Side);
    }

    public override void Dispose()
    {
        harmony?.UnpatchAll(HarmonyId);
        harmony = null;
    }
}

internal static class ItemSyncProbeLog
{
    private const int MaxLogsPerSide = 20000;
    private static ICoreAPI clientApi;
    private static ICoreAPI serverApi;
    private static int clientLogs;
    private static int serverLogs;
    private static long traceSeq;

    public static void RegisterApi(ICoreAPI api)
    {
        if (api.Side == EnumAppSide.Client)
        {
            clientApi = api;
        }
        else
        {
            serverApi = api;
        }
    }

    public static ICoreAPI ApiFor(EnumAppSide side)
    {
        return side == EnumAppSide.Client ? clientApi : serverApi;
    }

    public static void Log(ICoreAPI api, string message, params object[] args)
    {
        if (api == null) return;

        ref int count = ref (api.Side == EnumAppSide.Client ? ref clientLogs : ref serverLogs);
        int current = Interlocked.Increment(ref count);
        if (current > MaxLogsPerSide) return;

        api.Logger.Notification("[ItemSyncProbe:{0}] {1}", api.Side, string.Format(message, args));
    }

    public static void Trace(ICoreAPI api, string evt, string detail)
    {
        long seq = Interlocked.Increment(ref traceSeq);
        Log(api, "TRACE|seq={0}|side={1}|tms={2}|event={3}|{4}",
            seq, api?.Side, Environment.TickCount64, evt, detail);
    }

    public static string SlotSig(InventoryBase inv, int slotId)
    {
        try
        {
            if (inv == null || slotId < 0 || slotId >= inv.Count) return "slot?";
            return StackSig(inv[slotId]?.Itemstack);
        }
        catch
        {
            return "slot?";
        }
    }

    public static string StackSig(ItemStack stack)
    {
        try
        {
            if (stack == null) return "empty";
            Packet_ItemStack packet = StackConverter.ToPacket(stack);
            string code = stack.Collectible?.Code?.ToString() ?? "?";
            return StackSig(packet) + ":" + Escape(code);
        }
        catch
        {
            return "stack?";
        }
    }

    public static string StackSig(Packet_ItemStack stack)
    {
        if (stack == null || stack.ItemClass == -1 || stack.ItemId == 0) return "empty";

        unchecked
        {
            uint hash = 2166136261u;
            byte[] attributes = stack.Attributes;
            if (attributes != null)
            {
                for (int i = 0; i < attributes.Length; i++)
                {
                    hash ^= attributes[i];
                    hash *= 16777619u;
                }
            }

            return stack.ItemClass + ":" + stack.ItemId + ":" + stack.StackSize + ":" + hash;
        }
    }

    public static bool SamePacketSig(string localSig, string packetSig)
    {
        return BaseSig(localSig) == BaseSig(packetSig);
    }

    private static string BaseSig(string sig)
    {
        if (string.IsNullOrEmpty(sig) || sig == "empty" || sig == "slot?" || sig == "stack?") return sig;

        int colons = 0;
        for (int i = 0; i < sig.Length; i++)
        {
            if (sig[i] != ':') continue;
            colons++;
            if (colons == 4)
            {
                return sig.Substring(0, i);
            }
        }

        return sig;
    }


    public static string Escape(string value)
    {
        return (value ?? "").Replace("|", "%7C").Replace("\n", " ").Replace("\r", " ");
    }
}

internal readonly record struct SlotTraceState(string InvId, int SlotId, string Before, long LastChangedBefore);

internal static class ItemSyncProbeUiContext
{
    [ThreadStatic]
    private static string current;

    public static string Current => current;

    public static void Set(string detail)
    {
        current = detail;
    }

    public static void Clear()
    {
        current = null;
    }

    public static string DetailSuffix()
    {
        return string.IsNullOrEmpty(current) ? "" : "|ui=" + ItemSyncProbeLog.Escape(current);
    }
}

[HarmonyPatch(typeof(GuiElementItemSlotGridBase), nameof(GuiElementItemSlotGridBase.SlotClick))]
internal static class Patch_GuiElementItemSlotGridBase_SlotClick
{
    private static readonly FieldInfo InventoryField = AccessTools.Field(typeof(GuiElementItemSlotGridBase), "inventory");

    public static void Prefix(
        GuiElementItemSlotGridBase __instance,
        ICoreClientAPI api,
        int slotId,
        EnumMouseButton mouseButton,
        bool shiftPressed,
        bool ctrlPressed,
        bool altPressed)
    {
        ICoreAPI logApi = ItemSyncProbeLog.ApiFor(EnumAppSide.Client) ?? api;
        IInventory inv = InventoryField.GetValue(__instance) as IInventory;
        InventoryBase invBase = inv as InventoryBase;
        ItemSlot mouseSlot = api?.World?.Player?.InventoryManager?.MouseItemSlot;

        string detail =
            "elem=" + ItemSyncProbeLog.Escape(__instance.GetType().Name)
            + "|inv=" + ItemSyncProbeLog.Escape(inv?.InventoryID)
            + "|slot=" + slotId
            + "|renderedIndex=" + RenderedIndex(__instance, slotId)
            + "|renderedCount=" + (__instance.renderedSlots?.Count ?? -1)
            + "|slotNow=" + ItemSyncProbeLog.StackSig(SlotStack(inv, slotId))
            + "|mouseNow=" + ItemSyncProbeLog.StackSig(mouseSlot?.Itemstack)
            + "|button=" + mouseButton
            + "|shift=" + (shiftPressed ? "1" : "0")
            + "|ctrl=" + (ctrlPressed ? "1" : "0")
            + "|alt=" + (altPressed ? "1" : "0")
            + "|pauseInv=" + ((invBase?.InvNetworkUtil?.PauseInventoryUpdates ?? false) ? "1" : "0")
            + "|pauseMouse=" + ((mouseSlot?.Inventory?.InvNetworkUtil?.PauseInventoryUpdates ?? false) ? "1" : "0");

        ItemSyncProbeUiContext.Set(detail);
        ItemSyncProbeLog.Trace(logApi, "UI_SLOT_CLICK", detail);
    }

    public static void Postfix()
    {
        ItemSyncProbeUiContext.Clear();
    }

    private static int RenderedIndex(GuiElementItemSlotGridBase element, int slotId)
    {
        if (element?.renderedSlots == null) return -1;

        for (int i = 0; i < element.renderedSlots.Count; i++)
        {
            if (element.renderedSlots.GetKeyAtIndex(i) == slotId) return i;
        }

        return -1;
    }

    private static ItemStack SlotStack(IInventory inv, int slotId)
    {
        if (inv == null || slotId < 0 || slotId >= inv.Count) return null;
        return inv[slotId]?.Itemstack;
    }
}

[HarmonyPatch(typeof(ClientMain), nameof(ClientMain.SendPacketClient),
    new[] { typeof(Packet_Client) })]
internal static class Patch_ClientMain_SendPacketClient
{
    public static void Prefix(ClientMain __instance, Packet_Client packetClient)
    {
        ICoreAPI api = ItemSyncProbeLog.ApiFor(EnumAppSide.Client);
        if (api == null || packetClient == null) return;

        IPlayerInventoryManager invMgr = __instance?.player?.InventoryManager;

        if (packetClient.Id == 8 && packetClient.MoveItemstack != null)
        {
            Packet_MoveItemstack p = packetClient.MoveItemstack;
            InventoryBase src = invMgr?.GetInventory(p.SourceInventoryId) as InventoryBase;
            InventoryBase dst = invMgr?.GetInventory(p.TargetInventoryId) as InventoryBase;
            ItemSyncProbeLog.Trace(api, "CLIENT_SEND_MOVE",
                "srcInv=" + ItemSyncProbeLog.Escape(p.SourceInventoryId)
                + "|srcSlot=" + p.SourceSlot
                + "|srcLast=" + p.SourceLastChanged
                + "|srcNow=" + ItemSyncProbeLog.SlotSig(src, p.SourceSlot)
                + "|dstInv=" + ItemSyncProbeLog.Escape(p.TargetInventoryId)
                + "|dstSlot=" + p.TargetSlot
                + "|dstLast=" + p.TargetLastChanged
                + "|dstNow=" + ItemSyncProbeLog.SlotSig(dst, p.TargetSlot)
                + "|qty=" + p.Quantity
                + "|button=" + p.MouseButton
                + "|mods=" + p.Modifiers
                + "|priority=" + p.Priority
                + ItemSyncProbeUiContext.DetailSuffix());
        }
        else if (packetClient.Id == 7 && packetClient.ActivateInventorySlot != null)
        {
            Packet_ActivateInventorySlot p = packetClient.ActivateInventorySlot;
            InventoryBase inv = invMgr?.GetInventory(p.TargetInventoryId) as InventoryBase;
            ItemSyncProbeLog.Trace(api, "CLIENT_SEND_ACTIVATE",
                "inv=" + ItemSyncProbeLog.Escape(p.TargetInventoryId)
                + "|slot=" + p.TargetSlot
                + "|last=" + p.TargetLastChanged
                + "|slotNow=" + ItemSyncProbeLog.SlotSig(inv, p.TargetSlot)
                + "|button=" + p.MouseButton
                + "|mods=" + p.Modifiers
                + "|priority=" + p.Priority
                + "|dir=" + p.Dir
                + ItemSyncProbeUiContext.DetailSuffix());
        }
        else if (packetClient.Id == 9 && packetClient.Flipitemstacks != null)
        {
            Packet_FlipItemstacks p = packetClient.Flipitemstacks;
            InventoryBase src = invMgr?.GetInventory(p.SourceInventoryId) as InventoryBase;
            InventoryBase dst = invMgr?.GetInventory(p.TargetInventoryId) as InventoryBase;
            ItemSyncProbeLog.Trace(api, "CLIENT_SEND_FLIP",
                "srcInv=" + ItemSyncProbeLog.Escape(p.SourceInventoryId)
                + "|srcSlot=" + p.SourceSlot
                + "|srcLast=" + p.SourceLastChanged
                + "|srcNow=" + ItemSyncProbeLog.SlotSig(src, p.SourceSlot)
                + "|dstInv=" + ItemSyncProbeLog.Escape(p.TargetInventoryId)
                + "|dstSlot=" + p.TargetSlot
                + "|dstLast=" + p.TargetLastChanged
                + "|dstNow=" + ItemSyncProbeLog.SlotSig(dst, p.TargetSlot)
                + ItemSyncProbeUiContext.DetailSuffix());
        }
    }
}

[HarmonyPatch(typeof(InventoryBase), nameof(InventoryBase.DidModifyItemSlot))]
internal static class Patch_InventoryBase_DidModifyItemSlot
{
    public static void Prefix(InventoryBase __instance, ItemSlot slot, out SlotTraceState __state)
    {
        int slotId = SafeSlotId(__instance, slot);
        __state = new SlotTraceState(
            __instance.InventoryID,
            slotId,
            ItemSyncProbeLog.StackSig(slot?.Itemstack),
            __instance.LastChanged);
    }

    public static void Postfix(InventoryBase __instance, ItemSlot slot, SlotTraceState __state)
    {
        ICoreAPI api = __instance.Api ?? ItemSyncProbeLog.ApiFor(EnumAppSide.Server) ?? ItemSyncProbeLog.ApiFor(EnumAppSide.Client);
        if (api == null) return;

        long after = __instance.LastChanged;
        int slotId = SafeSlotId(__instance, slot);
        string afterSig = ItemSyncProbeLog.StackSig(slot?.Itemstack);
        string changed = __state.Before == afterSig ? "0" : "1";
        string lastChangedMoved = __state.LastChangedBefore == after ? "0" : "1";

        ItemSyncProbeLog.Trace(api, "SLOT_MUT",
            "inv=" + ItemSyncProbeLog.Escape(__instance.InventoryID)
            + "|slot=" + slotId
            + "|before=" + __state.Before
            + "|after=" + afterSig
            + "|stackChanged=" + changed
            + "|lastBefore=" + __state.LastChangedBefore
            + "|lastAfter=" + after
            + "|lastMoved=" + lastChangedMoved);
    }

    private static int SafeSlotId(InventoryBase inv, ItemSlot slot)
    {
        try { return inv.GetSlotId(slot); } catch { return -1; }
    }

}

[HarmonyPatch(typeof(InventoryNetworkUtil), nameof(InventoryNetworkUtil.HandleClientPacket),
    new[] { typeof(IPlayer), typeof(int), typeof(Packet_Client) })]
internal static class Patch_InventoryNetworkUtil_HandleClientPacket
{
    private static readonly FieldInfo InvField = AccessTools.Field(typeof(InventoryNetworkUtil), "inv");

    public static void Prefix(InventoryNetworkUtil __instance, IPlayer byPlayer, int packetId, Packet_Client packet)
    {
        ICoreAPI api = __instance.Api;
        if (api?.Side != EnumAppSide.Server || packet == null) return;

        InventoryBase inv = InvField.GetValue(__instance) as InventoryBase;

        if (packetId == 8 && packet.MoveItemstack != null)
        {
            Packet_MoveItemstack p = packet.MoveItemstack;
            InventoryBase src = byPlayer?.InventoryManager?.GetInventory(p.SourceInventoryId) as InventoryBase;
            InventoryBase dst = byPlayer?.InventoryManager?.GetInventory(p.TargetInventoryId) as InventoryBase;
            ItemSyncProbeLog.Trace(api, "SERVER_RECV_MOVE",
                "player=" + ItemSyncProbeLog.Escape(byPlayer?.PlayerName)
                + "|utilInv=" + ItemSyncProbeLog.Escape(inv?.InventoryID)
                + "|srcInv=" + ItemSyncProbeLog.Escape(p.SourceInventoryId)
                + "|srcSlot=" + p.SourceSlot
                + "|srcLast=" + p.SourceLastChanged
                + "|srcNow=" + ItemSyncProbeLog.SlotSig(src, p.SourceSlot)
                + "|dstInv=" + ItemSyncProbeLog.Escape(p.TargetInventoryId)
                + "|dstSlot=" + p.TargetSlot
                + "|dstLast=" + p.TargetLastChanged
                + "|dstNow=" + ItemSyncProbeLog.SlotSig(dst, p.TargetSlot)
                + "|qty=" + p.Quantity
                + "|button=" + p.MouseButton
                + "|mods=" + p.Modifiers
                + "|priority=" + p.Priority);
        }
        else if (packetId == 7 && packet.ActivateInventorySlot != null)
        {
            Packet_ActivateInventorySlot p = packet.ActivateInventorySlot;
            InventoryBase target = byPlayer?.InventoryManager?.GetInventory(p.TargetInventoryId) as InventoryBase;
            ItemSyncProbeLog.Trace(api, "SERVER_RECV_ACTIVATE",
                "player=" + ItemSyncProbeLog.Escape(byPlayer?.PlayerName)
                + "|utilInv=" + ItemSyncProbeLog.Escape(inv?.InventoryID)
                + "|inv=" + ItemSyncProbeLog.Escape(p.TargetInventoryId)
                + "|slot=" + p.TargetSlot
                + "|last=" + p.TargetLastChanged
                + "|slotNow=" + ItemSyncProbeLog.SlotSig(target, p.TargetSlot)
                + "|button=" + p.MouseButton
                + "|mods=" + p.Modifiers
                + "|priority=" + p.Priority
                + "|dir=" + p.Dir);
        }
        else if (packetId == 9 && packet.Flipitemstacks != null)
        {
            Packet_FlipItemstacks p = packet.Flipitemstacks;
            InventoryBase src = byPlayer?.InventoryManager?.GetInventory(p.SourceInventoryId) as InventoryBase;
            InventoryBase dst = byPlayer?.InventoryManager?.GetInventory(p.TargetInventoryId) as InventoryBase;
            ItemSyncProbeLog.Trace(api, "SERVER_RECV_FLIP",
                "player=" + ItemSyncProbeLog.Escape(byPlayer?.PlayerName)
                + "|utilInv=" + ItemSyncProbeLog.Escape(inv?.InventoryID)
                + "|srcInv=" + ItemSyncProbeLog.Escape(p.SourceInventoryId)
                + "|srcSlot=" + p.SourceSlot
                + "|srcLast=" + p.SourceLastChanged
                + "|srcNow=" + ItemSyncProbeLog.SlotSig(src, p.SourceSlot)
                + "|dstInv=" + ItemSyncProbeLog.Escape(p.TargetInventoryId)
                + "|dstSlot=" + p.TargetSlot
                + "|dstLast=" + p.TargetLastChanged
                + "|dstNow=" + ItemSyncProbeLog.SlotSig(dst, p.TargetSlot));
        }
    }
}

[HarmonyPatch(typeof(InventoryNetworkUtil), "SendDirtyInventoryContents")]
internal static class Patch_InventoryNetworkUtil_SendDirtyInventoryContents
{
    public static void Prefix(
        InventoryNetworkUtil __instance,
        IPlayer owningPlayer,
        string inventoryId,
        long lastChangedClient)
    {
        ICoreAPI api = __instance.Api;
        if (api?.Side != EnumAppSide.Server) return;

        InventoryBase targetInv = owningPlayer?.InventoryManager?.GetInventory(inventoryId) as InventoryBase;
        ItemSyncProbeLog.Trace(api, "SERVER_DIRTY_CHECK",
            "player=" + ItemSyncProbeLog.Escape(owningPlayer?.PlayerName)
            + "|inv=" + ItemSyncProbeLog.Escape(inventoryId)
            + "|serverLast=" + (targetInv?.LastChanged ?? -1)
            + "|clientLast=" + lastChangedClient
            + "|wouldResync=" + (targetInv != null && targetInv.LastChanged > lastChangedClient ? "1" : "0"));
    }
}

[HarmonyPatch(typeof(InventoryNetworkUtil), nameof(InventoryNetworkUtil.UpdateFromPacket),
    new[] { typeof(IWorldAccessor), typeof(Packet_InventoryUpdate) })]
internal static class Patch_InventoryNetworkUtil_UpdateFromPacket_Single
{
    private static readonly FieldInfo InvField = AccessTools.Field(typeof(InventoryNetworkUtil), "inv");

    public static void Prefix(InventoryNetworkUtil __instance, Packet_InventoryUpdate packet, out SlotTraceState __state)
    {
        __state = default;
        ICoreAPI api = __instance.Api;
        if (api?.Side != EnumAppSide.Client || packet == null) return;

        InventoryBase inv = InvField.GetValue(__instance) as InventoryBase;
        __state = new SlotTraceState(
            inv?.InventoryID,
            packet.SlotId,
            ItemSyncProbeLog.SlotSig(inv, packet.SlotId),
            inv?.LastChanged ?? -1);
        string packetSig = ItemSyncProbeLog.StackSig(packet.ItemStack);

        ItemSyncProbeLog.Trace(api, "CLIENT_APPLY_UPDATE_BEGIN",
            "utilInv=" + ItemSyncProbeLog.Escape(inv?.InventoryID)
            + "|packetInv=" + ItemSyncProbeLog.Escape(packet.InventoryId)
            + "|slot=" + packet.SlotId
            + "|before=" + __state.Before
            + "|packet=" + packetSig
            + "|differs=" + (ItemSyncProbeLog.SamePacketSig(__state.Before, packetSig) ? "0" : "1"));
    }

    public static void Postfix(InventoryNetworkUtil __instance, Packet_InventoryUpdate packet, SlotTraceState __state)
    {
        ICoreAPI api = __instance.Api;
        if (api?.Side != EnumAppSide.Client || packet == null) return;

        InventoryBase inv = InvField.GetValue(__instance) as InventoryBase;
        string after = ItemSyncProbeLog.SlotSig(inv, packet.SlotId);
        string packetSig = ItemSyncProbeLog.StackSig(packet.ItemStack);
        ItemSyncProbeLog.Trace(api, "CLIENT_APPLY_UPDATE_END",
            "utilInv=" + ItemSyncProbeLog.Escape(inv?.InventoryID)
            + "|packetInv=" + ItemSyncProbeLog.Escape(packet.InventoryId)
            + "|slot=" + packet.SlotId
            + "|before=" + __state.Before
            + "|packet=" + packetSig
            + "|after=" + after
            + "|changed=" + (__state.Before == after ? "0" : "1")
            + "|appliedPacket=" + (ItemSyncProbeLog.SamePacketSig(after, packetSig) ? "1" : "0"));
    }
}

[HarmonyPatch(typeof(InventoryNetworkUtil), nameof(InventoryNetworkUtil.UpdateFromPacket),
    new[] { typeof(IWorldAccessor), typeof(Packet_InventoryDoubleUpdate) })]
internal static class Patch_InventoryNetworkUtil_UpdateFromPacket_Double
{
    private static readonly FieldInfo InvField = AccessTools.Field(typeof(InventoryNetworkUtil), "inv");

    public static void Prefix(InventoryNetworkUtil __instance, Packet_InventoryDoubleUpdate packet)
    {
        ICoreAPI api = __instance.Api;
        if (api?.Side != EnumAppSide.Client || packet == null) return;

        InventoryBase inv = InvField.GetValue(__instance) as InventoryBase;
        string before1 = inv?.InventoryID == packet.InventoryId1 ? ItemSyncProbeLog.SlotSig(inv, packet.SlotId1) : "other";
        string before2 = inv?.InventoryID == packet.InventoryId2 ? ItemSyncProbeLog.SlotSig(inv, packet.SlotId2) : "other";
        ItemSyncProbeLog.Trace(api, "CLIENT_APPLY_DOUBLE_BEGIN",
            "utilInv=" + ItemSyncProbeLog.Escape(inv?.InventoryID)
            + "|inv1=" + ItemSyncProbeLog.Escape(packet.InventoryId1)
            + "|slot1=" + packet.SlotId1
            + "|before1=" + before1
            + "|packet1=" + ItemSyncProbeLog.StackSig(packet.ItemStack1)
            + "|inv2=" + ItemSyncProbeLog.Escape(packet.InventoryId2)
            + "|slot2=" + packet.SlotId2
            + "|before2=" + before2
            + "|packet2=" + ItemSyncProbeLog.StackSig(packet.ItemStack2));
    }
}
