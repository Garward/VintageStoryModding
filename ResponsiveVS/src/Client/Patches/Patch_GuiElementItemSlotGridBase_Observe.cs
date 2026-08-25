using HarmonyLib;
using ResponsiveVS.Client.Preview;
using ResponsiveVS.Diagnostics;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace ResponsiveVS.Client.Patches;

public static class Patch_GuiElementItemSlotGridBase_Observe
{
    private static readonly AccessTools.FieldRef<GuiElementItemSlotGridBase, IInventory> InventoryRef =
        AccessTools.FieldRefAccess<GuiElementItemSlotGridBase, IInventory>("inventory");
    private static readonly AccessTools.FieldRef<GuiElement, ICoreClientAPI> ApiRef =
        AccessTools.FieldRefAccess<GuiElement, ICoreClientAPI>("api");

    public static bool SlotClickPrefix(
        GuiElementItemSlotGridBase __instance,
        ICoreClientAPI api,
        int slotId,
        EnumMouseButton mouseButton,
        bool shiftPressed,
        bool ctrlPressed,
        bool altPressed,
        out SlotObserveState __state)
    {
        __state = Capture(__instance, api, slotId, mouseButton, shiftPressed, ctrlPressed, altPressed, "slotclick");
        return !ClientPreviewClickHandler.TryHandleSlotClick(__instance, api, slotId, mouseButton, shiftPressed, ctrlPressed, altPressed);
    }

    public static bool OnMouseDownOnElementPrefix(
        GuiElementItemSlotGridBase __instance,
        ICoreClientAPI api,
        MouseEvent args)
    {
        if (CraftingGridDragPreview.TryBegin(__instance, api, args))
        {
            return false;
        }

        ClientPreviewClickHandler.BeginVanillaDragIfNeeded(__instance, api, args);
        return true;
    }

    public static bool OnMouseMovePrefix(
        GuiElementItemSlotGridBase __instance,
        ICoreClientAPI api,
        MouseEvent args)
    {
        return !CraftingGridDragPreview.TryMove(__instance, api, args);
    }

    public static bool OnMouseUpPrefix(
        GuiElementItemSlotGridBase __instance,
        ICoreClientAPI api,
        MouseEvent args)
    {
        return !CraftingGridDragPreview.TryEnd(__instance, api, args);
    }

    public static void OnMouseUpPostfix(GuiElementItemSlotGridBase __instance)
    {
        ClientPreviewClickHandler.EndVanillaDrag(__instance);
    }

    public static void SlotClickPostfix(SlotObserveState __state)
    {
        Flush(__state);
    }

    public static void RenderPostfix(GuiElementItemSlotGridBase __instance, float deltaTime)
    {
        CraftingGridDragPreview.Render(__instance, deltaTime);
    }

    public static void SlotMouseWheelPrefix(
        GuiElementItemSlotGridBase __instance,
        int slotId,
        int wheelDelta,
        out SlotObserveState __state)
    {
        ICoreClientAPI api = __instance == null ? null : ApiRef(__instance);
        __state = Capture(__instance, api, slotId, EnumMouseButton.Wheel, false, false, false, wheelDelta > 0 ? "wheel-up" : "wheel-down");
    }

    public static void SlotMouseWheelPostfix(SlotObserveState __state)
    {
        Flush(__state);
    }

    private static SlotObserveState Capture(
        GuiElementItemSlotGridBase instance,
        ICoreClientAPI api,
        int slotId,
        EnumMouseButton mouseButton,
        bool shiftPressed,
        bool ctrlPressed,
        bool altPressed,
        string source)
    {
        if (!ResponsiveDiagnostics.BasicEnabled || instance == null || api?.World?.Player == null)
        {
            return null;
        }

        IInventory inventory = InventoryRef(instance);
        ItemSlot targetSlot = SafeSlot(inventory, slotId);
        ItemSlot mouseSlot = api.World.Player.InventoryManager.MouseItemSlot;

        return new SlotObserveState
        {
            Source = source,
            InventoryId = inventory?.InventoryID ?? "missing",
            SlotId = slotId,
            MouseButton = mouseButton,
            ShiftPressed = shiftPressed,
            CtrlPressed = ctrlPressed,
            AltPressed = altPressed,
            TargetSlot = targetSlot,
            MouseSlot = mouseSlot,
            TargetBefore = InventoryDiagFormat.Slot(targetSlot),
            MouseBefore = InventoryDiagFormat.Slot(mouseSlot),
            OpenedInventoryCount = api.World.Player.InventoryManager.OpenedInventories?.Count ?? -1
        };
    }

    private static void Flush(SlotObserveState state)
    {
        if (state == null || !ResponsiveDiagnostics.BasicEnabled)
        {
            return;
        }

        state.TargetAfter = InventoryDiagFormat.Slot(state.TargetSlot);
        state.MouseAfter = InventoryDiagFormat.Slot(state.MouseSlot);

        ResponsiveDiagnostics.Basic(
            "CLIENT observe {0} inv={1}[{2}] button={3} shift={4} ctrl={5} alt={6} target={7}->{8} mouse={9}->{10} opened={11}",
            state.Source,
            state.InventoryId,
            state.SlotId,
            state.MouseButton,
            state.ShiftPressed,
            state.CtrlPressed,
            state.AltPressed,
            state.TargetBefore,
            state.TargetAfter,
            state.MouseBefore,
            state.MouseAfter,
            state.OpenedInventoryCount);
    }

    private static ItemSlot SafeSlot(IInventory inventory, int slotId)
    {
        if (inventory == null || slotId < 0 || slotId >= inventory.Count)
        {
            return null;
        }

        return inventory[slotId];
    }

    public sealed class SlotObserveState
    {
        public string Source;
        public string InventoryId;
        public int SlotId;
        public EnumMouseButton MouseButton;
        public bool ShiftPressed;
        public bool CtrlPressed;
        public bool AltPressed;
        public ItemSlot TargetSlot;
        public ItemSlot MouseSlot;
        public string TargetBefore;
        public string TargetAfter;
        public string MouseBefore;
        public string MouseAfter;
        public int OpenedInventoryCount;
    }
}
