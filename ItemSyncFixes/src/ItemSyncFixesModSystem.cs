using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.Common;
using Vintagestory.Server;

namespace ItemSyncFixes;

public sealed class ItemSyncClientFixModSystem : ModSystem
{
    private const string HarmonyId = "garward.itemsyncfixes.client";
    private Harmony harmony;

    public override bool ShouldLoad(EnumAppSide side) => side == EnumAppSide.Client;

    public override void StartClientSide(ICoreClientAPI api)
    {
        SyncDiagnostics.RegisterCommand(api);

        harmony = new Harmony(HarmonyId);
        harmony.Patch(
            AccessTools.PropertySetter(typeof(InventoryNetworkUtil), nameof(InventoryNetworkUtil.PauseInventoryUpdates)),
            prefix: new HarmonyMethod(typeof(Patch_InventoryNetworkUtil_SetPauseInventoryUpdates), nameof(Patch_InventoryNetworkUtil_SetPauseInventoryUpdates.Prefix)));
        harmony.Patch(
            AccessTools.Method(typeof(InventoryNetworkUtil), nameof(InventoryNetworkUtil.UpdateFromPacket), new[] { typeof(IWorldAccessor), typeof(Packet_InventoryUpdate) }),
            prefix: new HarmonyMethod(typeof(Patch_InventoryNetworkUtil_UpdateFromPacket), nameof(Patch_InventoryNetworkUtil_UpdateFromPacket.Prefix)),
            postfix: new HarmonyMethod(typeof(Patch_InventoryNetworkUtil_UpdateFromPacket), nameof(Patch_InventoryNetworkUtil_UpdateFromPacket.Postfix)));
        harmony.Patch(
            AccessTools.Method(typeof(InventoryNetworkUtil), nameof(InventoryNetworkUtil.UpdateFromPacket), new[] { typeof(IWorldAccessor), typeof(Packet_InventoryDoubleUpdate) }),
            prefix: new HarmonyMethod(typeof(Patch_InventoryNetworkUtil_UpdateFromPacket_Double), nameof(Patch_InventoryNetworkUtil_UpdateFromPacket_Double.Prefix)));
        harmony.Patch(
            AccessTools.Method(typeof(InventoryNetworkUtil), nameof(InventoryNetworkUtil.UpdateFromPacket), new[] { typeof(IWorldAccessor), typeof(Packet_InventoryContents) }),
            prefix: new HarmonyMethod(typeof(Patch_InventoryNetworkUtil_UpdateFromPacket_Contents), nameof(Patch_InventoryNetworkUtil_UpdateFromPacket_Contents.Prefix)));
        harmony.Patch(
            AccessTools.Method(typeof(PlayerInventoryNetworkUtil), nameof(PlayerInventoryNetworkUtil.UpdateFromPacket), new[] { typeof(IWorldAccessor), typeof(Packet_InventoryUpdate) }),
            prefix: new HarmonyMethod(typeof(Patch_PlayerInventoryNetworkUtil_UpdateFromPacket), nameof(Patch_PlayerInventoryNetworkUtil_UpdateFromPacket.Prefix)));
        harmony.Patch(
            AccessTools.Method(typeof(InventoryBase), nameof(InventoryBase.SlotsFromTreeAttributes), new[] { typeof(ITreeAttribute), typeof(ItemSlot[]), typeof(List<ItemSlot>) }),
            prefix: new HarmonyMethod(typeof(Patch_InventoryBase_SlotsFromTreeAttributes), nameof(Patch_InventoryBase_SlotsFromTreeAttributes.Prefix)));
        harmony.Patch(
            AccessTools.Method(typeof(GuiElementItemSlotGridBase), nameof(GuiElementItemSlotGridBase.SlotClick), new[] { typeof(ICoreClientAPI), typeof(int), typeof(EnumMouseButton), typeof(bool), typeof(bool), typeof(bool) }),
            prefix: new HarmonyMethod(typeof(Patch_GuiElementItemSlotGridBase_SlotClick), nameof(Patch_GuiElementItemSlotGridBase_SlotClick.Prefix)),
            postfix: new HarmonyMethod(typeof(Patch_GuiElementItemSlotGridBase_SlotClick), nameof(Patch_GuiElementItemSlotGridBase_SlotClick.Postfix)));
        harmony.Patch(
            AccessTools.Method(typeof(GuiElementItemSlotGridBase), nameof(GuiElementItemSlotGridBase.OnMouseDownOnElement), new[] { typeof(ICoreClientAPI), typeof(MouseEvent) }),
            postfix: new HarmonyMethod(typeof(Patch_GuiElementItemSlotGridBase_OnMouseDownOnElement), nameof(Patch_GuiElementItemSlotGridBase_OnMouseDownOnElement.Postfix)));
        ClientDoubleUpdatePatchInstaller.Patch(harmony);
        CraftingPatchInstaller.Patch(harmony);

        api.Logger.Notification("[ItemSyncFixes] Client stale queued inventory update coalescing active.");
    }

    public override void Dispose()
    {
        harmony?.UnpatchAll(HarmonyId);
        harmony = null;
    }
}

public sealed class ItemSyncServerFixModSystem : ModSystem
{
    private const string HarmonyId = "garward.itemsyncfixes.server";
    private Harmony harmony;

    public override bool ShouldLoad(EnumAppSide side) => side == EnumAppSide.Server;

    public override void StartServerSide(ICoreServerAPI api)
    {
        SyncDiagnostics.RegisterCommand(api);
        EchoSuppressor.Api = api;

        harmony = new Harmony(HarmonyId);
        harmony.Patch(
            AccessTools.Method(typeof(InventoryNetworkUtil), nameof(InventoryNetworkUtil.HandleClientPacket), new[] { typeof(IPlayer), typeof(int), typeof(Packet_Client) }),
            prefix: new HarmonyMethod(typeof(Patch_InventoryNetworkUtil_HandleClientPacket), nameof(Patch_InventoryNetworkUtil_HandleClientPacket.Prefix)),
            postfix: new HarmonyMethod(typeof(Patch_InventoryNetworkUtil_HandleClientPacket), nameof(Patch_InventoryNetworkUtil_HandleClientPacket.Postfix)));
        harmony.Patch(
            AccessTools.Method(typeof(ServerMain), nameof(ServerMain.SendPacket), new[] { typeof(int), typeof(Packet_Server) }),
            prefix: new HarmonyMethod(typeof(Patch_ServerMain_SendPacket), nameof(Patch_ServerMain_SendPacket.Prefix)));
        CraftingPatchInstaller.Patch(harmony);

        api.Logger.Notification("[ItemSyncFixes] Server crafting output ghost mitigation active.");
    }

    public override void Dispose()
    {
        harmony?.UnpatchAll(HarmonyId);
        harmony = null;
        EchoSuppressor.Clear();
    }

}

internal static class CraftingPatchInstaller
{
    public static void Patch(Harmony harmony)
    {
        PatchIfFound(
            harmony,
            AccessTools.Method(typeof(InventoryCraftingGrid), nameof(InventoryCraftingGrid.ActivateSlot), new[] { typeof(int), typeof(ItemSlot), typeof(ItemStackMoveOperation).MakeByRefType() }),
            prefix: new HarmonyMethod(typeof(Patch_InventoryCraftingGrid_ActivateSlot), nameof(Patch_InventoryCraftingGrid_ActivateSlot.Prefix)));
        PatchIfFound(
            harmony,
            AccessTools.Method(typeof(ItemSlotCraftingOutput), nameof(ItemSlotCraftingOutput.TryPutInto), new[] { typeof(ItemSlot), typeof(ItemStackMoveOperation).MakeByRefType() }),
            prefix: new HarmonyMethod(typeof(Patch_ItemSlotCraftingOutput_TryPutInto), nameof(Patch_ItemSlotCraftingOutput_TryPutInto.Prefix)));
        PatchIfFound(
            harmony,
            AccessTools.Method(typeof(ItemSlotCraftingOutput), "FlipWith"),
            prefix: new HarmonyMethod(typeof(Patch_ItemSlotCraftingOutput_FlipWith), nameof(Patch_ItemSlotCraftingOutput_FlipWith.Prefix)));
        PatchIfFound(
            harmony,
            AccessTools.Method(typeof(InventoryCraftingGrid), "FindMatchingRecipe"),
            postfix: new HarmonyMethod(typeof(Patch_InventoryCraftingGrid_FindMatchingRecipe), nameof(Patch_InventoryCraftingGrid_FindMatchingRecipe.Postfix)));
    }

    private static void PatchIfFound(Harmony harmony, MethodBase target, HarmonyMethod prefix = null, HarmonyMethod postfix = null)
    {
        if (target != null)
        {
            harmony.Patch(target, prefix: prefix, postfix: postfix);
        }
    }
}

internal static class ClientDoubleUpdatePatchInstaller
{
    public static void Patch(Harmony harmony)
    {
        Type generalPacketHandlerType = AccessTools.TypeByName("Vintagestory.Client.NoObf.GeneralPacketHandler");
        MethodInfo target = generalPacketHandlerType == null
            ? null
            : AccessTools.Method(generalPacketHandlerType, "HandleInventoryDoubleUpdate");

        if (target != null)
        {
            harmony.Patch(target, prefix: new HarmonyMethod(typeof(Patch_GeneralPacketHandler_HandleInventoryDoubleUpdate), nameof(Patch_GeneralPacketHandler_HandleInventoryDoubleUpdate.Prefix)));
        }
    }
}

