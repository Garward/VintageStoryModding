using System;
using System.Collections.Generic;
using HarmonyLib;
using ResponsiveVS.Config;
using ResponsiveVS.Diagnostics;
using ResponsiveVS.Transactions;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.Common;

namespace ResponsiveVS.Client.Preview;

public static class ClientPreviewClickHandler
{
    private static readonly HashSet<GuiElementItemSlotGridBase> VanillaDragGrids = new();

    private static readonly AccessTools.FieldRef<GuiElementItemSlotGridBase, IInventory> InventoryRef =
        AccessTools.FieldRefAccess<GuiElementItemSlotGridBase, IInventory>("inventory");
    private static readonly AccessTools.FieldRef<GuiElementItemSlotGridBase, Action<object>> SendPacketHandlerRef =
        AccessTools.FieldRefAccess<GuiElementItemSlotGridBase, Action<object>>("SendPacketHandler");

    public static bool TryHandleSlotClick(
        GuiElementItemSlotGridBase grid,
        ICoreClientAPI api,
        int slotId,
        EnumMouseButton mouseButton,
        bool shiftPressed,
        bool ctrlPressed,
        bool altPressed)
    {
        if (!ResponsiveVSConfigSystem.Config.Transactions.EnableClientPreviewOnlyClicks)
        {
            return false;
        }

        if (IsVanillaDragActive(grid))
        {
            ResponsiveDiagnostics.Verbose("CLIENT preview bypass drag gesture");
            return false;
        }

        IInventory inventory = grid == null ? null : InventoryRef(grid);
        if (api?.World?.Player == null || inventory == null || slotId < 0 || slotId >= inventory.Count)
        {
            return false;
        }

        if (ShouldBypassPreviewOwnership(inventory, out string bypassReason))
        {
            ResponsiveDiagnostics.Basic(
                "CLIENT preview bypass inv={0} type={1} reason={2}",
                inventory.InventoryID,
                inventory.GetType().FullName,
                bypassReason);
            return false;
        }

        InventoryBase inventoryBase = inventory as InventoryBase;
        if (inventoryBase?.InvNetworkUtil == null)
        {
            return false;
        }

        ItemSlot targetSlot = inventory[slotId];
        ItemSlot mouseSlot = api.World.Player.InventoryManager.MouseItemSlot;
        if (targetSlot == null || mouseSlot == null)
        {
            return false;
        }

        EnumModifierKey modifiers =
            (shiftPressed ? EnumModifierKey.SHIFT : 0) |
            (ctrlPressed ? EnumModifierKey.CTRL : 0) |
            (altPressed ? EnumModifierKey.ALT : 0);

        ItemStackMoveOperation packetOp = new ItemStackMoveOperation(api.World, mouseButton, modifiers, EnumMergePriority.AutoMerge);
        packetOp.ActingPlayer = api.World.Player;
        if (!shiftPressed)
        {
            packetOp.CurrentPriority = EnumMergePriority.DirectMerge;
        }

        object packet = inventoryBase.InvNetworkUtil.GetActivateSlotPacket(slotId, packetOp);
        if (packet == null)
        {
            return false;
        }

        if (!TryApplyPreview(api, inventory, slotId, targetSlot, mouseSlot, mouseButton, modifiers, shiftPressed, out string ownershipKind))
        {
            return false;
        }

        SendPacket(SendPacketHandlerRef(grid), packet);
        api.Input.TriggerOnMouseClickSlot(targetSlot);

        ResponsiveDiagnostics.Basic(
            "CLIENT preview-own {0} inv={1}[{2}] button={3} shift={4} target={5} mouse={6}",
            ownershipKind,
            inventory.InventoryID,
            slotId,
            mouseButton,
            shiftPressed,
            InventoryDiagFormat.Slot(targetSlot),
            InventoryDiagFormat.Slot(mouseSlot));

        return true;
    }

    public static void BeginVanillaDragIfNeeded(GuiElementItemSlotGridBase grid, ICoreClientAPI api, MouseEvent args)
    {
        if (!ResponsiveVSConfigSystem.Config.Transactions.BypassClickDragGestures
            || grid == null
            || api?.World?.Player?.InventoryManager?.MouseItemSlot?.Itemstack == null
            || args == null)
        {
            return;
        }

        if (args.Button != EnumMouseButton.Left && args.Button != EnumMouseButton.Right)
        {
            return;
        }

        VanillaDragGrids.Add(grid);
    }

