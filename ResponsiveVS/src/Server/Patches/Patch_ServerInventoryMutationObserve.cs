using ResponsiveVS.Diagnostics;
using Vintagestory.API.Common;

namespace ResponsiveVS.Server.Patches;

public static class Patch_ServerInventoryMutationObserve
{
    public static void TryMoveItemStackPrefix(
        InventoryBase __instance,
        IPlayer player,
        string[] invIds,
        int[] slotIds,
        ref ItemStackMoveOperation op,
        out MoveObserveState __state)
    {
        __state = CaptureMove(__instance, player, invIds, slotIds, ref op);
    }

    public static void TryMoveItemStackPostfix(MoveObserveState __state, ref ItemStackMoveOperation op, bool __result)
    {
        if (__state == null || !ResponsiveDiagnostics.VerboseEnabled)
        {
            return;
        }

        ResponsiveDiagnostics.Verbose(
            "SERVER move-result player={0} handlerInv={1} result={2} source={3}[{4}] {5}->{6} target={7}[{8}] {9}->{10} requested={11}->{12} moved={13}->{14}",
            __state.PlayerName,
            __state.HandlerInventoryId,
            __result,
            __state.SourceInventoryId,
            __state.SourceSlotId,
            __state.SourceBefore,
            InventoryDiagFormat.Slot(__state.SourceSlot),
            __state.TargetInventoryId,
            __state.TargetSlotId,
            __state.TargetBefore,
            InventoryDiagFormat.Slot(__state.TargetSlot),
            __state.RequestedBefore,
            op.RequestedQuantity,
            __state.MovedBefore,
            op.MovedQuantity);
    }

    public static void TryFlipItemStackPrefix(
        InventoryBase __instance,
        IPlayer owningPlayer,
        string[] invIds,
        int[] slotIds,
        long[] lastChanged,
        out MoveObserveState __state)
    {
        ItemStackMoveOperation op = null;
        __state = CaptureMove(__instance, owningPlayer, invIds, slotIds, ref op);
    }

    public static void TryFlipItemStackPostfix(MoveObserveState __state, bool __result)
    {
        if (__state == null || !ResponsiveDiagnostics.VerboseEnabled)
        {
            return;
        }

        ResponsiveDiagnostics.Verbose(
            "SERVER flip-result player={0} handlerInv={1} result={2} source={3}[{4}] {5}->{6} target={7}[{8}] {9}->{10}",
            __state.PlayerName,
            __state.HandlerInventoryId,
            __result,
            __state.SourceInventoryId,
            __state.SourceSlotId,
            __state.SourceBefore,
            InventoryDiagFormat.Slot(__state.SourceSlot),
            __state.TargetInventoryId,
            __state.TargetSlotId,
            __state.TargetBefore,
            InventoryDiagFormat.Slot(__state.TargetSlot));
    }

    public static void DidModifyItemSlotPostfix(InventoryBase __instance, ItemSlot slot)
    {
        if (!ResponsiveDiagnostics.VerboseEnabled || __instance?.Api?.Side != EnumAppSide.Server)
        {
            return;
        }

        ResponsiveDiagnostics.Verbose(
            "SERVER slot-modified inv={0}[{1}] stack={2} last={3}",
            __instance.InventoryID,
            __instance.GetSlotId(slot),
            InventoryDiagFormat.Slot(slot),
            __instance.LastChanged);
    }

    public static void MarkSlotDirtyPostfix(InventoryBase __instance, int slotId)
    {
        if (!ResponsiveDiagnostics.TraceEnabled || __instance?.Api?.Side != EnumAppSide.Server)
        {
            return;
        }

        ResponsiveDiagnostics.Trace(
            "SERVER dirty inv={0}[{1}] stack={2} dirtyCount={3}",
            __instance.InventoryID,
            slotId,
            SafeSlot(__instance, slotId) == null ? "missing" : InventoryDiagFormat.Slot(__instance[slotId]),
            __instance.DirtySlots?.Count ?? -1);
    }

    private static MoveObserveState CaptureMove(
        InventoryBase handlerInventory,
        IPlayer player,
        string[] invIds,
        int[] slotIds,
        ref ItemStackMoveOperation op)
    {
        if (!ResponsiveDiagnostics.VerboseEnabled || handlerInventory?.Api?.Side != EnumAppSide.Server)
        {
            return null;
        }

        ItemSlot sourceSlot = ResolveSlot(player, invIds, slotIds, 0);
        ItemSlot targetSlot = ResolveSlot(player, invIds, slotIds, 1);

        return new MoveObserveState
        {
            PlayerName = player?.PlayerName,
            HandlerInventoryId = handlerInventory.InventoryID,
            SourceInventoryId = invIds != null && invIds.Length > 0 ? invIds[0] : "missing",
            SourceSlotId = slotIds != null && slotIds.Length > 0 ? slotIds[0] : -1,
            TargetInventoryId = invIds != null && invIds.Length > 1 ? invIds[1] : "missing",
            TargetSlotId = slotIds != null && slotIds.Length > 1 ? slotIds[1] : -1,
            SourceSlot = sourceSlot,
            TargetSlot = targetSlot,
            SourceBefore = InventoryDiagFormat.Slot(sourceSlot),
            TargetBefore = InventoryDiagFormat.Slot(targetSlot),
            RequestedBefore = op?.RequestedQuantity ?? 0,
            MovedBefore = op?.MovedQuantity ?? 0
        };
    }

    private static ItemSlot ResolveSlot(IPlayer player, string[] invIds, int[] slotIds, int index)
    {
        if (player == null || invIds == null || slotIds == null || invIds.Length <= index || slotIds.Length <= index)
        {
            return null;
        }

        IInventory inventory = player.InventoryManager.GetInventory(invIds[index]);
        int slotId = slotIds[index];
        if (inventory == null || slotId < 0 || slotId >= inventory.Count)
        {
            return null;
        }

        return inventory[slotId];
    }

    private static ItemSlot SafeSlot(InventoryBase inventory, int slotId)
    {
        if (inventory == null || slotId < 0 || slotId >= inventory.Count)
        {
            return null;
        }

        return inventory[slotId];
    }

    public sealed class MoveObserveState
    {
        public string PlayerName;
        public string HandlerInventoryId;
        public string SourceInventoryId;
        public int SourceSlotId;
        public string TargetInventoryId;
        public int TargetSlotId;
        public ItemSlot SourceSlot;
        public ItemSlot TargetSlot;
        public string SourceBefore;
        public string TargetBefore;
        public int RequestedBefore;
        public int MovedBefore;
    }
}