internal readonly record struct QueuedSlotUpdate(Packet_InventoryUpdate Packet, int LastIndex);

internal readonly record struct ClientPredictionState(bool Track, string TargetBefore, string MouseBefore, long DiagnosticClickId);

internal enum ClientPredictionDirection
{
    Unknown,
    Increasing,
    Decreasing
}

internal readonly record struct ClientStackInfo(string Fingerprint, string Identity, int StackSize, bool Empty);

internal sealed class ClientPredictedSlotUpdate
{
    public string Fingerprint;
    public long CreatedMs;
}

internal sealed class ClientLatestSlotPrediction
{
    public string Identity;
    public int StackSize;
    public ClientPredictionDirection Direction;
    public long CreatedMs;
}

internal sealed class DiagnosticClickTrace
{
    public long Id;
    public long CreatedMs;
    public string InventoryId;
    public int SlotId;
    public string MouseButton;
    public string Modifiers;
    public string TargetBefore;
    public string TargetAfter;
    public string MouseBefore;
    public string MouseAfter;
}

internal static class SyncDiagnostics
{
    private const long RecentMs = 4000;
    private const int MaxClicks = 256;
    private static readonly object LockObj = new();
    private static readonly Dictionary<string, DiagnosticClickTrace> RecentBySlot = new();
    private static readonly Queue<string> RecentSlotOrder = new();
    private static long NextClickId;

    public static bool Enabled;

    public static void RegisterCommand(ICoreAPI api)
    {
        api.ChatCommands
            .Create("isfdiag")
            .WithDescription("Toggle ItemSyncFixes inventory sync diagnostics")
            .RequiresPrivilege(Privilege.chat)
            .WithArgs(api.ChatCommands.Parsers.OptionalWord("state"))
            .HandleWith(args =>
            {
                string state = ((string)args[0] ?? "status").ToLowerInvariant();
                if (state == "on" || state == "1" || state == "true" || state == "enable" || state == "enabled")
                {
                    Enabled = true;
                    return TextCommandResult.Success("ItemSyncFixes diagnostics enabled.");
                }

                if (state == "off" || state == "0" || state == "false" || state == "disable" || state == "disabled")
                {
                    Enabled = false;
                    Clear();
                    return TextCommandResult.Success("ItemSyncFixes diagnostics disabled.");
                }

                return TextCommandResult.Success("ItemSyncFixes diagnostics are " + (Enabled ? "enabled." : "disabled."));
            });
    }

    public static long BeginClick(
        ICoreAPI api,
        InventoryBase targetInv,
        int slotId,
        EnumMouseButton mouseButton,
        bool shiftPressed,
        bool ctrlPressed,
        bool altPressed,
        string targetBefore,
        string mouseBefore)
    {
        if (!Enabled || targetInv == null) return 0;

        long id = System.Threading.Interlocked.Increment(ref NextClickId);
        string modifiers = (shiftPressed ? "S" : "-") + (ctrlPressed ? "C" : "-") + (altPressed ? "A" : "-");

        Log(api,
            "CLICK begin #{0} {1}[{2}] button={3} mods={4} targetBefore={5} mouseBefore={6}",
            id,
            targetInv.InventoryID ?? "?",
            slotId,
            mouseButton,
            modifiers,
            targetBefore,
            mouseBefore);

        Remember(new DiagnosticClickTrace
        {
            Id = id,
            CreatedMs = Environment.TickCount64,
            InventoryId = targetInv.InventoryID ?? "?",
            SlotId = slotId,
            MouseButton = mouseButton.ToString(),
            Modifiers = modifiers,
            TargetBefore = targetBefore,
            MouseBefore = mouseBefore
        });

        return id;
    }

    public static void EndClick(
        ICoreAPI api,
        long id,
        InventoryBase targetInv,
        int slotId,
        string targetBefore,
        string targetAfter,
        string mouseBefore,
        string mouseAfter)
    {
        if (!Enabled || id <= 0 || targetInv == null) return;

        string key = Key(targetInv.InventoryID, slotId);
        DiagnosticClickTrace trace = null;
        lock (LockObj)
        {
            RecentBySlot.TryGetValue(key, out trace);
            if (trace != null && trace.Id == id)
            {
                trace.TargetAfter = targetAfter;
                trace.MouseAfter = mouseAfter;
                trace.CreatedMs = Environment.TickCount64;
            }
        }

        Log(api,
            "CLICK end   #{0} {1}[{2}] target {3}->{4} mouse {5}->{6}",
            id,
            targetInv.InventoryID ?? "?",
            slotId,
            targetBefore,
            targetAfter,
            mouseBefore,
            mouseAfter);
    }

    public static void ClientUpdate(
        ICoreAPI api,
        string kind,
        InventoryBase inv,
        int slotId,
        Packet_ItemStack incoming,
        string action)
    {
        if (!Enabled || inv == null) return;

        string local = slotId >= 0 && slotId < inv.Count
            ? ClientPredictionSuppressor.Fingerprint(inv[slotId]?.Itemstack)
            : "missing-slot";
        string inc = ClientPredictionSuppressor.Fingerprint(incoming);
        DiagnosticClickTrace recent = FindRecent(inv.InventoryID, slotId, out long ageMs);
        string recentText = recent == null
            ? "recent=none"
            : $"recent=#{recent.Id}/{ageMs}ms/{recent.MouseButton}/{recent.TargetBefore}->{recent.TargetAfter}/mouse:{recent.MouseBefore}->{recent.MouseAfter}";

        Log(api,
            "CLIENT {0} {1} {2}[{3}] incoming={4} localBefore={5} {6}",
            kind,
            action,
            inv.InventoryID ?? "?",
            slotId,
            inc,
            local,
            recentText);
    }

    public static bool ShouldDetailContentsSlot(InventoryBase inv, int slotId, Packet_ItemStack incoming)
    {
        if (!Enabled || inv == null) return false;

        if (FindRecent(inv.InventoryID, slotId, out _) != null)
        {
            return true;
        }

        if (slotId < 0 || slotId >= inv.Count)
        {
            return true;
        }

        return ClientPredictionSuppressor.Fingerprint(inv[slotId]?.Itemstack) != ClientPredictionSuppressor.Fingerprint(incoming);
    }

    public static void ClientContentsSummary(ICoreAPI api, InventoryBase inv, int count, int applied, int suppressed, int detailed)
    {
        if (!Enabled || inv == null) return;

        Log(api,
            "CLIENT CONTENTS summary {0} slots={1} applied={2} suppressed={3} detailed={4}",
            inv.InventoryID ?? "?",
            count,
            applied,
            suppressed,
            detailed);
    }

    public static void ServerClientPacket(ICoreAPI api, InventoryBase inv, IPlayer player, int packetId, Packet_Client packet)
    {
        if (!Enabled || inv == null || player == null || packet == null) return;

        switch (packetId)
        {
            case 7 when packet.ActivateInventorySlot != null:
                Packet_ActivateInventorySlot p = packet.ActivateInventorySlot;
                Log(api,
                    "SERVER client ActivateSlot player={0} inv={1} slot={2} button={3} targetLastChanged={4} serverLastChanged={5}",
                    player.PlayerName,
                    inv.InventoryID ?? "?",
                    p.TargetSlot,
                    (EnumMouseButton)p.MouseButton,
                    p.TargetLastChanged,
                    inv.lastChangedSinceServerStart);
                break;

            case 8 when packet.MoveItemstack != null:
                Log(api,
                    "SERVER client MoveStack player={0} {1}[{2}]({3}) -> {4}[{5}]({6}) qty={7}",
                    player.PlayerName,
                    packet.MoveItemstack.SourceInventoryId,
                    packet.MoveItemstack.SourceSlot,
                    packet.MoveItemstack.SourceLastChanged,
                    packet.MoveItemstack.TargetInventoryId,
                    packet.MoveItemstack.TargetSlot,
                    packet.MoveItemstack.TargetLastChanged,
                    packet.MoveItemstack.Quantity);
                break;

            case 9 when packet.Flipitemstacks != null:
                Log(api,
                    "SERVER client FlipStack player={0} {1}[{2}]({3}) <-> {4}[{5}]({6})",
                    player.PlayerName,
                    packet.Flipitemstacks.SourceInventoryId,
                    packet.Flipitemstacks.SourceSlot,
                    packet.Flipitemstacks.SourceLastChanged,
                    packet.Flipitemstacks.TargetInventoryId,
                    packet.Flipitemstacks.TargetSlot,
                    packet.Flipitemstacks.TargetLastChanged);
                break;
        }
    }