    public static void EndVanillaDrag(GuiElementItemSlotGridBase grid)
    {
        if (grid != null)
        {
            VanillaDragGrids.Remove(grid);
        }
    }

    public static void BeginVanillaBypass(GuiElementItemSlotGridBase grid)
    {
        if (grid != null)
        {
            VanillaDragGrids.Add(grid);
        }
    }

    public static void EndVanillaBypass(GuiElementItemSlotGridBase grid)
    {
        EndVanillaDrag(grid);
    }

    private static bool IsVanillaDragActive(GuiElementItemSlotGridBase grid)
    {
        return grid != null && VanillaDragGrids.Contains(grid);
    }

    private static bool ShouldBypassPreviewOwnership(IInventory inventory, out string reason)
    {
        reason = null;
        if (inventory == null)
        {
            reason = "missing-inventory";
            return true;
        }

        try
        {
            string typeName = inventory.GetType().FullName;
            if (ResponsiveVSConfigSystem.Config.Compatibility.BypassCreativeInventory
                && inventory is InventoryPlayerCreative)
            {
                // Creative inventory is an item creation UI, not a normal storage inventory. Vanilla
                // uses ItemSlotCreative clone semantics and adds current tab metadata after activation.
                // Preview-owning it can miss that post-activation state and can break creative-window
                // callbacks. Leave creative slots on the vanilla path until there is a dedicated model.
                reason = "creative-inventory";
                return true;
            }

            if (ResponsiveVSConfigSystem.Config.Compatibility.BypassStorageControllerVirtualInventory
                && string.Equals(typeName, "storagecontroller.StorageVirtualInv", StringComparison.Ordinal))
            {
                // Storage Controller uses a virtual display inventory as a click trigger. Its packet
                // handler reads the real mouse slot after vanilla has moved a displayed stack there,
                // then sends a block-entity packet and clears the mouse again. Preview-only ownership
                // prevents that temporary real mouse mutation, so the controller sees an empty mouse
                // and never requests the item. Leave this virtual inventory on the vanilla path.
                reason = "storagecontroller-virtual";
                return true;
            }
        }
        catch (Exception ex)
        {
            ResponsiveDiagnostics.Basic(
                "CLIENT preview bypass check failed; falling back to vanilla error={0}: {1}",
                ex.GetType().Name,
                ex.Message);
            reason = "compat-error";
            return true;
        }

        return false;
    }

    private static bool TryApplyPreview(
        ICoreClientAPI api,
        IInventory inventory,
        int slotId,
        ItemSlot targetSlot,
        ItemSlot mouseSlot,
        EnumMouseButton mouseButton,
        EnumModifierKey modifiers,
        bool shiftPressed,
        out string ownershipKind)
    {
        ownershipKind = "slotclick";
        SlotKey targetKey = new SlotKey(inventory.InventoryID, slotId);
        SlotKey mouseKey = new SlotKey(mouseSlot.Inventory?.InventoryID ?? ("mouse-" + api.World.Player.PlayerUID), 0);

        if (ResponsiveVSConfigSystem.Config.Transactions.EnableCraftingGridPreviewOwnership
            && inventory is InventoryCraftingGrid craftingGrid)
        {
            return TryApplyCraftingGridPreview(
                api,
                craftingGrid,
                slotId,
                targetSlot,
                mouseSlot,
                targetKey,
                mouseKey,
                mouseButton,
                modifiers,
                shiftPressed,
                out ownershipKind);
        }

        return TryApplyNormalSlotPreview(api, inventory, slotId, targetSlot, mouseSlot, targetKey, mouseKey, mouseButton, modifiers, shiftPressed);
    }

