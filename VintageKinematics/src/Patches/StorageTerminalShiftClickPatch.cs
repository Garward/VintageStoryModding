using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using VintageKinematics.Gui.Storage;

namespace VintageKinematics.Patches
{
    /// <summary>Lets an open terminal claim Shift-click before vanilla inventory routing.</summary>
    [HarmonyPatch(typeof(GuiElementItemSlotGridBase), nameof(GuiElementItemSlotGridBase.SlotClick))]
    internal static class StorageTerminalShiftClickPatch
    {
        public static bool Prefix(
            GuiElementItemSlotGridBase __instance,
            int slotId,
            EnumMouseButton mouseButton,
            bool shiftPressed)
        {
            if (!shiftPressed || mouseButton != EnumMouseButton.Left) return true;
            if (!__instance.renderedSlots.TryGetValue(slotId, out ItemSlot slot)) return true;
            return !StorageTerminalShiftClickRouter.TryDeposit(slot);
        }
    }
}
