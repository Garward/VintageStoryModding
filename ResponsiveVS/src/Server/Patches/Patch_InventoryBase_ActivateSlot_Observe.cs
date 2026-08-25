using ResponsiveVS.Diagnostics;
using Vintagestory.API.Common;

namespace ResponsiveVS.Server.Patches;

public static class Patch_InventoryBase_ActivateSlot_Observe
{
    public static void Prefix(
        InventoryBase __instance,
        int slotId,
        ItemSlot sourceSlot,
        ref ItemStackMoveOperation op,
        out ServerActivateObserveState __state)
    {
        __state = Capture(__instance, slotId, sourceSlot, ref op);
    }

    public static void Postfix(ServerActivateObserveState __state, ref ItemStackMoveOperation op)
    {
        Flush(__state, ref op);
    }

    private static ServerActivateObserveState Capture(
        InventoryBase inventory,
        int slotId,
        ItemSlot sourceSlot,
        ref ItemStackMoveOperation op)
    {
        if (!ResponsiveDiagnostics.BasicEnabled || inventory?.Api?.Side != EnumAppSide.Server)
        {
            return null;
        }

        ItemSlot targetSlot = SafeSlot(inventory, slotId);
        return new ServerActivateObserveState
        {
            InventoryId = inventory.InventoryID,
            SlotId = slotId,
            MouseButton = op.MouseButton,
            ShiftPressed = op.ShiftDown,
            CtrlPressed = op.CtrlDown,
            AltPressed = op.AltDown,
            RequestedQuantityBefore = op.RequestedQuantity,
            MovedQuantityBefore = op.MovedQuantity,
            TargetSlot = targetSlot,
            SourceSlot = sourceSlot,
            TargetBefore = InventoryDiagFormat.Slot(targetSlot),
            SourceBefore = InventoryDiagFormat.Slot(sourceSlot),
            PlayerName = op.ActingPlayer?.PlayerName
        };
    }

    private static void Flush(ServerActivateObserveState state, ref ItemStackMoveOperation op)
    {
        if (state == null || !ResponsiveDiagnostics.BasicEnabled)
        {
            return;
        }

        ResponsiveDiagnostics.Basic(
            "SERVER observe activateslot player={0} inv={1}[{2}] button={3} shift={4} ctrl={5} alt={6} target={7}->{8} source={9}->{10} requested={11}->{12} moved={13}->{14}",
            state.PlayerName,
            state.InventoryId,
            state.SlotId,
            state.MouseButton,
            state.ShiftPressed,
            state.CtrlPressed,
            state.AltPressed,
            state.TargetBefore,
            InventoryDiagFormat.Slot(state.TargetSlot),
            state.SourceBefore,
            InventoryDiagFormat.Slot(state.SourceSlot),
            state.RequestedQuantityBefore,
            op.RequestedQuantity,
            state.MovedQuantityBefore,
            op.MovedQuantity);
    }

    private static ItemSlot SafeSlot(InventoryBase inventory, int slotId)
    {
        if (inventory == null || slotId < 0 || slotId >= inventory.Count)
        {
            return null;
        }

        return inventory[slotId];
    }

    public sealed class ServerActivateObserveState
    {
        public string InventoryId;
        public int SlotId;
        public EnumMouseButton MouseButton;
        public bool ShiftPressed;
        public bool CtrlPressed;
        public bool AltPressed;
        public int RequestedQuantityBefore;
        public int MovedQuantityBefore;
        public ItemSlot TargetSlot;
        public ItemSlot SourceSlot;
        public string TargetBefore;
        public string SourceBefore;
        public string PlayerName;
    }
}