    private static bool TryApplyCraftingGridPreview(
        ICoreClientAPI api,
        InventoryCraftingGrid craftingGrid,
        int slotId,
        ItemSlot targetSlot,
        ItemSlot mouseSlot,
        SlotKey targetKey,
        SlotKey mouseKey,
        EnumMouseButton mouseButton,
        EnumModifierKey modifiers,
        bool shiftPressed,
        out string ownershipKind)
    {
        int outputSlotId = craftingGrid.Count - 1;
        ItemSlot outputSlot = craftingGrid[outputSlotId];
        SlotKey outputKey = new SlotKey(craftingGrid.InventoryID, outputSlotId);

        if (slotId == outputSlotId)
        {
            ownershipKind = "craft-output";
            return TryApplyCraftingOutputPreview(
                api,
                craftingGrid,
                outputSlotId,
                outputSlot,
                mouseSlot,
                outputKey,
                mouseKey,
                shiftPressed);
        }

        ownershipKind = "craft-input";
        if (!TryApplyNormalSlotPreview(api, craftingGrid, slotId, targetSlot, mouseSlot, targetKey, mouseKey, mouseButton, modifiers, shiftPressed))
        {
            return false;
        }

        // The clicked input slot is only a preview, so the real client grid was not mutated and
        // vanilla did not run InventoryCraftingGrid.FindMatchingRecipe. Hide stale output until
        // the server sends the authoritative grid/output update.
        if (outputSlot != null)
        {
            ClientInventoryPreviewStore.Set(outputKey, outputSlot, null);
        }

        return true;
    }

    private static bool TryApplyCraftingOutputPreview(
        ICoreClientAPI api,
        InventoryCraftingGrid craftingGrid,
        int outputSlotId,
        ItemSlot outputSlot,
        ItemSlot mouseSlot,
        SlotKey outputKey,
        SlotKey mouseKey,
        bool shiftPressed)
    {
        ItemStack outputBefore = CurrentPreviewOrReal(outputKey, outputSlot);
        if (outputBefore == null)
        {
            return true;
        }

        if (shiftPressed)
        {
            // Craft-many can touch many destination slots. Do not guess them client-side; just
            // hide the stale output until the server's dirty-slot update arrives.
            ClientInventoryPreviewStore.Set(outputKey, outputSlot, null);
            return true;
        }

        ItemStack mouseBefore = CurrentPreviewOrReal(mouseKey, mouseSlot);
        ItemStack outputAfter = outputBefore.Clone();
        ItemStack mouseAfter = mouseBefore?.Clone();

        if (mouseAfter == null)
        {
            mouseAfter = outputAfter.Clone();
            outputAfter = null;
        }
        else
        {
            int mergeable = mouseAfter.Collectible.GetMergableQuantity(mouseAfter, outputAfter, EnumMergePriority.DirectMerge);
            if (mergeable < outputAfter.StackSize)
            {
                return true;
            }

            mouseAfter.StackSize += outputAfter.StackSize;
            outputAfter = null;
        }

        ClientInventoryPreviewStore.Set(outputKey, outputSlot, outputAfter);
        ClientInventoryPreviewStore.Set(mouseKey, mouseSlot, mouseAfter);
        PlayPreviewSound(api, mouseBefore, mouseAfter);

        ResponsiveDiagnostics.Basic(
            "CLIENT craft-output preview inv={0}[{1}] output={2}->{3} mouse={4}->{5}",
            craftingGrid.InventoryID,
            outputSlotId,
            InventoryDiagFormat.Stack(outputBefore),
            InventoryDiagFormat.Stack(outputAfter),
            InventoryDiagFormat.Stack(mouseBefore),
            InventoryDiagFormat.Stack(mouseAfter));

        return true;
    }