    public static void ServerSend(ICoreAPI api, int clientId, Packet_Server packet, string action)
    {
        if (!Enabled || packet == null) return;

        if (packet.InventoryUpdate != null)
        {
            Log(api,
                "SERVER send {0} InventoryUpdate client={1} {2}[{3}] stack={4}",
                action,
                clientId,
                packet.InventoryUpdate.InventoryId,
                packet.InventoryUpdate.SlotId,
                ClientPredictionSuppressor.Fingerprint(packet.InventoryUpdate.ItemStack));
        }
        else if (packet.InventoryDoubleUpdate != null)
        {
            Log(api,
                "SERVER send {0} InventoryDoubleUpdate client={1} {2}[{3}]={4} {5}[{6}]={7}",
                action,
                clientId,
                packet.InventoryDoubleUpdate.InventoryId1,
                packet.InventoryDoubleUpdate.SlotId1,
                ClientPredictionSuppressor.Fingerprint(packet.InventoryDoubleUpdate.ItemStack1),
                packet.InventoryDoubleUpdate.InventoryId2,
                packet.InventoryDoubleUpdate.SlotId2,
                ClientPredictionSuppressor.Fingerprint(packet.InventoryDoubleUpdate.ItemStack2));
        }
        else if (packet.InventoryContents != null)
        {
            Log(api,
                "SERVER send {0} InventoryContents client={1} {2} slots={3}",
                action,
                clientId,
                packet.InventoryContents.InventoryId,
                packet.InventoryContents.ItemstacksCount);
        }
    }

    public static void Log(ICoreAPI api, string message, params object[] args)
    {
        if (!Enabled) return;
        api?.Logger.Notification("[ISFDiag] " + message, args);
    }

    private static void Remember(DiagnosticClickTrace trace)
    {
        string key = Key(trace.InventoryId, trace.SlotId);
        lock (LockObj)
        {
            RecentBySlot[key] = trace;
            RecentSlotOrder.Enqueue(key);
            while (RecentSlotOrder.Count > MaxClicks)
            {
                string oldKey = RecentSlotOrder.Dequeue();
                if (RecentBySlot.TryGetValue(oldKey, out DiagnosticClickTrace oldTrace)
                    && Environment.TickCount64 - oldTrace.CreatedMs > RecentMs)
                {
                    RecentBySlot.Remove(oldKey);
                }
            }
        }
    }

    private static DiagnosticClickTrace FindRecent(string inventoryId, int slotId, out long ageMs)
    {
        ageMs = -1;
        string key = Key(inventoryId, slotId);
        lock (LockObj)
        {
            if (!RecentBySlot.TryGetValue(key, out DiagnosticClickTrace trace))
            {
                return null;
            }

            ageMs = Environment.TickCount64 - trace.CreatedMs;
            if (ageMs > RecentMs)
            {
                RecentBySlot.Remove(key);
                return null;
            }

            return trace;
        }
    }

    private static void Clear()
    {
        lock (LockObj)
        {
            RecentBySlot.Clear();
            RecentSlotOrder.Clear();
        }
    }

    private static string Key(string inventoryId, int slotId)
    {
        return (inventoryId ?? "?") + "#" + slotId;
    }
}

internal static class ClientPredictionSuppressor
{
    private const long MaxAgeMs = 2000;
    private const long ExhaustedCursorSplitGuardMs = 350;
    private const int MaxEntriesPerSlot = 128;
    private static readonly Dictionary<string, List<ClientPredictedSlotUpdate>> PredictedBySlot = new();
    private static readonly Dictionary<string, ClientLatestSlotPrediction> LatestBySlot = new();
    private static readonly Dictionary<string, long> RecentlyExhaustedCursorBySlot = new();

    public static void Remember(InventoryBase inv, int slotId, ClientPredictionDirection direction, string staleIdentity)
    {
        if (inv == null || slotId < 0 || slotId >= inv.Count || string.IsNullOrEmpty(inv.InventoryID)) return;

        string key = Key(inv.InventoryID, slotId);
        ClientStackInfo predicted = StackInfo(inv[slotId]?.Itemstack);
        long now = Environment.TickCount64;

        if (!PredictedBySlot.TryGetValue(key, out List<ClientPredictedSlotUpdate> entries))
        {
            entries = new List<ClientPredictedSlotUpdate>();
            PredictedBySlot[key] = entries;
        }

        PruneExpired(entries, now);
        entries.Add(new ClientPredictedSlotUpdate
        {
            Fingerprint = predicted.Fingerprint,
            CreatedMs = now
        });

        while (entries.Count > MaxEntriesPerSlot)
        {
            entries.RemoveAt(0);
        }

        if (direction != ClientPredictionDirection.Unknown)
        {
            LatestBySlot[key] = new ClientLatestSlotPrediction
            {
                Identity = staleIdentity ?? predicted.Identity,
                StackSize = predicted.StackSize,
                Direction = direction,
                CreatedMs = now
            };
        }
    }

    public static void RememberExhaustedCursorTarget(InventoryBase targetInv, int slotId)
    {
        if (targetInv == null || slotId < 0 || slotId >= targetInv.Count || string.IsNullOrEmpty(targetInv.InventoryID)) return;

        RecentlyExhaustedCursorBySlot[Key(targetInv.InventoryID, slotId)] = Environment.TickCount64;
    }

    public static bool ShouldGuardEmptyCursorSplit(InventoryBase targetInv, int slotId)
    {
        if (targetInv == null || slotId < 0 || slotId >= targetInv.Count || string.IsNullOrEmpty(targetInv.InventoryID)) return false;

        string key = Key(targetInv.InventoryID, slotId);
        if (!RecentlyExhaustedCursorBySlot.TryGetValue(key, out long createdMs))
        {
            return false;
        }

        if (Environment.TickCount64 - createdMs > ExhaustedCursorSplitGuardMs)
        {
            RecentlyExhaustedCursorBySlot.Remove(key);
            return false;
        }

        return true;
    }

    public static void Clear(InventoryBase inv, int slotId)
    {
        if (inv == null || slotId < 0 || slotId >= inv.Count || string.IsNullOrEmpty(inv.InventoryID)) return;

        string key = Key(inv.InventoryID, slotId);
        PredictedBySlot.Remove(key);
        LatestBySlot.Remove(key);
        RecentlyExhaustedCursorBySlot.Remove(key);
    }

    public static bool ShouldSuppress(InventoryBase inv, Packet_InventoryUpdate packet)
    {
        if (inv == null || packet == null || string.IsNullOrEmpty(packet.InventoryId)) return false;

        string key = Key(packet.InventoryId, packet.SlotId);
        ClientStackInfo incoming = StackInfo(packet.ItemStack);

        if (ShouldSuppressStaleMonotonicConfirmation(inv, packet.SlotId, key, incoming))
        {
            RemoveMatchingPrediction(key, incoming.Fingerprint, Environment.TickCount64);
            return true;
        }

        if (!PredictedBySlot.TryGetValue(key, out List<ClientPredictedSlotUpdate> entries))
        {
            return false;
        }

        long now = Environment.TickCount64;
        PruneExpired(entries, now);

        int matchIndex = entries.FindIndex(entry => entry.Fingerprint == incoming.Fingerprint);
        if (matchIndex < 0)
        {
            if (entries.Count == 0) PredictedBySlot.Remove(key);
            return false;
        }

        bool hasLaterPrediction = matchIndex < entries.Count - 1;
        bool alreadyMatches = packet.SlotId >= 0
            && packet.SlotId < inv.Count
            && Fingerprint(inv[packet.SlotId]?.Itemstack) == incoming.Fingerprint;

        entries.RemoveAt(matchIndex);
        if (entries.Count == 0)
        {
            PredictedBySlot.Remove(key);
        }

        return hasLaterPrediction || alreadyMatches;
    }

    private static void RemoveMatchingPrediction(string key, string fingerprint, long now)
    {
        if (!PredictedBySlot.TryGetValue(key, out List<ClientPredictedSlotUpdate> entries))
        {
            return;
        }

        PruneExpired(entries, now);
        int matchIndex = entries.FindIndex(entry => entry.Fingerprint == fingerprint);
        if (matchIndex >= 0)
        {
            entries.RemoveAt(matchIndex);
        }

        if (entries.Count == 0)
        {
            PredictedBySlot.Remove(key);
        }
    }

    public static ClientPredictionDirection Direction(string beforeFingerprint, string afterFingerprint)
    {
        ClientStackInfo before = StackInfo(beforeFingerprint);
        ClientStackInfo after = StackInfo(afterFingerprint);

        if (before.Empty && !after.Empty) return ClientPredictionDirection.Increasing;
        if (!before.Empty && after.Empty) return ClientPredictionDirection.Decreasing;
        if (before.Identity != after.Identity) return ClientPredictionDirection.Unknown;
        if (after.StackSize > before.StackSize) return ClientPredictionDirection.Increasing;
        if (after.StackSize < before.StackSize) return ClientPredictionDirection.Decreasing;

        return ClientPredictionDirection.Unknown;
    }

    public static string StaleIdentity(string beforeFingerprint, string afterFingerprint, ClientPredictionDirection direction)
    {
        ClientStackInfo before = StackInfo(beforeFingerprint);
        ClientStackInfo after = StackInfo(afterFingerprint);

        if (direction == ClientPredictionDirection.Decreasing && after.Empty)
        {
            return before.Identity;
        }

        if (!after.Empty)
        {
            return after.Identity;
        }

        return before.Identity;
    }

    public static string Fingerprint(ItemStack stack)
    {
        return StackInfo(stack).Fingerprint;
    }

