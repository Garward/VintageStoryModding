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
using Vintagestory.API.MathTools;
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
        ItemSyncFixesConfigSystem.Load(api);
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
            prefix: new HarmonyMethod(typeof(Patch_GuiElementItemSlotGridBase_OnMouseDownOnElement), nameof(Patch_GuiElementItemSlotGridBase_OnMouseDownOnElement.Prefix)),
            postfix: new HarmonyMethod(typeof(Patch_GuiElementItemSlotGridBase_OnMouseDownOnElement), nameof(Patch_GuiElementItemSlotGridBase_OnMouseDownOnElement.Postfix)));
        harmony.Patch(
            AccessTools.Method(typeof(GuiElementItemSlotGridBase), nameof(GuiElementItemSlotGridBase.OnMouseMove), new[] { typeof(ICoreClientAPI), typeof(MouseEvent) }),
            prefix: new HarmonyMethod(typeof(Patch_GuiElementItemSlotGridBase_OnMouseMove), nameof(Patch_GuiElementItemSlotGridBase_OnMouseMove.Prefix)));
        harmony.Patch(
            AccessTools.Method(typeof(GuiElementItemSlotGridBase), nameof(GuiElementItemSlotGridBase.OnMouseUp), new[] { typeof(ICoreClientAPI), typeof(MouseEvent) }),
            prefix: new HarmonyMethod(typeof(Patch_GuiElementItemSlotGridBase_OnMouseUp), nameof(Patch_GuiElementItemSlotGridBase_OnMouseUp.Prefix)));
        harmony.Patch(
            AccessTools.Method(typeof(GuiElementItemSlotGridBase), nameof(GuiElementItemSlotGridBase.RenderInteractiveElements), new[] { typeof(float) }),
            postfix: new HarmonyMethod(typeof(Patch_GuiElementItemSlotGridBase_RenderInteractiveElements), nameof(Patch_GuiElementItemSlotGridBase_RenderInteractiveElements.Postfix)));
        ClientDoubleUpdatePatchInstaller.Patch(harmony);
        CraftingPatchInstaller.Patch(harmony);

        api.Logger.Notification(
            "[ItemSyncFixes] Client stale queued inventory update coalescing active. Crafting grid drag preview: {0}.",
            ItemSyncFixesConfigSystem.Config.EnableCraftingGridDragPreview ? "enabled" : "disabled");
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

public sealed class ItemSyncFixesConfig
{
    public bool EnableCraftingGridDragPreview { get; set; } = true;
    public bool EnablePacedCraftingGridDragCommit { get; set; } = true;
    public int CraftingGridDragCommitSlotsPerStep { get; set; } = 3;
    public int CraftingGridDragCommitStepDelayMs { get; set; } = 1;
}

internal static class ItemSyncFixesConfigSystem
{
    private const string ConfigFileName = "itemsyncfixes.json";

    public static ItemSyncFixesConfig Config { get; private set; } = new();