    private static bool TryApplyNormalSlotPreview(
        ICoreClientAPI api,
        IInventory inventory,
        int slotId,
        ItemSlot targetSlot,
        ItemSlot mouseSlot,
        SlotKey targetKey,
        SlotKey mouseKey,
        EnumMouseButton mouseButton,
        EnumModifierKey modifiers,
        bool shiftPressed)
    {

        if (shiftPressed)
        {
            // Shift transfer destinations depend on the full opened-inventory set. In client-only
            // preview mode we deliberately do not guess those destination slots; we only show that
            // the clicked source is pending until the authoritative server update arrives.
            ClientInventoryPreviewStore.Set(targetKey, targetSlot, null);
            return true;
        }

        ItemStack targetBefore = CurrentPreviewOrReal(targetKey, targetSlot);
        ItemStack mouseBefore = CurrentPreviewOrReal(mouseKey, mouseSlot);
        PreviewOnlySlot targetPreview = PreviewOnlySlot.From(targetSlot, targetBefore);
        PreviewOnlySlot mousePreview = PreviewOnlySlot.From(mouseSlot, mouseBefore);

        ItemStackMoveOperation previewOp = new ItemStackMoveOperation(api.World, mouseButton, modifiers, EnumMergePriority.AutoMerge);
        previewOp.ActingPlayer = api.World.Player;
        previewOp.CurrentPriority = EnumMergePriority.DirectMerge;
        if (mouseButton == EnumMouseButton.Wheel)
        {
            previewOp.RequestedQuantity = 1;
        }

        try
        {
            // This intentionally reuses vanilla ItemSlot semantics on disposable slots. The preview
            // can be wrong for custom slot subclasses, but it cannot corrupt real inventory state.
            targetPreview.ActivateSlot(mousePreview, ref previewOp);
        }
        catch (Exception ex)
        {
            ResponsiveDiagnostics.Basic(
                "CLIENT preview failed; falling back to vanilla inv={0}[{1}] button={2} error={3}: {4}",
                inventory.InventoryID,
                slotId,
                mouseButton,
                ex.GetType().Name,
                ex.Message);
            return false;
        }

        ClientInventoryPreviewStore.Set(targetKey, targetSlot, targetPreview.Itemstack);
        ClientInventoryPreviewStore.Set(mouseKey, mouseSlot, mousePreview.Itemstack);
        PlayPreviewSound(api, mouseBefore, mousePreview.Itemstack);
        return true;
    }

    private static ItemStack CurrentPreviewOrReal(SlotKey key, ItemSlot slot)
    {
        return ClientInventoryPreviewStore.TryGet(key, out ItemStack preview) ? preview : slot.Itemstack;
    }

    private static void SendPacket(Action<object> sendPacketHandler, object packet)
    {
        if (packet is object[] packets)
        {
            for (int i = 0; i < packets.Length; i++)
            {
                sendPacketHandler?.Invoke(packets[i]);
            }
            return;
        }

        sendPacketHandler?.Invoke(packet);
    }

    private static void PlayPreviewSound(ICoreClientAPI api, ItemStack mouseBefore, ItemStack mouseAfter)
    {
        if (api?.World == null)
        {
            return;
        }

        if (mouseBefore == null && mouseAfter != null)
        {
            api.World.PlaySoundAt(mouseAfter.Collectible?.HeldSounds?.InvPickup ?? HeldSounds.InvPickUpDefault, 0, 0, 0, 0, null, 1f);
            return;
        }

        if (mouseBefore != null && (mouseAfter == null || mouseBefore.Collectible?.Id != mouseAfter.Collectible?.Id))
        {
            api.World.PlaySoundAt(mouseBefore.Collectible?.HeldSounds?.InvPlace ?? HeldSounds.InvPlaceDefault, 0, 0, 0, 0, null, 1f);
        }
    }

    private sealed class PreviewOnlySlot : ItemSlot
    {
        private PreviewOnlySlot(ItemStack stack)
            : base(null)
        {
            itemstack = stack;
        }

        public static PreviewOnlySlot From(ItemSlot sourceSlot, ItemStack stack)
        {
            PreviewOnlySlot preview = new PreviewOnlySlot(stack?.Clone());
            if (sourceSlot != null)
            {
                preview.MaxSlotStackSize = sourceSlot.MaxSlotStackSize;
                preview.StorageType = sourceSlot.StorageType;
            }

            return preview;
        }

        public override bool CanHold(ItemSlot sourceSlot)
        {
            return sourceSlot?.Itemstack?.Collectible != null
                && (sourceSlot.Itemstack.Collectible.GetStorageFlags(sourceSlot.Itemstack) & StorageType) > 0;
        }

        public override void OnItemSlotModified(ItemStack sinkStack)
        {
            // Preview slots are deliberately detached from real inventories. Vanilla click logic
            // calls this during simulation; forwarding it would dirty real inventories or crash
            // because the simulated slot is not part of the source inventory.
        }

        public override void MarkDirty()
        {
            // Render-only preview state is committed to ClientInventoryPreviewStore after the
            // simulated click completes. There is no inventory dirty queue to update here.
        }
    }
}