    private static bool ShouldSuppressStaleMonotonicConfirmation(
        InventoryBase inv,
        int slotId,
        string key,
        ClientStackInfo incoming)
    {
        if (!LatestBySlot.TryGetValue(key, out ClientLatestSlotPrediction latest))
        {
            return false;
        }

        long now = Environment.TickCount64;
        if (now - latest.CreatedMs > MaxAgeMs)
        {
            LatestBySlot.Remove(key);
            return false;
        }

        if (slotId < 0 || slotId >= inv.Count)
        {
            return false;
        }

        ClientStackInfo local = StackInfo(inv[slotId]?.Itemstack);

        if (incoming.Fingerprint == local.Fingerprint)
        {
            return true;
        }

        bool incomingSameItem = incoming.Empty
            ? latest.Direction == ClientPredictionDirection.Increasing && latest.StackSize > 0
            : incoming.Identity == latest.Identity;
        bool localAtOrPastLatest = local.Empty
            ? latest.StackSize == 0
            : local.Identity == latest.Identity
                && ((latest.Direction == ClientPredictionDirection.Increasing && local.StackSize >= latest.StackSize)
                    || (latest.Direction == ClientPredictionDirection.Decreasing && local.StackSize <= latest.StackSize));

        if (!incomingSameItem || !localAtOrPastLatest)
        {
            return false;
        }

        return latest.Direction switch
        {
            ClientPredictionDirection.Increasing => incoming.Empty || incoming.StackSize < latest.StackSize,
            ClientPredictionDirection.Decreasing => incoming.StackSize > latest.StackSize,
            _ => false
        };
    }

    private static ClientStackInfo StackInfo(ItemStack stack)
    {
        return stack == null ? StackInfo((Packet_ItemStack)null) : StackInfo(StackConverter.ToPacket(stack));
    }

    public static string Fingerprint(Packet_ItemStack stack)
    {
        return StackInfo(stack).Fingerprint;
    }

    private static ClientStackInfo StackInfo(Packet_ItemStack stack)
    {
        if (stack == null || stack.ItemClass == -1 || stack.ItemId == 0)
        {
            return new ClientStackInfo("empty", "empty", 0, true);
        }

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

            string identity = stack.ItemClass + ":" + stack.ItemId + ":" + hash;
            return new ClientStackInfo(
                stack.ItemClass + ":" + stack.ItemId + ":" + stack.StackSize + ":" + hash,
                identity,
                stack.StackSize,
                false);
        }
    }

    private static ClientStackInfo StackInfo(string fingerprint)
    {
        if (string.IsNullOrEmpty(fingerprint) || fingerprint == "empty")
        {
            return new ClientStackInfo("empty", "empty", 0, true);
        }

        string[] parts = fingerprint.Split(':');
        if (parts.Length < 4 || !int.TryParse(parts[2], out int stackSize))
        {
            return new ClientStackInfo(fingerprint, fingerprint, 0, false);
        }

        return new ClientStackInfo(
            fingerprint,
            parts[0] + ":" + parts[1] + ":" + parts[3],
            stackSize,
            false);
    }

    private static void PruneExpired(List<ClientPredictedSlotUpdate> entries, long now)
    {
        for (int i = entries.Count - 1; i >= 0; i--)
        {
            if (now - entries[i].CreatedMs > MaxAgeMs)
            {
                entries.RemoveAt(i);
            }
        }
    }

    private static string Key(string inventoryId, int slotId)
    {
        return inventoryId + "#" + slotId;
    }
}

internal static class ClientExternalClickGate
{
    private const long MaxPendingMs = 450;
    private const int ContentsDeferMs = 90;
    private static readonly object LockObj = new();
    private static readonly Dictionary<string, PendingExternalClick> PendingBySlot = new();
    private static long NextSequence;

    public static bool HasActivePending(ICoreClientAPI api, InventoryBase targetInv, int slotId)
    {
        long now = Environment.TickCount64;
        lock (LockObj)
        {
            PruneExpired(now);
            return false;
        }
    }

    public static void Remember(ICoreClientAPI api, InventoryBase inv, int slotId)
    {
        if (!IsExternalInventory(inv) || slotId < 0 || slotId >= inv.Count) return;

        lock (LockObj)
        {
            PendingBySlot[Key(inv.InventoryID, slotId)] = new PendingExternalClick
            {
                InventoryId = inv.InventoryID,
                SlotId = slotId,
                Sequence = System.Threading.Interlocked.Increment(ref NextSequence),
                CreatedMs = Environment.TickCount64
            };
        }

        SyncDiagnostics.Log(api, "CLIENT gate pending {0}[{1}]", inv.InventoryID ?? "?", slotId);
    }

    public static void Clear(ICoreAPI api, InventoryBase inv, int slotId)
    {
        if (inv?.InventoryID == null || slotId < 0) return;

        bool removed;
        lock (LockObj)
        {
            removed = PendingBySlot.Remove(Key(inv.InventoryID, slotId));
        }

        if (removed)
        {
            SyncDiagnostics.Log(api, "CLIENT gate confirmed {0}[{1}]", inv.InventoryID ?? "?", slotId);
        }
    }

    public static bool TryDeferContentsSlot(
        ICoreClientAPI api,
        InventoryNetworkUtil util,
        IWorldAccessor resolver,
        InventoryBase inv,
        Packet_InventoryUpdate update)
    {
        if (api == null || util == null || update == null || !IsExternalInventory(inv))
        {
            return false;
        }

        PendingExternalClick pending;
        lock (LockObj)
        {
            PruneExpired(Environment.TickCount64);
            if (!PendingBySlot.TryGetValue(Key(inv.InventoryID, update.SlotId), out pending))
            {
                return false;
            }
        }

        string fingerprint = ClientPredictionSuppressor.Fingerprint(update.ItemStack);
        api.Event.RegisterCallback(_ => ApplyDeferredContentsSlot(api, util, resolver, inv, update, pending.Sequence, fingerprint), ContentsDeferMs);
        SyncDiagnostics.Log(
            api,
            "CLIENT gate deferred contents {0}[{1}] seq={2} stack={3}",
            inv.InventoryID ?? "?",
            update.SlotId,
            pending.Sequence,
            fingerprint);
        return true;
    }

    public static bool TryPreservePendingTreeSlots(ICoreAPI api, InventoryBase inv, ref ITreeAttribute treeAttribute)
    {
        if (api?.Side != EnumAppSide.Client || inv == null || treeAttribute == null || !IsExternalInventory(inv))
        {
            return false;
        }

        List<int> pendingSlots;
        lock (LockObj)
        {
            PruneExpired(Environment.TickCount64);
            pendingSlots = PendingBySlot.Values
                .Where(pending => pending.InventoryId == inv.InventoryID && pending.SlotId >= 0 && pending.SlotId < inv.Count)
                .Select(pending => pending.SlotId)
                .Distinct()
                .ToList();
        }

        if (pendingSlots.Count == 0)
        {
            return false;
        }

        ITreeAttribute clonedTree = treeAttribute.Clone();
        ITreeAttribute slotsTree = clonedTree.GetOrAddTreeAttribute("slots");

        foreach (int slotId in pendingSlots)
        {
            string key = slotId.ToString();
            ItemStack stack = inv[slotId]?.Itemstack?.Clone();
            if (stack == null)
            {
                slotsTree.RemoveAttribute(key);
            }
            else
            {
                slotsTree.SetItemstack(key, stack);
            }
        }

        treeAttribute = clonedTree;
        SyncDiagnostics.Log(
            api,
            "CLIENT gate preserved tree slots {0}[{1}]",
            inv.InventoryID ?? "?",
            string.Join(",", pendingSlots));
        return true;
    }

    public static bool IsExternalInventory(InventoryBase inv)
    {
        string id = inv?.InventoryID;
        if (string.IsNullOrEmpty(id)) return false;
        if (inv is InventoryBasePlayer) return false;
        if (inv is InventoryCraftingGrid) return false;

        return !id.StartsWith("hotbar-", StringComparison.Ordinal)
            && !id.StartsWith("backpack-", StringComparison.Ordinal)
            && !id.StartsWith("mouse-", StringComparison.Ordinal)
            && !id.StartsWith("ground-", StringComparison.Ordinal)
            && !id.StartsWith("character-", StringComparison.Ordinal)
            && !id.StartsWith("creative-", StringComparison.Ordinal)
            && !id.StartsWith("craftinggrid-", StringComparison.Ordinal);
    }

    private static void PruneExpired(long now)
    {
        foreach (string key in PendingBySlot
            .Where(entry => now - entry.Value.CreatedMs > MaxPendingMs)
            .Select(entry => entry.Key)
            .ToList())
        {
            PendingBySlot.Remove(key);
        }
    }

    private static string Key(string inventoryId, int slotId)
    {
        return inventoryId + "#" + slotId;
    }

