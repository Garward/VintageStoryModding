using HarmonyLib;
using ResponsiveVS.Client.Preview;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace ResponsiveVS.Client.Patches;

public static class Patch_ClientPreviewRendering
{
    private static readonly AccessTools.FieldRef<GuiElementItemSlotGridBase, IInventory> GridInventoryRef =
        AccessTools.FieldRefAccess<GuiElementItemSlotGridBase, IInventory>("inventory");
    private static readonly AccessTools.FieldRef<GuiElementPassiveItemSlot, ItemSlot> PassiveSlotRef =
        AccessTools.FieldRefAccess<GuiElementPassiveItemSlot, ItemSlot>("slot");

    public static void GridRenderPrefix(
        GuiElementItemSlotGridBase __instance,
        out ClientInventoryPreviewStore.PreviewRenderScope __state)
    {
        __state = ClientInventoryPreviewStore.ApplyToInventory(__instance == null ? null : GridInventoryRef(__instance));
    }

    public static void GridRenderPostfix(ClientInventoryPreviewStore.PreviewRenderScope __state)
    {
        __state.Restore();
    }

    public static void PassiveSlotRenderPrefix(
        GuiElementPassiveItemSlot __instance,
        out ClientInventoryPreviewStore.PreviewRenderScope __state)
    {
        __state = ClientInventoryPreviewStore.ApplyToSlot(__instance == null ? null : PassiveSlotRef(__instance));
    }

    public static void PassiveSlotRenderPostfix(ClientInventoryPreviewStore.PreviewRenderScope __state)
    {
        __state.Restore();
    }
}