    public static void Load(ICoreAPI api)
    {
        try
        {
            Config = api.LoadModConfig<ItemSyncFixesConfig>(ConfigFileName) ?? new ItemSyncFixesConfig();
            api.StoreModConfig(Config, ConfigFileName);
        }
        catch (Exception ex)
        {
            Config = new ItemSyncFixesConfig();
            api.Logger.Error("[ItemSyncFixes] Failed to load config, using defaults: {0}", ex);
        }
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
            prefix: new HarmonyMethod(typeof(Patch_InventoryCraftingGrid_FindMatchingRecipe), nameof(Patch_InventoryCraftingGrid_FindMatchingRecipe.Prefix)),
            postfix: new HarmonyMethod(typeof(Patch_InventoryCraftingGrid_FindMatchingRecipe), nameof(Patch_InventoryCraftingGrid_FindMatchingRecipe.Postfix)),
            finalizer: new HarmonyMethod(typeof(Patch_InventoryCraftingGrid_FindMatchingRecipe), nameof(Patch_InventoryCraftingGrid_FindMatchingRecipe.Finalizer)));
    }

    private static void PatchIfFound(Harmony harmony, MethodBase target, HarmonyMethod prefix = null, HarmonyMethod postfix = null, HarmonyMethod finalizer = null)
    {
        if (target != null)
        {
            harmony.Patch(target, prefix: prefix, postfix: postfix, finalizer: finalizer);
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

internal readonly record struct ClientPredictionState(bool Track, string TargetBefore, string MouseBefore, long DiagnosticClickId, long StartedMs);

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

    public static void Slow(ICoreAPI api, long startedMs, string label, string detail)
    {
        if (!Enabled) return;
        if (startedMs <= 0) return;
        long elapsedMs = Environment.TickCount64 - startedMs;
        if (elapsedMs < 50) return;
        api?.Logger.Notification("[ISFDiag] SLOW {0} {1}ms {2}", label, elapsedMs, detail ?? "");
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
        long startedMs = Environment.TickCount64;
        __state = new ClientPredictionState(false, targetBefore, mouseBefore, diagnosticClickId, startedMs);
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
            __state = new ClientPredictionState(false, targetBefore, mouseBefore, diagnosticClickId, startedMs);
            return true;
        }

        __state = new ClientPredictionState(
            true,
            targetBefore,
            mouseBefore,
            diagnosticClickId,
            startedMs);
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
        SyncDiagnostics.Slow(
            api,
            __state.StartedMs,
            "SlotClick",
            string.Format(
                "{0}[{1}] target {2}->{3} mouse {4}->{5}",
                targetInv.InventoryID ?? "?",
                slotId,
                __state.TargetBefore,
                targetAfter,
                __state.MouseBefore,
                mouseAfter));
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

    public static bool Prefix(GuiElementItemSlotGridBase __instance, ICoreClientAPI api, MouseEvent args)
    {
        return !CraftingGridDragPreview.TryBegin(__instance, api, args);
    }

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

internal static class Patch_GuiElementItemSlotGridBase_OnMouseMove
{
    public static bool Prefix(GuiElementItemSlotGridBase __instance, ICoreClientAPI api, MouseEvent args)
    {
        return !CraftingGridDragPreview.TryMove(__instance, api, args);
    }
}

internal static class Patch_GuiElementItemSlotGridBase_OnMouseUp
{
    public static bool Prefix(GuiElementItemSlotGridBase __instance, ICoreClientAPI api, MouseEvent args)
    {
        return !CraftingGridDragPreview.TryEnd(__instance, api, args);
    }
}

internal static class Patch_GuiElementItemSlotGridBase_RenderInteractiveElements
{
    public static void Postfix(GuiElementItemSlotGridBase __instance, float deltaTime)
    {
        CraftingGridDragPreview.Render(__instance, deltaTime);
    }
}

internal static class CraftingGridDragPreview
{
    private const int DefaultCommitSlotsPerStep = 3;
    private const int DefaultCommitStepDelayMs = 1;

    private static readonly FieldInfo InventoryField = AccessTools.Field(typeof(GuiElementItemSlotGridBase), "inventory");
    private static readonly FieldInfo RenderedSlotsField = AccessTools.Field(typeof(GuiElementItemSlotGridBase), "renderedSlots");
    private static readonly FieldInfo HoverSlotIdField = AccessTools.Field(typeof(GuiElementItemSlotGridBase), "hoverSlotId");
    private static readonly FieldInfo HoverInvField = AccessTools.Field(typeof(GuiElementItemSlotGridBase), "hoverInv");
    private static readonly FieldInfo WasMouseDownOnSlotIndexField = AccessTools.Field(typeof(GuiElementItemSlotGridBase), "wasMouseDownOnSlotIndex");
    private static readonly FieldInfo DistributePrevField = AccessTools.Field(typeof(GuiElementItemSlotGridBase), "distributeStacksPrevStackSizeBySlotId");
    private static readonly FieldInfo DistributeAddedField = AccessTools.Field(typeof(GuiElementItemSlotGridBase), "distributeStacksAddedStackSizeBySlotId");
    private static readonly FieldInfo ReferenceDistributeStackField = AccessTools.Field(typeof(GuiElementItemSlotGridBase), "referenceDistributStack");
    private static readonly FieldInfo SendPacketHandlerField = AccessTools.Field(typeof(GuiElementItemSlotGridBase), "SendPacketHandler");
    private static readonly MethodInfo RedistributeStacksMethod = AccessTools.Method(typeof(GuiElementItemSlotGridBase), "RedistributeStacks");
    private static readonly MethodInfo FindMatchingRecipeMethod = AccessTools.Method(typeof(InventoryCraftingGrid), "FindMatchingRecipe");
    private static readonly Dictionary<GuiElementItemSlotGridBase, PreviewState> Active = new();

    public static bool TryBegin(GuiElementItemSlotGridBase elem, ICoreClientAPI api, MouseEvent args)
    {
        if (!ItemSyncFixesConfigSystem.Config.EnableCraftingGridDragPreview)
        {
            return false;
        }

        if (!CanUsePreview(elem, api, args) || !TryGetInventory(elem, out InventoryCraftingGrid inv))
        {
            return false;
        }

        bool shift = IsDown(api, GlKeys.ShiftLeft) || IsDown(api, GlKeys.ShiftRight);
        bool ctrl = IsDown(api, GlKeys.ControlLeft) || IsDown(api, GlKeys.ControlRight);
        bool alt = IsDown(api, GlKeys.AltLeft) || IsDown(api, GlKeys.AltRight);
        if (shift || ctrl || alt)
        {
            return false;
        }

        ItemStack mouseStack = api.World?.Player?.InventoryManager?.MouseItemSlot?.Itemstack;
        if (mouseStack == null)
        {
            return false;
        }

        if (!TryGetHoveredSlot(elem, args.X, args.Y, out int slotIndex, out int slotId))
        {
            return false;
        }

        if (!CanPreviewSlot(api, inv, slotId, mouseStack))
        {
            return false;
        }

        Active[elem] = new PreviewState(api, inv, args.Button, mouseStack.Clone());
        AddSlot(elem, Active[elem], slotIndex, slotId);
        args.Handled = true;

        SyncDiagnostics.Log(api, "CLIENT crafting drag preview begin {0}[{1}] button={2}", inv.InventoryID ?? "?", slotId, args.Button);
        return true;
    }

    public static bool TryMove(GuiElementItemSlotGridBase elem, ICoreClientAPI api, MouseEvent args)
    {
        if (!ItemSyncFixesConfigSystem.Config.EnableCraftingGridDragPreview)
        {
            Active.Remove(elem);
            return false;
        }

        if (!Active.TryGetValue(elem, out PreviewState state))
        {
            return false;
        }

        if (api?.Side != EnumAppSide.Client)
        {
            Active.Remove(elem);
            return false;
        }

        if (TryGetHoveredSlot(elem, args.X, args.Y, out int slotIndex, out int slotId))
        {
            ItemSlot hoverSlot = state.Inventory[slotId];
            if (!state.SlotIds.Contains(slotId) && CanPreviewSlot(api, state.Inventory, slotId, state.ReferenceStack))
            {
                AddSlot(elem, state, slotIndex, slotId);
                SyncDiagnostics.Log(api, "CLIENT crafting drag preview add {0}[{1}] count={2}", state.Inventory.InventoryID ?? "?", slotId, state.SlotIds.Count);
            }

            SetHover(elem, hoverSlot, slotId);
        }
        else
        {
            SetHover(elem, null, -1);
        }

        args.Handled = true;
        return true;
    }

    public static bool TryEnd(GuiElementItemSlotGridBase elem, ICoreClientAPI api, MouseEvent args)
    {
        if (!ItemSyncFixesConfigSystem.Config.EnableCraftingGridDragPreview)
        {
            Active.Remove(elem);
            return false;
        }

        if (!Active.TryGetValue(elem, out PreviewState state))
        {
            return false;
        }

        Active.Remove(elem);
        args.Handled = true;

        SetHover(elem, null, -1);
        state.Inventory.InvNetworkUtil.PauseInventoryUpdates = false;
        api.World.Player.InventoryManager.MouseItemSlot.Inventory.InvNetworkUtil.PauseInventoryUpdates = false;

        if (ShouldPaceCommit(state))
        {
            BeginPacedCommit(elem, api, state);
            SyncDiagnostics.Log(api, "CLIENT crafting drag preview queued paced apply {0} slots button={1}", state.SlotIds.Count, state.Button);
            return true;
        }

        int applied = state.Button == EnumMouseButton.Left
            ? ApplyLeftDrag(elem, api, state)
            : ApplySimpleDrag(elem, api, state);

        SyncDiagnostics.Log(api, "CLIENT crafting drag preview apply {0} slots button={1}", applied, state.Button);
        return true;
    }

    public static void Render(GuiElementItemSlotGridBase elem, float deltaTime)
    {
        if (!ItemSyncFixesConfigSystem.Config.EnableCraftingGridDragPreview)
        {
            return;
        }

        if (!Active.TryGetValue(elem, out PreviewState state) || state.SlotIndices.Count == 0)
        {
            return;
        }

        LoadedTexture highlight = elem.HighlightSlotTexture;
        if (highlight == null || highlight.TextureId == 0)
        {
            return;
        }

        for (int i = 0; i < state.SlotIndices.Count; i++)
        {
            int slotIndex = state.SlotIndices[i];
            if (slotIndex < 0 || slotIndex >= elem.SlotBounds.Length)
            {
                continue;
            }

            ElementBounds bounds = elem.SlotBounds[slotIndex];
            state.Api.Render.Render2DTexturePremultipliedAlpha(
                highlight.TextureId,
                (int)(bounds.renderX - 2.0),
                (int)(bounds.renderY - 2.0),
                bounds.OuterWidthInt + 4,
                bounds.OuterHeightInt + 4);

            if (!TryBuildPreviewSlot(state, i, out ItemSlot previewSlot))
            {
                continue;
            }

            state.Api.Render.RenderItemstackToGui(
                previewSlot,
                bounds.renderX + bounds.OuterWidth / 2.0,
                bounds.renderY + bounds.OuterHeight / 2.0,
                500.0,
                (float)GuiElement.scaled(GuiElementPassiveItemSlot.unscaledItemSize),
                ColorUtil.WhiteArgb,
                deltaTime,
                true,
                false,
                true);
        }
    }

    private static bool TryBuildPreviewSlot(PreviewState state, int previewIndex, out ItemSlot previewSlot)
    {
        previewSlot = null;
        if (previewIndex < 0 || previewIndex >= state.SlotIds.Count)
        {
            return false;
        }

        int slotId = state.SlotIds[previewIndex];
        ItemStack existingStack = state.Inventory[slotId]?.Itemstack;
        ItemStack previewStack = (existingStack ?? state.ReferenceStack).Clone();
        int existingSize = existingStack?.StackSize ?? 0;
        int addedSize = state.Button == EnumMouseButton.Right
            ? RightDragPreviewAddedSize(state, previewIndex)
            : LeftDragPreviewAddedSize(state, previewIndex);

        if (addedSize <= 0)
        {
            return false;
        }

        previewStack.StackSize = existingSize + addedSize;
        previewSlot = new ItemSlot(state.Inventory)
        {
            Itemstack = previewStack
        };
        return true;
    }

    private static int RightDragPreviewAddedSize(PreviewState state, int previewIndex)
    {
        return previewIndex < state.ReferenceStack.StackSize ? 1 : 0;
    }

    private static int LeftDragPreviewAddedSize(PreviewState state, int previewIndex)
    {
        int slotCount = Math.Max(1, state.SlotIds.Count);
        int evenShare = state.ReferenceStack.StackSize / slotCount;
        if (previewIndex < slotCount - 1)
        {
            return evenShare;
        }

        return state.ReferenceStack.StackSize - evenShare * (slotCount - 1);
    }

    private static int ApplySimpleDrag(GuiElementItemSlotGridBase elem, ICoreClientAPI api, PreviewState state)
    {
        int applied = 0;
        foreach (int slotId in state.SlotIds)
        {
            if (slotId < 0 || slotId >= state.Inventory.Count)
            {
                continue;
            }

            if (!CanPreviewSlot(api, state.Inventory, slotId, state.ReferenceStack))
            {
                continue;
            }

            elem.SlotClick(api, slotId, state.Button, false, false, false);
            applied++;
        }

        return applied;
    }

    private static bool ShouldPaceCommit(PreviewState state)
    {
        if (!ItemSyncFixesConfigSystem.Config.EnablePacedCraftingGridDragCommit)
        {
            return false;
        }

        return state.SlotIds.Count > CommitSlotsPerStep();
    }

    private static void BeginPacedCommit(GuiElementItemSlotGridBase elem, ICoreClientAPI api, PreviewState state)
    {
        PendingDragCommit commit = PendingDragCommit.Create(elem, api, state);
        ScheduleCommitStep(api, commit);
    }

    private static void ScheduleCommitStep(ICoreClientAPI api, PendingDragCommit commit)
    {
        api.Event.RegisterCallback(_ => ContinuePacedCommit(commit), CommitStepDelayMs(), true);
    }

    private static void ContinuePacedCommit(PendingDragCommit commit)
    {
        if (commit == null || commit.Api?.Side != EnumAppSide.Client)
        {
            return;
        }

        long startedMs = Environment.TickCount64;
        int appliedBefore = commit.Applied;
        int stepBudget = CommitSlotsPerStep();

        for (int i = 0; i < stepBudget && !commit.Done; i++)
        {
            commit.ApplyNext();
        }

        SyncDiagnostics.Slow(
            commit.Api,
            startedMs,
            "CraftingDragCommitStep",
            string.Format(
                "{0} applied {1}/{2} button={3}",
                commit.InventoryId,
                commit.Applied - appliedBefore,
                commit.TotalSlots,
                commit.Button));

        if (!commit.Done)
        {
            ScheduleCommitStep(commit.Api, commit);
            return;
        }

        commit.Finish();
    }

    private static int CommitSlotsPerStep()
    {
        return Math.Max(1, ItemSyncFixesConfigSystem.Config.CraftingGridDragCommitSlotsPerStep <= 0
            ? DefaultCommitSlotsPerStep
            : ItemSyncFixesConfigSystem.Config.CraftingGridDragCommitSlotsPerStep);
    }

    private static int CommitStepDelayMs()
    {
        return Math.Max(1, ItemSyncFixesConfigSystem.Config.CraftingGridDragCommitStepDelayMs <= 0
            ? DefaultCommitStepDelayMs
            : ItemSyncFixesConfigSystem.Config.CraftingGridDragCommitStepDelayMs);
    }

    private static int ApplyLeftDrag(GuiElementItemSlotGridBase elem, ICoreClientAPI api, PreviewState state)
    {
        if (state.SlotIds.Count == 0)
        {
            return 0;
        }

        if (TryGetSendPacketHandler(elem, out Action<object> sendPacket))
        {
            int applied = 0;
            for (int i = 0; i < state.SlotIds.Count; i++)
            {
                if (ApplyDirectLeftDragSlot(elem, api, state, i, sendPacket))
                {
                    applied++;
                }
            }

            ClearDistributionState(elem);
            RefreshCraftingGrid(state);
            return applied;
        }

        return ApplyLeftDragViaRedistribution(elem, api, state);
    }

    private static int ApplyLeftDragViaRedistribution(GuiElementItemSlotGridBase elem, ICoreClientAPI api, PreviewState state)
    {
        if (state.SlotIds.Count == 0 || RedistributeStacksMethod == null)
        {
            return 0;
        }

        var wasMouseDownOnSlotIndex = WasMouseDownOnSlotIndexField.GetValue(elem) as HashSet<int>;
        var distributePrev = DistributePrevField.GetValue(elem) as Vintagestory.API.Datastructures.OrderedDictionary<int, int>;
        var distributeAdded = DistributeAddedField.GetValue(elem) as Vintagestory.API.Datastructures.OrderedDictionary<int, int>;
        if (wasMouseDownOnSlotIndex == null || distributePrev == null || distributeAdded == null)
        {
            return ApplySimpleDrag(elem, api, state);
        }

        wasMouseDownOnSlotIndex.Clear();
        distributePrev.Clear();
        distributeAdded.Clear();
        ReferenceDistributeStackField.SetValue(elem, state.ReferenceStack.Clone());

        int applied = 0;
        for (int i = 0; i < state.SlotIds.Count; i++)
        {
            int slotId = state.SlotIds[i];
            int slotIndex = state.SlotIndices[i];
            if (slotId < 0 || slotId >= state.Inventory.Count || slotIndex < 0)
            {
                continue;
            }

            ItemStack stack = state.Inventory[slotId]?.Itemstack;
            if (stack != null && !stack.Equals(api.World, state.ReferenceStack, GlobalConstants.IgnoredStackAttributes))
            {
                continue;
            }

            wasMouseDownOnSlotIndex.Add(slotIndex);
            distributePrev[slotId] = state.Inventory[slotId].StackSize;

            int previousStackSize = state.Inventory[slotId].StackSize;
            if (api.World.Player.InventoryManager.MouseItemSlot.StackSize > 0)
            {
                elem.SlotClick(api, slotId, EnumMouseButton.Left, false, false, false);
                distributeAdded[slotId] = state.Inventory[slotId].StackSize - previousStackSize;
            }

            if (api.World.Player.InventoryManager.MouseItemSlot.StackSize <= 0)
            {
                RedistributeStacksMethod.Invoke(elem, new object[] { slotId });
            }

            applied++;
        }

        wasMouseDownOnSlotIndex.Clear();
        distributePrev.Clear();
        distributeAdded.Clear();
        RefreshCraftingGrid(state);
        return applied;
    }

    private static bool ApplyDirectLeftDragSlot(
        GuiElementItemSlotGridBase elem,
        ICoreClientAPI api,
        PreviewState state,
        int stateIndex,
        Action<object> sendPacket)
    {
        int slotId = state.SlotIds[stateIndex];
        if (slotId < 0 || slotId >= state.Inventory.Count)
        {
            return false;
        }

        if (!CanPreviewSlot(api, state.Inventory, slotId, state.ReferenceStack))
        {
            return false;
        }

        ItemSlot mouseSlot = api.World.Player.InventoryManager.MouseItemSlot;
        ItemSlot targetSlot = state.Inventory[slotId];
        int requested = LeftDragPreviewAddedSize(state, stateIndex);
        if (mouseSlot?.Itemstack == null || requested <= 0)
        {
            return false;
        }

        requested = Math.Min(requested, mouseSlot.StackSize);
        if (requested <= 0)
        {
            return false;
        }

        ItemStackMoveOperation op = new(
            api.World,
            EnumMouseButton.Left,
            (EnumModifierKey)0,
            EnumMergePriority.DirectMerge,
            requested);
        op.ActingPlayer = api.World.Player;

        object packet = api.World.Player.InventoryManager.TryTransferTo(mouseSlot, targetSlot, ref op);
        if (op.MovedQuantity <= 0)
        {
            return false;
        }

        SendPacket(sendPacket, packet);
        return true;
    }

    private static bool TryGetSendPacketHandler(GuiElementItemSlotGridBase elem, out Action<object> sendPacket)
    {
        sendPacket = SendPacketHandlerField?.GetValue(elem) as Action<object>;
        return sendPacket != null;
    }

    private static void SendPacket(Action<object> sendPacket, object packet)
    {
        if (sendPacket == null || packet == null)
        {
            return;
        }

        if (packet is object[] packets)
        {
            for (int i = 0; i < packets.Length; i++)
            {
                sendPacket(packets[i]);
            }

            return;
        }

        sendPacket(packet);
    }

    private static void ClearDistributionState(GuiElementItemSlotGridBase elem)
    {
        (WasMouseDownOnSlotIndexField.GetValue(elem) as HashSet<int>)?.Clear();
        (DistributePrevField.GetValue(elem) as Vintagestory.API.Datastructures.OrderedDictionary<int, int>)?.Clear();
        (DistributeAddedField.GetValue(elem) as Vintagestory.API.Datastructures.OrderedDictionary<int, int>)?.Clear();
    }

    private static void RefreshCraftingGrid(PreviewState state)
    {
        InventoryCraftingGrid inv = state?.Inventory;
        if (inv == null || FindMatchingRecipeMethod == null)
        {
            return;
        }

        long startedMs = Environment.TickCount64;
        FindMatchingRecipeMethod.Invoke(inv, Array.Empty<object>());

        SyncDiagnostics.Slow(
            inv.Api,
            startedMs,
            "CraftingDragRefresh",
            string.Format(
                "{0} output={1} inputs={2}",
                inv.InventoryID ?? "?",
                ClientPredictionSuppressor.Fingerprint(inv[inv.Count - 1]?.Itemstack),
                CraftingOutputGuard.InputSummary(inv)));
    }

    private static bool CanUsePreview(GuiElementItemSlotGridBase elem, ICoreClientAPI api, MouseEvent args)
    {
        if (api?.Side != EnumAppSide.Client || elem == null || args == null)
        {
            return false;
        }

        if (args.Button != EnumMouseButton.Left && args.Button != EnumMouseButton.Right)
        {
            return false;
        }

        return elem.Bounds?.ParentBounds != null && elem.Bounds.ParentBounds.PointInside(args.X, args.Y);
    }

    private static bool TryGetInventory(GuiElementItemSlotGridBase elem, out InventoryCraftingGrid inv)
    {
        inv = InventoryField.GetValue(elem) as InventoryCraftingGrid;
        return inv != null;
    }

    private static bool TryGetHoveredSlot(GuiElementItemSlotGridBase elem, double x, double y, out int slotIndex, out int slotId)
    {
        slotIndex = -1;
        slotId = -1;

        if (elem.SlotBounds == null)
        {
            return false;
        }

        var renderedSlots = RenderedSlotsField.GetValue(elem) as Vintagestory.API.Datastructures.OrderedDictionary<int, ItemSlot>;
        if (renderedSlots == null)
        {
            return false;
        }

        for (int i = 0; i < elem.SlotBounds.Length && i < renderedSlots.Count; i++)
        {
            if (!elem.SlotBounds[i].PointInside(x, y))
            {
                continue;
            }

            if (elem.CanClickSlot?.Invoke(i) == false)
            {
                return false;
            }

            slotIndex = i;
            slotId = renderedSlots.GetKeyAtIndex(i);
            return true;
        }

        return false;
    }

    private static bool CanPreviewSlot(ICoreClientAPI api, InventoryCraftingGrid inv, int slotId, ItemStack referenceStack)
    {
        if (api?.World == null || inv == null || referenceStack == null || slotId < 0 || slotId >= inv.Count - 1)
        {
            return false;
        }

        ItemStack stack = inv[slotId]?.Itemstack;
        return stack == null || stack.Equals(api.World, referenceStack, GlobalConstants.IgnoredStackAttributes);
    }

    private static void AddSlot(GuiElementItemSlotGridBase elem, PreviewState state, int slotIndex, int slotId)
    {
        state.SlotIndices.Add(slotIndex);
        state.SlotIds.Add(slotId);
        SetHover(elem, state.Inventory[slotId], slotId);
    }

    private static void SetHover(GuiElementItemSlotGridBase elem, ItemSlot slot, int slotId)
    {
        HoverSlotIdField?.SetValue(elem, slotId);
        HoverInvField?.SetValue(elem, slot?.Inventory);
    }

    private static bool IsDown(ICoreClientAPI api, GlKeys key)
    {
        return api?.Input?.KeyboardKeyState != null && api.Input.KeyboardKeyState[(int)key];
    }

    private sealed class PreviewState
    {
        public readonly ICoreClientAPI Api;
        public readonly InventoryCraftingGrid Inventory;
        public readonly EnumMouseButton Button;
        public readonly ItemStack ReferenceStack;
        public readonly List<int> SlotIndices = new();
        public readonly List<int> SlotIds = new();

        public PreviewState(ICoreClientAPI api, InventoryCraftingGrid inventory, EnumMouseButton button, ItemStack referenceStack)
        {
            Api = api;
            Inventory = inventory;
            Button = button;
            ReferenceStack = referenceStack;
        }
    }

    private sealed class PendingDragCommit
    {
        public readonly GuiElementItemSlotGridBase Element;
        public readonly ICoreClientAPI Api;
        public readonly PreviewState State;
        public readonly EnumMouseButton Button;
        public readonly int TotalSlots;
        public readonly string InventoryId;
        public readonly long CreatedMs;

        private HashSet<int> wasMouseDownOnSlotIndex;
        private Vintagestory.API.Datastructures.OrderedDictionary<int, int> distributePrev;
        private Vintagestory.API.Datastructures.OrderedDictionary<int, int> distributeAdded;
        private bool useLeftRedistribution;
        private Action<object> sendPacket;
        private int nextIndex;

        public int Applied { get; private set; }
        public bool Done => nextIndex >= State.SlotIds.Count;

        private PendingDragCommit(GuiElementItemSlotGridBase element, ICoreClientAPI api, PreviewState state)
        {
            Element = element;
            Api = api;
            State = state;
            Button = state.Button;
            TotalSlots = state.SlotIds.Count;
            InventoryId = state.Inventory.InventoryID ?? "?";
            CreatedMs = Environment.TickCount64;
        }

        public static PendingDragCommit Create(GuiElementItemSlotGridBase element, ICoreClientAPI api, PreviewState state)
        {
            PendingDragCommit commit = new(element, api, state);
            if (state.Button == EnumMouseButton.Left)
            {
                commit.BeginLeftRedistribution();
            }

            return commit;
        }

        public void ApplyNext()
        {
            if (Done)
            {
                return;
            }

            if (Button == EnumMouseButton.Left && useLeftRedistribution)
            {
                ApplyLeftNext();
            }
            else
            {
                ApplySimpleNext();
            }
        }

        public void Finish()
        {
            if (useLeftRedistribution)
            {
                wasMouseDownOnSlotIndex?.Clear();
                distributePrev?.Clear();
                distributeAdded?.Clear();
            }

            State.Inventory.InvNetworkUtil.PauseInventoryUpdates = false;
            Api.World.Player.InventoryManager.MouseItemSlot.Inventory.InvNetworkUtil.PauseInventoryUpdates = false;

            if (Button == EnumMouseButton.Left)
            {
                RefreshCraftingGrid(State);
            }

            SyncDiagnostics.Log(
                Api,
                "CLIENT crafting drag preview paced apply finished {0}/{1} slots button={2} elapsed={3}ms",
                Applied,
                TotalSlots,
                Button,
                Environment.TickCount64 - CreatedMs);
        }

        private void BeginLeftRedistribution()
        {
            if (TryGetSendPacketHandler(Element, out sendPacket))
            {
                useLeftRedistribution = false;
                ClearDistributionState(Element);
                return;
            }

            wasMouseDownOnSlotIndex = WasMouseDownOnSlotIndexField.GetValue(Element) as HashSet<int>;
            distributePrev = DistributePrevField.GetValue(Element) as Vintagestory.API.Datastructures.OrderedDictionary<int, int>;
            distributeAdded = DistributeAddedField.GetValue(Element) as Vintagestory.API.Datastructures.OrderedDictionary<int, int>;

            if (wasMouseDownOnSlotIndex == null || distributePrev == null || distributeAdded == null || RedistributeStacksMethod == null)
            {
                useLeftRedistribution = false;
                return;
            }

            wasMouseDownOnSlotIndex.Clear();
            distributePrev.Clear();
            distributeAdded.Clear();
            ReferenceDistributeStackField.SetValue(Element, State.ReferenceStack.Clone());
            useLeftRedistribution = true;
        }

        private void ApplySimpleNext()
        {
            int slotId = State.SlotIds[nextIndex++];
            if (slotId < 0 || slotId >= State.Inventory.Count)
            {
                return;
            }

            if (!CanPreviewSlot(Api, State.Inventory, slotId, State.ReferenceStack))
            {
                return;
            }

            if (Button == EnumMouseButton.Left && sendPacket != null)
            {
                if (ApplyDirectLeftDragSlot(Element, Api, State, nextIndex - 1, sendPacket))
                {
                    Applied++;
                }

                return;
            }

            Element.SlotClick(Api, slotId, Button, false, false, false);
            Applied++;
        }

        private void ApplyLeftNext()
        {
            int stateIndex = nextIndex++;
            int slotId = State.SlotIds[stateIndex];
            int slotIndex = State.SlotIndices[stateIndex];
            if (slotId < 0 || slotId >= State.Inventory.Count || slotIndex < 0)
            {
                return;
            }

            ItemStack stack = State.Inventory[slotId]?.Itemstack;
            if (stack != null && !stack.Equals(Api.World, State.ReferenceStack, GlobalConstants.IgnoredStackAttributes))
            {
                return;
            }

            wasMouseDownOnSlotIndex.Add(slotIndex);
            distributePrev[slotId] = State.Inventory[slotId].StackSize;

            int previousStackSize = State.Inventory[slotId].StackSize;
            ItemSlot mouseSlot = Api.World.Player.InventoryManager.MouseItemSlot;
            if (mouseSlot?.StackSize > 0)
            {
                Element.SlotClick(Api, slotId, EnumMouseButton.Left, false, false, false);
                distributeAdded[slotId] = State.Inventory[slotId].StackSize - previousStackSize;
            }

            if (mouseSlot?.StackSize <= 0)
            {
                RedistributeStacksMethod.Invoke(Element, new object[] { slotId });
            }

            Applied++;
        }
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
    private static readonly MethodInfo FindMatchingRecipeMethod = AccessTools.Method(typeof(InventoryCraftingGrid), "FindMatchingRecipe");

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

    public static void Postfix(InventoryNetworkUtil __instance, IPlayer byPlayer, int packetId, Packet_Client packet, DirtySnapshot __state)
    {
        if (__instance?.Api?.Side != EnumAppSide.Server || byPlayer == null || packetId != 8 || packet?.MoveItemstack == null)
        {
            return;
        }

        RefreshCraftingGridAfterMove(__instance.Api, byPlayer, packet.MoveItemstack.SourceInventoryId);
        if (packet.MoveItemstack.TargetInventoryId != packet.MoveItemstack.SourceInventoryId)
        {
            RefreshCraftingGridAfterMove(__instance.Api, byPlayer, packet.MoveItemstack.TargetInventoryId);
        }
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

    private static void RefreshCraftingGridAfterMove(ICoreAPI api, IPlayer byPlayer, string inventoryId)
    {
        if (string.IsNullOrEmpty(inventoryId) || FindMatchingRecipeMethod == null)
        {
            return;
        }

        InventoryCraftingGrid inv = byPlayer.InventoryManager?.GetInventory(inventoryId) as InventoryCraftingGrid;
        if (inv == null)
        {
            return;
        }

        long startedMs = Environment.TickCount64;
        FindMatchingRecipeMethod.Invoke(inv, Array.Empty<object>());

        SyncDiagnostics.Slow(
            api,
            startedMs,
            "ServerCraftingMoveRefresh",
            string.Format(
                "{0} player={1} output={2} inputs={3}",
                inv.InventoryID ?? "?",
                byPlayer.PlayerName,
                ClientPredictionSuppressor.Fingerprint(inv[inv.Count - 1]?.Itemstack),
                CraftingOutputGuard.InputSummary(inv)));
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
    public readonly record struct FindMatchingRecipeState(long StartedMs, bool HadOutput);

    public static void Prefix(InventoryCraftingGrid __instance, out FindMatchingRecipeState __state)
    {
        __state = new FindMatchingRecipeState(
            Environment.TickCount64,
            __instance?.Count > 0 && !__instance[__instance.Count - 1].Empty);
    }

    public static void Postfix(InventoryCraftingGrid __instance, FindMatchingRecipeState __state)
    {
        CraftingOutputGuard.SuppressSpuriousEmptyOutputDirty(__instance, __state.HadOutput);

        SyncDiagnostics.Slow(
            __instance.Api,
            __state.StartedMs,
            "FindMatchingRecipe",
            string.Format(
                "{0} side={1} recipe={2} output={3} inputs={4}",
                __instance.InventoryID ?? "?",
                __instance.Api?.Side,
                __instance.MatchingRecipe?.Name?.ToString() ?? "none",
                ClientPredictionSuppressor.Fingerprint(__instance[__instance.Count - 1]?.Itemstack),
                CraftingOutputGuard.InputSummary(__instance)));
    }

    public static Exception Finalizer(InventoryCraftingGrid __instance, Exception __exception)
    {
        if (__exception == null)
        {
            return null;
        }

        if (!CraftingOutputGuard.IsMissingOutputResultException(__exception, out string message))
        {
            return __exception;
        }

        CraftingOutputGuard.ClearErroredOutput(__instance);
        __instance?.Api?.Logger.Warning(
            "[ItemSyncFixes] Suppressed crafting output generation error during FindMatchingRecipe for {0}: {1}",
            __instance?.InventoryID ?? "?",
            message);
        return null;
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

        long startedMs = Environment.TickCount64;
        BeginCraftMethod.Invoke(inv, Array.Empty<object>());
        SyncDiagnostics.Slow(
            inv.Api,
            startedMs,
            "BeginCraft",
            inv.InventoryID ?? "?");
        return true;
    }

    public static void EndCraftIfNeeded(InventoryCraftingGrid inv, bool beganCraft)
    {
        if (!beganCraft)
        {
            return;
        }

        try
        {
            long startedMs = Environment.TickCount64;
            EndCraftMethod.Invoke(inv, Array.Empty<object>());
            SyncDiagnostics.Slow(
                inv.Api,
                startedMs,
                "EndCraft",
                string.Format(
                    "{0} recipe={1} output={2} inputs={3}",
                    inv.InventoryID ?? "?",
                    inv.MatchingRecipe?.Name?.ToString() ?? "none",
                    ClientPredictionSuppressor.Fingerprint(inv[inv.Count - 1]?.Itemstack),
                    InputSummary(inv)));
        }
        catch (TargetInvocationException ex) when (IsMissingOutputResultException(ex, out _))
        {
            IsMissingOutputResultException(ex, out string message);
            ClearErroredOutput(inv);
            inv.Api?.Logger.Warning(
                "[ItemSyncFixes] Suppressed crafting output generation error during EndCraft for {0}: {1}",
                inv.InventoryID ?? "?",
                message);
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

    public static void SuppressSpuriousEmptyOutputDirty(InventoryCraftingGrid inv, bool hadOutput)
    {
        if (inv?.Api?.Side != EnumAppSide.Server || inv.Count == 0 || hadOutput || inv.MatchingRecipe != null)
        {
            return;
        }

        int outputSlotId = inv.Count - 1;
        if (inv[outputSlotId]?.Itemstack != null)
        {
            return;
        }

        // Vanilla marks the output dirty even when FindMatchingRecipe() only reconfirms
        // "no recipe". If the dirty flush lands between fast grid placements, that empty
        // server update can overwrite the client's newly predicted valid output.
        inv.dirtySlots.Remove(outputSlotId);
    }

    public static bool IsMissingOutputResultException(Exception exception, out string message)
    {
        Exception unwrapped = exception is TargetInvocationException targetException && targetException.InnerException != null
            ? targetException.InnerException
            : exception;

        if (unwrapped is InvalidOperationException invalid
            && invalid.Message.StartsWith("Missing or errored output result for recipe", StringComparison.Ordinal))
        {
            message = invalid.Message;
            return true;
        }

        message = null;
        return false;
    }

    public static void ClearErroredOutput(InventoryCraftingGrid inv)
    {
        if (inv == null || inv.Count == 0)
        {
            return;
        }

        ItemSlotCraftingOutput outputSlot = inv[inv.Count - 1] as ItemSlotCraftingOutput;
        if (outputSlot != null)
        {
            outputSlot.Itemstack = null;
            ResetLeftoverState(outputSlot);
            outputSlot.MarkDirty();
            inv.MarkSlotDirty(inv.Count - 1);
        }

        inv.MatchingRecipe = null;
    }

    public static string InputSummary(InventoryCraftingGrid inv)
    {
        if (inv == null)
        {
            return "?";
        }

        List<string> parts = new();
        int lastInputSlot = Math.Max(0, inv.Count - 1);
        for (int slotId = 0; slotId < lastInputSlot; slotId++)
        {
            ItemStack stack = inv[slotId]?.Itemstack;
            if (stack == null)
            {
                continue;
            }

            parts.Add(slotId + ":" + ClientPredictionSuppressor.Fingerprint(stack));
        }

        return parts.Count == 0 ? "empty" : string.Join(",", parts);
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
        long startedMs = Environment.TickCount64;
        int movedTotal = 0;
        int iterations = 0;
        ItemStack craftedEventStack = null;

        while (!outputSlot.Empty)
        {
            iterations++;
            ItemStack craftedStack = outputSlot.Itemstack.Clone();
            craftedEventStack ??= craftedStack.Clone();
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

            if (!inv.CanStillCraftCurrent())
            {
                break;
            }

            outputSlot.Itemstack = craftedStack.Clone();
        }

        op.MovedQuantity = movedTotal;

        if (movedTotal > 0)
        {
            TriggerCrafted(craftedEventStack, movedTotal, op.ActingPlayer ?? inv.Player);
            sinkSlot.OnItemSlotModified(sinkSlot.Itemstack);
            outputSlot.OnItemSlotModified(sinkSlot.Itemstack);
        }

        SyncDiagnostics.Slow(
            inv.Api,
            startedMs,
            "CraftManyFullOutputsOnly",
            string.Format(
                "{0} moved={1} iterations={2} sink={3}",
                inv.InventoryID ?? "?",
                movedTotal,
                iterations,
                ClientPredictionSuppressor.Fingerprint(sinkSlot?.Itemstack)));

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