    private static void ApplyDeferredContentsSlot(
        ICoreClientAPI api,
        InventoryNetworkUtil util,
        IWorldAccessor resolver,
        InventoryBase inv,
        Packet_InventoryUpdate update,
        long sequence,
        string fingerprint)
    {
        bool stillPending;
        lock (LockObj)
        {
            stillPending = PendingBySlot.TryGetValue(Key(inv.InventoryID, update.SlotId), out PendingExternalClick pending)
                && pending.Sequence == sequence;
        }

        if (!stillPending)
        {
            SyncDiagnostics.Log(
                api,
                "CLIENT gate dropped deferred contents {0}[{1}] seq={2} stack={3}",
                inv.InventoryID ?? "?",
                update.SlotId,
                sequence,
                fingerprint);
            return;
        }

        Clear(api, inv, update.SlotId);
        ClientPredictionSuppressor.Clear(inv, update.SlotId);
        SyncDiagnostics.Log(
            api,
            "CLIENT gate applied deferred contents {0}[{1}] seq={2} stack={3}",
            inv.InventoryID ?? "?",
            update.SlotId,
            sequence,
            fingerprint);
        util.UpdateFromPacket(resolver ?? api.World, update);
    }
}

internal sealed class PendingExternalClick
{
    public string InventoryId;
    public int SlotId;
    public long Sequence;
    public long CreatedMs;
}

internal static class Patch_InventoryBase_SlotsFromTreeAttributes
{
    public static void Prefix(InventoryBase __instance, ref ITreeAttribute tree)
    {
        ClientExternalClickGate.TryPreservePendingTreeSlots(__instance?.Api, __instance, ref tree);
    }
}

internal static class Patch_GuiElementItemSlotGridBase_SlotClick
{
    private static readonly FieldInfo InventoryField = AccessTools.Field(typeof(GuiElementItemSlotGridBase), "inventory");

    public static bool Prefix(
        GuiElementItemSlotGridBase __instance,
        ICoreClientAPI api,
        int slotId,
        EnumMouseButton mouseButton,
        bool shiftPressed,
        bool ctrlPressed,
        bool altPressed,
        out ClientPredictionState __state)
    {
        __state = default;

        if (api?.Side != EnumAppSide.Client)
        {
            return true;
        }

        IInventory rawInv = InventoryField.GetValue(__instance) as IInventory;
        InventoryBase targetInv = rawInv as InventoryBase;
        InventoryBase mouseInv = api.World?.Player?.InventoryManager?.GetOwnInventory(GlobalConstants.mousecursorInvClassName) as InventoryBase;
        ItemSlot mouseSlot = api.World?.Player?.InventoryManager?.MouseItemSlot;
        if (ClientExternalClickGate.HasActivePending(api, targetInv, slotId))
        {
            return false;
        }

        bool mouseEmpty = mouseSlot == null || mouseSlot.Empty;
        string targetBefore = rawInv != null && slotId >= 0 && slotId < rawInv.Count
            ? ClientPredictionSuppressor.Fingerprint(rawInv[slotId]?.Itemstack)
            : "empty";
        string mouseBefore = ClientPredictionSuppressor.Fingerprint(mouseSlot?.Itemstack);
        long diagnosticClickId = SyncDiagnostics.BeginClick(
            api,
            targetInv,
            slotId,
            mouseButton,
            shiftPressed,
            ctrlPressed,
            altPressed,
            targetBefore,
            mouseBefore);
        __state = new ClientPredictionState(false, targetBefore, mouseBefore, diagnosticClickId);
        bool canTrackPrediction = !shiftPressed
            && (mouseButton == EnumMouseButton.Left
                || (mouseButton == EnumMouseButton.Right && !mouseEmpty));

        if (targetInv is InventoryCraftingGrid)
        {
            canTrackPrediction = false;
        }

        if (mouseEmpty)
        {
            if (mouseButton == EnumMouseButton.Right
                && !shiftPressed
                && ClientPredictionSuppressor.ShouldGuardEmptyCursorSplit(targetInv, slotId))
            {
                return false;
            }
        }

        if (!canTrackPrediction)
        {
            ClientPredictionSuppressor.Clear(targetInv, slotId);
            ClientPredictionSuppressor.Clear(mouseInv, 0);
            __state = new ClientPredictionState(false, targetBefore, mouseBefore, diagnosticClickId);
            return true;
        }

        __state = new ClientPredictionState(
            true,
            targetBefore,
            mouseBefore,
            diagnosticClickId);
        return true;
    }

    public static void Postfix(
        GuiElementItemSlotGridBase __instance,
        ICoreClientAPI api,
        int slotId,
        ClientPredictionState __state)
    {
        IInventory rawInv = InventoryField.GetValue(__instance) as IInventory;
        InventoryBase targetInv = rawInv as InventoryBase;
        InventoryBase mouseInv = api?.World?.Player?.InventoryManager?.GetOwnInventory(GlobalConstants.mousecursorInvClassName) as InventoryBase;
        if (targetInv == null || mouseInv == null || slotId < 0 || slotId >= targetInv.Count)
        {
            return;
        }

        string mouseAfter = ClientPredictionSuppressor.Fingerprint(mouseInv[0]?.Itemstack);
        string targetAfter = ClientPredictionSuppressor.Fingerprint(targetInv[slotId]?.Itemstack);
        SyncDiagnostics.EndClick(
            api,
            __state.DiagnosticClickId,
            targetInv,
            slotId,
            __state.TargetBefore,
            targetAfter,
            __state.MouseBefore,
            mouseAfter);

        if (!__state.Track)
        {
            return;
        }

        if (targetAfter == __state.TargetBefore && mouseAfter == __state.MouseBefore)
        {
            return;
        }

        ClientExternalClickGate.Remember(api, targetInv, slotId);

        ClientPredictionDirection targetDirection = ClientPredictionSuppressor.Direction(__state.TargetBefore, targetAfter);
        ClientPredictionDirection mouseDirection = ClientPredictionSuppressor.Direction(__state.MouseBefore, mouseAfter);

        ClientPredictionSuppressor.Remember(
            targetInv,
            slotId,
            targetDirection,
            ClientPredictionSuppressor.StaleIdentity(__state.TargetBefore, targetAfter, targetDirection));
        ClientPredictionSuppressor.Remember(
            mouseInv,
            0,
            mouseDirection,
            ClientPredictionSuppressor.StaleIdentity(__state.MouseBefore, mouseAfter, mouseDirection));

        if (targetDirection == ClientPredictionDirection.Increasing && mouseAfter == "empty")
        {
            ClientPredictionSuppressor.RememberExhaustedCursorTarget(targetInv, slotId);
        }
    }
}

internal static class Patch_GuiElementItemSlotGridBase_OnMouseDownOnElement
{
    private static readonly FieldInfo InventoryField = AccessTools.Field(typeof(GuiElementItemSlotGridBase), "inventory");

    public static void Postfix(GuiElementItemSlotGridBase __instance, ICoreClientAPI api, MouseEvent args)
    {
        if (api?.Side != EnumAppSide.Client || args?.Button != EnumMouseButton.Left)
        {
            return;
        }

        InventoryBase inv = InventoryField.GetValue(__instance) as InventoryBase;
        if (inv is not InventoryCraftingGrid)
        {
            return;
        }

        inv.InvNetworkUtil.PauseInventoryUpdates = false;
        if (api.World?.Player?.InventoryManager?.MouseItemSlot?.Inventory?.InvNetworkUtil != null)
        {
            api.World.Player.InventoryManager.MouseItemSlot.Inventory.InvNetworkUtil.PauseInventoryUpdates = false;
        }

        SyncDiagnostics.Log(api, "CLIENT crafting drag left server updates unpaused {0}", inv.InventoryID ?? "?");
    }
}

internal static class Patch_InventoryNetworkUtil_UpdateFromPacket
{
    private static readonly FieldInfo InvField = AccessTools.Field(typeof(InventoryNetworkUtil), "inv");
    private static readonly FieldInfo PauseField = AccessTools.Field(typeof(InventoryNetworkUtil), "pauseInvUpdates");
    private static readonly FieldInfo QueueField = AccessTools.Field(typeof(InventoryNetworkUtil), "pkts");

    public static bool Prefix(InventoryNetworkUtil __instance, Packet_InventoryUpdate packet, out bool __state)
    {
        __state = false;

        ICoreAPI api = __instance?.Api;
        if (api?.Side != EnumAppSide.Client || packet == null)
        {
            return true;
        }

        if (__instance is PlayerInventoryNetworkUtil)
        {
            return true;
        }

        InventoryBase inv = InvField.GetValue(__instance) as InventoryBase;
        bool isMouseInventory = inv?.InventoryID != null && inv.InventoryID.StartsWith("mouse-", StringComparison.Ordinal);
        bool suppress = !isMouseInventory && ClientPredictionSuppressor.ShouldSuppress(inv, packet);
        SyncDiagnostics.ClientUpdate(api, "InventoryUpdate", inv, packet.SlotId, packet.ItemStack, suppress ? "suppressed" : "incoming");
        if (suppress)
        {
            return false;
        }

        ClientExternalClickGate.Clear(api, inv, packet.SlotId);

        if (inv?.InventoryID == null)
        {
            return true;
        }

        if (!inv.InventoryID.StartsWith("mouse-", StringComparison.Ordinal))
        {
            return true;
        }

        ClientPredictionSuppressor.Clear(inv, packet.SlotId);

        bool isPaused = (bool)PauseField.GetValue(__instance);
        if (!isPaused)
        {
            return true;
        }

        Queue<Packet_InventoryUpdate> queue = QueueField.GetValue(__instance) as Queue<Packet_InventoryUpdate>;
        int dropped = queue?.Count ?? 0;
        queue?.Clear();

        PauseField.SetValue(__instance, false);
        __state = true;

        api.Logger.VerboseDebug(
            "[ItemSyncFixes] Applied paused mouse update immediately for {0}[{1}], dropped queued={2}",
            inv.InventoryID,
            packet.SlotId,
            dropped);
        SyncDiagnostics.Log(
            api,
            "CLIENT mouse-pause immediate {0}[{1}] droppedQueued={2}",
            inv.InventoryID,
            packet.SlotId,
            dropped);

        return true;
    }

    public static void Postfix(InventoryNetworkUtil __instance, bool __state)
    {
        if (__state)
        {
            PauseField.SetValue(__instance, true);
        }
    }
}

internal static class Patch_InventoryNetworkUtil_UpdateFromPacket_Double
{
    private static readonly FieldInfo InvField = AccessTools.Field(typeof(InventoryNetworkUtil), "inv");

    public static bool Prefix(InventoryNetworkUtil __instance, IWorldAccessor resolver, Packet_InventoryDoubleUpdate packet)
    {
        ICoreAPI api = __instance?.Api;
        if (api?.Side != EnumAppSide.Client || packet == null)
        {
            return true;
        }

        if (__instance is PlayerInventoryNetworkUtil)
        {
            return true;
        }

        InventoryBase inv = InvField.GetValue(__instance) as InventoryBase;
        if (inv?.InventoryID == null)
        {
            return true;
        }

        bool handled = false;
        if (packet.InventoryId1 == inv.InventoryID)
        {
            ApplySingleIfNotSuppressed(__instance, resolver, inv, packet.InventoryId1, packet.SlotId1, packet.ItemStack1);
            handled = true;
        }

        if (packet.InventoryId2 == inv.InventoryID)
        {
            ApplySingleIfNotSuppressed(__instance, resolver, inv, packet.InventoryId2, packet.SlotId2, packet.ItemStack2);
            handled = true;
        }

        return !handled;
    }

    private static void ApplySingleIfNotSuppressed(
        InventoryNetworkUtil util,
        IWorldAccessor resolver,
        InventoryBase inv,
        string inventoryId,
        int slotId,
        Packet_ItemStack itemStack)
    {
        Packet_InventoryUpdate update = new()
        {
            InventoryId = inventoryId,
            SlotId = slotId,
            ItemStack = itemStack
        };

        bool isMouseInventory = inv?.InventoryID != null && inv.InventoryID.StartsWith("mouse-", StringComparison.Ordinal);
        if (isMouseInventory)
        {
            ClientPredictionSuppressor.Clear(inv, slotId);
        }

        bool suppress = !isMouseInventory && ClientPredictionSuppressor.ShouldSuppress(inv, update);
        SyncDiagnostics.ClientUpdate(util.Api, "InventoryDoubleUpdate", inv, slotId, itemStack, suppress ? "suppressed" : "apply-single");
        if (!suppress)
        {
            ClientExternalClickGate.Clear(util.Api, inv, slotId);
            util.UpdateFromPacket(resolver, update);
        }
    }
}

internal static class Patch_PlayerInventoryNetworkUtil_UpdateFromPacket
{
    private static readonly FieldInfo InvField = AccessTools.Field(typeof(InventoryNetworkUtil), "inv");

    public static bool Prefix(PlayerInventoryNetworkUtil __instance, Packet_InventoryUpdate packet)
    {
        ICoreAPI api = __instance?.Api;
        if (api?.Side != EnumAppSide.Client || packet == null)
        {
            return true;
        }

        InventoryBase inv = InvField.GetValue(__instance) as InventoryBase;
        if (!IsClientPlayerStorage(inv) || packet.SlotId < 0 || packet.SlotId >= inv.Count)
        {
            return true;
        }

        string incoming = ClientPredictionSuppressor.Fingerprint(packet.ItemStack);
        string local = ClientPredictionSuppressor.Fingerprint(inv[packet.SlotId]?.Itemstack);
        if (incoming != local)
        {
            return true;
        }

        ClientPredictionSuppressor.Clear(inv, packet.SlotId);
        SyncDiagnostics.ClientUpdate(api, "PlayerInventoryUpdate", inv, packet.SlotId, packet.ItemStack, "exact-local-suppressed");
        return false;
    }

    private static bool IsClientPlayerStorage(InventoryBase inv)
    {
        string id = inv?.InventoryID;
        return id != null
            && (id.StartsWith("hotbar-", StringComparison.Ordinal)
                || id.StartsWith("backpack-", StringComparison.Ordinal));
    }
}

internal static class Patch_InventoryNetworkUtil_UpdateFromPacket_Contents
{
    private static readonly FieldInfo InvField = AccessTools.Field(typeof(InventoryNetworkUtil), "inv");

    public static bool Prefix(InventoryNetworkUtil __instance, IWorldAccessor resolver, Packet_InventoryContents packet)
    {
        ICoreAPI api = __instance?.Api;
        if (api?.Side != EnumAppSide.Client || packet == null)
        {
            return true;
        }

        if (__instance is PlayerInventoryNetworkUtil)
        {
            return true;
        }

        InventoryBase inv = InvField.GetValue(__instance) as InventoryBase;
        if (inv?.InventoryID == null || packet.Itemstacks == null)
        {
            return true;
        }

        int count = Math.Min(packet.ItemstacksCount, Math.Min(packet.Itemstacks.Length, inv.Count));
        int applied = 0;
        int suppressed = 0;
        int detailed = 0;
        for (int slotId = 0; slotId < count; slotId++)
        {
            Packet_InventoryUpdate update = new()
            {
                ClientId = packet.ClientId,
                InventoryId = packet.InventoryId,
                SlotId = slotId,
                ItemStack = packet.Itemstacks[slotId]
            };

            bool detail = SyncDiagnostics.ShouldDetailContentsSlot(inv, slotId, packet.Itemstacks[slotId]);
            if (detail)
            {
                detailed++;
                SyncDiagnostics.ClientUpdate(api, "InventoryContents", inv, slotId, packet.Itemstacks[slotId], "apply-slot");
            }

            if (ClientExternalClickGate.TryDeferContentsSlot(api as ICoreClientAPI, __instance, resolver, inv, update))
            {
                suppressed++;
                continue;
            }

            ClientExternalClickGate.Clear(api, inv, slotId);
            ClientPredictionSuppressor.Clear(inv, slotId);
            applied++;
            __instance.UpdateFromPacket(resolver, update);
        }

        SyncDiagnostics.ClientContentsSummary(api, inv, count, applied, suppressed, detailed);
        return false;
    }
}

internal static class Patch_InventoryNetworkUtil_SetPauseInventoryUpdates
{
    private static readonly FieldInfo InvField = AccessTools.Field(typeof(InventoryNetworkUtil), "inv");
    private static readonly FieldInfo PauseField = AccessTools.Field(typeof(InventoryNetworkUtil), "pauseInvUpdates");
    private static readonly FieldInfo QueueField = AccessTools.Field(typeof(InventoryNetworkUtil), "pkts");

    public static bool Prefix(InventoryNetworkUtil __instance, bool value)
    {
        ICoreAPI api = __instance?.Api;
        if (api?.Side != EnumAppSide.Client)
        {
            return true;
        }

        bool wasPaused = (bool)PauseField.GetValue(__instance);
        if (value || !wasPaused)
        {
            return true;
        }

        Queue<Packet_InventoryUpdate> queue = QueueField.GetValue(__instance) as Queue<Packet_InventoryUpdate>;
        if (queue == null || queue.Count <= 1)
        {
            return true;
        }

        InventoryBase inv = InvField.GetValue(__instance) as InventoryBase;
        int queuedCount = queue.Count;
        Dictionary<int, QueuedSlotUpdate> latestBySlot = new();
        int index = 0;

        foreach (Packet_InventoryUpdate packet in queue)
        {
            if (packet == null) continue;
            latestBySlot[packet.SlotId] = new QueuedSlotUpdate(packet, index);
            index++;
        }

        if (latestBySlot.Count == queuedCount)
        {
            return true;
        }

        queue.Clear();
        PauseField.SetValue(__instance, false);

        foreach (QueuedSlotUpdate update in latestBySlot.Values.OrderBy(update => update.LastIndex))
        {
            __instance.UpdateFromPacket(api.World, update.Packet);
        }

        api.Logger.VerboseDebug(
            "[ItemSyncFixes] Coalesced paused updates for {0}: queued={1}, applied={2}, dropped={3}",
            inv?.InventoryID ?? "?",
            queuedCount,
            latestBySlot.Count,
            queuedCount - latestBySlot.Count);
        SyncDiagnostics.Log(
            api,
            "CLIENT pause-flush coalesced {0} queued={1} applied={2} dropped={3}",
            inv?.InventoryID ?? "?",
            queuedCount,
            latestBySlot.Count,
            queuedCount - latestBySlot.Count);

        return false;
    }
}

internal readonly record struct SkipKey(int ClientId, string InventoryId, int SlotId);

internal sealed class SkipEntry
{
    public string Fingerprint;
    public long CreatedMs;
}

internal static class EchoSuppressor
{
    private const long MaxAgeMs = 2000;
    private const int MaxEntriesPerSlot = 64;
    private static readonly ConcurrentDictionary<SkipKey, List<SkipEntry>> ExpectedSelfUpdates = new();

    public static ICoreServerAPI Api;

    public static void Remember(int clientId, InventoryBase inv, int slotId)
    {
        if (clientId <= 0 || inv == null || slotId < 0 || slotId >= inv.Count) return;
        if (!IsSuppressibleInventory(inv.InventoryID)) return;

        string fingerprint = Fingerprint(inv[slotId]?.Itemstack);
        if (fingerprint == null) return;

        SkipKey key = new(clientId, inv.InventoryID, slotId);
        List<SkipEntry> entries = ExpectedSelfUpdates.GetOrAdd(key, _ => new List<SkipEntry>());
        long now = Environment.TickCount64;

        lock (entries)
        {
            PruneExpired(entries, now);

            entries.Add(new SkipEntry
            {
                Fingerprint = fingerprint,
                CreatedMs = now
            });

            while (entries.Count > MaxEntriesPerSlot)
            {
                entries.RemoveAt(0);
            }
        }
    }

    public static bool ShouldSuppress(int clientId, Packet_InventoryUpdate update)
    {
        if (update == null || string.IsNullOrEmpty(update.InventoryId)) return false;
        if (!IsSuppressibleInventory(update.InventoryId)) return false;

        SkipKey key = new(clientId, update.InventoryId, update.SlotId);
        if (!ExpectedSelfUpdates.TryGetValue(key, out List<SkipEntry> entries))
        {
            return false;
        }

        string outgoing = Fingerprint(update.ItemStack);
        bool suppress = false;

        lock (entries)
        {
            long now = Environment.TickCount64;
            PruneExpired(entries, now);

            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].Fingerprint != outgoing) continue;

                entries.RemoveAt(i);
                suppress = true;
                break;
            }

            if (entries.Count == 0)
            {
                ExpectedSelfUpdates.TryRemove(key, out _);
            }
        }

        if (suppress)
        {
            Api?.Logger.VerboseDebug(
                "[ItemSyncFixes] Suppressed exact self echo {0}[{1}] to client {2}",
                update.InventoryId,
                update.SlotId,
                clientId);
        }

        return suppress;
    }

    public static void Clear()
    {
        ExpectedSelfUpdates.Clear();
        Api = null;
    }

    private static void PruneExpired(List<SkipEntry> entries, long now)
    {
        for (int i = entries.Count - 1; i >= 0; i--)
        {
            if (now - entries[i].CreatedMs > MaxAgeMs)
            {
                entries.RemoveAt(i);
            }
        }
    }

    private static bool IsSuppressibleInventory(string inventoryId)
    {
        return inventoryId != null
            && (inventoryId.StartsWith("hotbar-", StringComparison.Ordinal)
                || inventoryId.StartsWith("backpack-", StringComparison.Ordinal));
    }

    private static string Fingerprint(ItemStack stack)
    {
        if (stack == null) return "empty";
        return Fingerprint(StackConverter.ToPacket(stack));
    }

    private static string Fingerprint(Packet_ItemStack stack)
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
}

internal static class Patch_InventoryNetworkUtil_HandleClientPacket
{
    private static readonly FieldInfo InvField = AccessTools.Field(typeof(InventoryNetworkUtil), "inv");

    public static bool Prefix(InventoryNetworkUtil __instance, IPlayer byPlayer, int packetId, Packet_Client packet, out DirtySnapshot __state)
    {
        __state = null;

        if (byPlayer == null || packet == null || (packetId != 7 && packetId != 8 && packetId != 9))
        {
            return true;
        }

        SyncDiagnostics.ServerClientPacket(__instance?.Api, InvField.GetValue(__instance) as InventoryBase, byPlayer, packetId, packet);

        if (TryHandleRightClickFromMouseStack(__instance, byPlayer, packetId, packet))
        {
            SyncDiagnostics.Log(
                __instance?.Api,
                "SERVER handled custom right-click place-one player={0}",
                byPlayer.PlayerName);
            return false;
        }

        __state = null;
        return true;
    }

    public static void Postfix(IPlayer byPlayer, DirtySnapshot __state)
    {
    }

    private static bool TryHandleRightClickFromMouseStack(InventoryNetworkUtil util, IPlayer byPlayer, int packetId, Packet_Client packet)
    {
        if (util?.Api?.Side != EnumAppSide.Server || packetId != 7 || packet.ActivateInventorySlot == null)
        {
            return false;
        }

        Packet_ActivateInventorySlot activate = packet.ActivateInventorySlot;
        if ((EnumMouseButton)activate.MouseButton != EnumMouseButton.Right)
        {
            return false;
        }

        InventoryBase targetInv = InvField.GetValue(util) as InventoryBase;
        InventoryBase mouseInv = byPlayer.InventoryManager?.GetInventory("mouse-" + byPlayer.PlayerUID) as InventoryBase;
        ItemSlot sourceSlot = mouseInv?[0];
        if (targetInv == null || sourceSlot == null || sourceSlot.Empty)
        {
            return false;
        }

        if (activate.TargetSlot < 0 || activate.TargetSlot >= targetInv.Count)
        {
            return true;
        }

        ItemStackMoveOperation op = new(
            util.Api.World,
            EnumMouseButton.Right,
            (EnumModifierKey)activate.Modifiers,
            (EnumMergePriority)activate.Priority);
        op.WheelDir = activate.Dir;
        op.ActingPlayer = byPlayer;

        targetInv.ActivateSlot(activate.TargetSlot, sourceSlot, ref op);
        return true;
    }
}

internal static class Patch_GeneralPacketHandler_HandleInventoryDoubleUpdate
{
    private static readonly Type ClientSystemType =
        AccessTools.TypeByName("Vintagestory.Client.NoObf.ClientSystem");

    private static readonly FieldInfo GameField =
        ClientSystemType == null ? null : AccessTools.Field(ClientSystemType, "game");

    private static readonly MethodInfo GetPlayerFromClientIdMethod =
        AccessTools.Method("Vintagestory.Client.NoObf.ClientMain:GetPlayerFromClientId");

    public static bool Prefix(object __instance, Packet_Server packet)
    {
        if (packet?.InventoryDoubleUpdate == null || GameField == null || GetPlayerFromClientIdMethod == null)
        {
            return true;
        }

        object game = GameField.GetValue(__instance);
        IWorldAccessor resolver = game as IWorldAccessor;
        IPlayer player = GetPlayerFromClientIdMethod.Invoke(game, new object[] { packet.InventoryDoubleUpdate.ClientId }) as IPlayer;
        if (resolver == null || player?.InventoryManager == null)
        {
            return false;
        }

        string inventoryId1 = packet.InventoryDoubleUpdate.InventoryId1;
        string inventoryId2 = packet.InventoryDoubleUpdate.InventoryId2;

        UpdateInventory(player, resolver, packet, inventoryId1);
        if (inventoryId1 != inventoryId2)
        {
            UpdateInventory(player, resolver, packet, inventoryId2);
        }

        return false;
    }

    private static void UpdateInventory(IPlayer player, IWorldAccessor resolver, Packet_Server packet, string inventoryId)
    {
        if (player.InventoryManager.GetInventory(inventoryId) is InventoryBase inventory &&
            inventory.InvNetworkUtil is InventoryNetworkUtil util)
        {
            util.UpdateFromPacket(resolver, packet.InventoryDoubleUpdate);
        }
    }
}

internal static class Patch_ServerMain_SendPacket
{
    public static bool Prefix(int clientId, Packet_Server packet)
    {
        if (packet == null) return true;

        if (packet.InventoryUpdate == null)
        {
            SyncDiagnostics.ServerSend(EchoSuppressor.Api, clientId, packet, "outgoing");
            return true;
        }

        SyncDiagnostics.ServerSend(EchoSuppressor.Api, clientId, packet, "outgoing");
        return true;
    }
}

internal sealed class DirtySnapshot
{
    private readonly Dictionary<InventoryBase, HashSet<int>> before;

    private DirtySnapshot(Dictionary<InventoryBase, HashSet<int>> before)
    {
        this.before = before;
    }

    public static DirtySnapshot Capture(IPlayer player)
    {
        Dictionary<InventoryBase, HashSet<int>> snapshot = new();

        foreach (IInventory rawInv in player.InventoryManager.Inventories.Values)
        {
            if (rawInv is InventoryBase inv)
            {
                snapshot[inv] = new HashSet<int>(inv.DirtySlots);
            }
        }

        return new DirtySnapshot(snapshot);
    }

    public IEnumerable<(InventoryBase inv, int slotId)> FindNewlyDirtySlots(IPlayer player)
    {
        foreach (IInventory rawInv in player.InventoryManager.Inventories.Values)
        {
            if (rawInv is not InventoryBase inv) continue;
            if (inv.DirtySlots.Count == 0) continue;

            before.TryGetValue(inv, out HashSet<int> oldDirty);

            foreach (int slotId in inv.DirtySlots)
            {
                if (oldDirty == null || !oldDirty.Contains(slotId))
                {
                    yield return (inv, slotId);
                }
            }
        }
    }
}

internal static class Patch_InventoryCraftingGrid_ActivateSlot
{
    public static bool Prefix(
        InventoryCraftingGrid __instance,
        int slotId,
        ref ItemStackMoveOperation op,
        ref object __result)
    {
        if (!op.ShiftDown || slotId != __instance.Count - 1)
        {
            return true;
        }

        ItemSlotCraftingOutput outputSlot = __instance[slotId] as ItemSlotCraftingOutput;
        __result = __instance.InvNetworkUtil.GetActivateSlotPacket(slotId, op);

        if (outputSlot == null || outputSlot.Empty || op.ActingPlayer == null)
        {
            return false;
        }

        bool beganCraft = CraftingOutputGuard.BeginCraftIfNeeded(__instance);

        try
        {
            op.RequestedQuantity = outputSlot.StackSize;
            op.ActingPlayer.InventoryManager.TryTransferAway(outputSlot, ref op, onlyPlayerInventory: false);
        }
        finally
        {
            CraftingOutputGuard.EndCraftIfNeeded(__instance, beganCraft);
        }

        return false;
    }
}

internal static class Patch_ItemSlotCraftingOutput_TryPutInto
{
    public static bool Prefix(
        ItemSlotCraftingOutput __instance,
        ItemSlot sinkSlot,
        ref ItemStackMoveOperation op,
        ref int __result)
    {
        if (!op.ShiftDown)
        {
            return true;
        }

        InventoryCraftingGrid inv = __instance.Inventory as InventoryCraftingGrid;
        if (inv == null)
        {
            return true;
        }

        bool beganCraft = CraftingOutputGuard.BeginCraftIfNeeded(inv);

        try
        {
            __result = CraftingOutputGuard.CraftManyFullOutputsOnly(__instance, sinkSlot, ref op);
        }
        finally
        {
            CraftingOutputGuard.EndCraftIfNeeded(inv, beganCraft);
        }

        return false;
    }
}

internal static class Patch_ItemSlotCraftingOutput_FlipWith
{
    public static bool Prefix(ItemSlotCraftingOutput __instance, ItemSlot withSlot)
    {
        InventoryCraftingGrid inv = __instance.Inventory as InventoryCraftingGrid;
        if (inv == null || __instance.Empty || withSlot == null)
        {
            return true;
        }

        bool beganCraft = CraftingOutputGuard.BeginCraftIfNeeded(inv);

        try
        {
            ItemStack craftedStack = __instance.Itemstack.Clone();
            ItemStackMoveOperation op = new(
                inv.Api.World,
                EnumMouseButton.Left,
                (EnumModifierKey)0,
                EnumMergePriority.AutoMerge,
                __instance.StackSize);
            op.ActingPlayer = inv.Player;

            if (!CraftingOutputGuard.FullOutputFits(__instance, withSlot, op))
            {
                CraftingOutputGuard.MarkDirtyForResync(__instance, withSlot);
                return false;
            }

            int moved = __instance.TryPutIntoNoEvent(withSlot, ref op);
            if (moved != craftedStack.StackSize)
            {
                CraftingOutputGuard.MarkDirtyForResync(__instance, withSlot);
                return false;
            }

            CraftingOutputGuard.ConsumeIngredients(inv, withSlot);
            CraftingOutputGuard.TriggerCrafted(craftedStack, moved, op.ActingPlayer ?? inv.Player);
            withSlot.OnItemSlotModified(withSlot.Itemstack);
            __instance.OnItemSlotModified(withSlot.Itemstack);
            return false;
        }
        finally
        {
            CraftingOutputGuard.EndCraftIfNeeded(inv, beganCraft);
        }
    }
}

internal static class Patch_InventoryCraftingGrid_FindMatchingRecipe
{
    public static void Postfix(InventoryCraftingGrid __instance)
    {
        if (__instance[__instance.Count - 1] is ItemSlotCraftingOutput outputSlot)
        {
            CraftingOutputGuard.ResetLeftoverState(outputSlot);
        }
    }
}

internal static class CraftingOutputGuard
{
    private static readonly FieldInfo IsCraftingField =
        AccessTools.Field(typeof(InventoryCraftingGrid), "isCrafting");

    private static readonly MethodInfo BeginCraftMethod =
        AccessTools.Method(typeof(InventoryCraftingGrid), "BeginCraft");

    private static readonly MethodInfo EndCraftMethod =
        AccessTools.Method(typeof(InventoryCraftingGrid), "EndCraft");

    private static readonly MethodInfo ConsumeIngredientsMethod =
        AccessTools.Method(typeof(InventoryCraftingGrid), "ConsumeIngredients");

    private static readonly FieldInfo HasLeftOversField =
        AccessTools.Field(typeof(ItemSlotCraftingOutput), "hasLeftOvers");

    private static readonly FieldInfo PrevStackField =
        AccessTools.Field(typeof(ItemSlotCraftingOutput), "prevStack");

    public static bool BeginCraftIfNeeded(InventoryCraftingGrid inv)
    {
        if ((bool)IsCraftingField.GetValue(inv))
        {
            return false;
        }

        BeginCraftMethod.Invoke(inv, Array.Empty<object>());
        return true;
    }

    public static void EndCraftIfNeeded(InventoryCraftingGrid inv, bool beganCraft)
    {
        if (beganCraft)
        {
            EndCraftMethod.Invoke(inv, Array.Empty<object>());
        }
    }

    public static void ConsumeIngredients(InventoryCraftingGrid inv, ItemSlot outputSinkSlot)
    {
        ConsumeIngredientsMethod.Invoke(inv, new object[] { outputSinkSlot });
    }

    public static void ResetLeftoverState(ItemSlotCraftingOutput outputSlot)
    {
        HasLeftOversField.SetValue(outputSlot, false);
        PrevStackField.SetValue(outputSlot, null);
    }

    public static int CraftManyFullOutputsOnly(
        ItemSlotCraftingOutput outputSlot,
        ItemSlot sinkSlot,
        ref ItemStackMoveOperation op)
    {
        if (outputSlot.Empty)
        {
            op.MovedQuantity = 0;
            return 0;
        }

        InventoryCraftingGrid inv = (InventoryCraftingGrid)outputSlot.Inventory;
        int movedTotal = 0;

        while (!outputSlot.Empty)
        {
            ItemStack craftedStack = outputSlot.Itemstack.Clone();
            int recipeOutputSize = outputSlot.StackSize;

            if (!FullOutputFits(outputSlot, sinkSlot, op))
            {
                MarkDirtyForResync(outputSlot, sinkSlot);
                break;
            }

            op.RequestedQuantity = recipeOutputSize;
            op.MovedQuantity = 0;

            int moved = outputSlot.TryPutIntoNoEvent(sinkSlot, ref op);
            if (moved != recipeOutputSize)
            {
                MarkDirtyForResync(outputSlot, sinkSlot);
                break;
            }

            movedTotal += moved;
            ConsumeIngredients(inv, sinkSlot);
            TriggerCrafted(craftedStack, moved, op.ActingPlayer ?? inv.Player);

            if (!inv.CanStillCraftCurrent())
            {
                break;
            }

            outputSlot.Itemstack = craftedStack.Clone();
        }

        op.MovedQuantity = movedTotal;

        if (movedTotal > 0)
        {
            sinkSlot.OnItemSlotModified(sinkSlot.Itemstack);
            outputSlot.OnItemSlotModified(sinkSlot.Itemstack);
        }

        return movedTotal;
    }

    public static void MarkDirtyForResync(ItemSlotCraftingOutput outputSlot, ItemSlot sinkSlot)
    {
        outputSlot.MarkDirty();
        sinkSlot?.MarkDirty();
    }

    public static bool FullOutputFits(
        ItemSlotCraftingOutput outputSlot,
        ItemSlot sinkSlot,
        ItemStackMoveOperation op)
    {
        if (outputSlot.Empty || sinkSlot == null || !outputSlot.CanTake() || !sinkSlot.CanTakeFrom(outputSlot))
        {
            return false;
        }

        if (sinkSlot.Inventory?.CanContain(sinkSlot, outputSlot) == false)
        {
            return false;
        }

        ItemStack outputStack = outputSlot.Itemstack;
        int needed = outputSlot.StackSize;
        int remainingSlotSpace = sinkSlot.GetRemainingSlotSpace(outputStack);

        if (sinkSlot.Itemstack == null)
        {
            return remainingSlotSpace >= needed;
        }

        int mergeable = sinkSlot.Itemstack.Collectible.GetMergableQuantity(
            sinkSlot.Itemstack,
            outputStack,
            op.CurrentPriority);

        return Math.Min(remainingSlotSpace, mergeable) >= needed;
    }

    public static void TriggerCrafted(ItemStack craftedStack, int moved, IPlayer actingPlayer)
    {
        if (actingPlayer?.Entity?.World?.Api == null || moved <= 0)
        {
            return;
        }

        craftedStack.StackSize = moved;
        TreeAttribute tree = new();
        tree["itemstack"] = new ItemstackAttribute(craftedStack);
        tree["byentityid"] = new LongAttribute(actingPlayer.Entity.EntityId);
        actingPlayer.Entity.World.Api.Event.PushEvent("onitemcrafted", tree);
    }
}
