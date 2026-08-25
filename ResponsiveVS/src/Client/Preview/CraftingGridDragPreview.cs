using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using ResponsiveVS.Config;
using ResponsiveVS.Diagnostics;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.Common;

namespace ResponsiveVS.Client.Preview;

public static class CraftingGridDragPreview
{
    private static readonly FieldInfo InventoryField = AccessTools.Field(typeof(GuiElementItemSlotGridBase), "inventory");
    private static readonly FieldInfo RenderedSlotsField = AccessTools.Field(typeof(GuiElementItemSlotGridBase), "renderedSlots");
    private static readonly FieldInfo HoverSlotIdField = AccessTools.Field(typeof(GuiElementItemSlotGridBase), "hoverSlotId");
    private static readonly FieldInfo HoverInvField = AccessTools.Field(typeof(GuiElementItemSlotGridBase), "hoverInv");
    private static readonly FieldInfo SendPacketHandlerField = AccessTools.Field(typeof(GuiElementItemSlotGridBase), "SendPacketHandler");
    private static readonly MethodInfo FindMatchingRecipeMethod = AccessTools.Method(typeof(InventoryCraftingGrid), "FindMatchingRecipe");

    private static readonly Dictionary<GuiElementItemSlotGridBase, DragState> Active = new();

    public static bool TryBegin(GuiElementItemSlotGridBase grid, ICoreClientAPI api, MouseEvent args)
    {
        if (!ResponsiveVSConfigSystem.Config.Transactions.EnableCraftingGridDragPreview
            || !CanUsePreview(grid, api, args)
            || !TryGetInventory(grid, out InventoryCraftingGrid inventory))
        {
            return false;
        }

        if (IsDown(api, GlKeys.ShiftLeft) || IsDown(api, GlKeys.ShiftRight)
            || IsDown(api, GlKeys.ControlLeft) || IsDown(api, GlKeys.ControlRight)
            || IsDown(api, GlKeys.AltLeft) || IsDown(api, GlKeys.AltRight))
        {
            return false;
        }

        ItemStack mouseStack = api.World.Player.InventoryManager.MouseItemSlot?.Itemstack;
        if (mouseStack == null)
        {
            return false;
        }

        if (!TryGetHoveredSlot(grid, args.X, args.Y, out int slotIndex, out int slotId)
            || !CanPreviewSlot(api, inventory, slotId, mouseStack))
        {
            return false;
        }

        DragState state = new DragState(api, inventory, args.Button, mouseStack.Clone());
        Active[grid] = state;
        AddSlot(grid, state, slotIndex, slotId);
        args.Handled = true;

        ResponsiveDiagnostics.Basic(
            "CLIENT craft-drag preview begin inv={0}[{1}] button={2}",
            inventory.InventoryID,
            slotId,
            args.Button);
        return true;
    }

    public static bool TryMove(GuiElementItemSlotGridBase grid, ICoreClientAPI api, MouseEvent args)
    {
        if (!Active.TryGetValue(grid, out DragState state))
        {
            return false;
        }

        if (!ResponsiveVSConfigSystem.Config.Transactions.EnableCraftingGridDragPreview || api?.Side != EnumAppSide.Client)
        {
            Active.Remove(grid);
            return false;
        }

        if (TryGetHoveredSlot(grid, args.X, args.Y, out int slotIndex, out int slotId))
        {
            ItemSlot hoverSlot = state.Inventory[slotId];
            if (!state.SlotIds.Contains(slotId) && CanPreviewSlot(api, state.Inventory, slotId, state.ReferenceStack))
            {
                AddSlot(grid, state, slotIndex, slotId);
                ResponsiveDiagnostics.Verbose(
                    "CLIENT craft-drag preview add inv={0}[{1}] count={2}",
                    state.Inventory.InventoryID,
                    slotId,
                    state.SlotIds.Count);
            }

            SetHover(grid, hoverSlot, slotId);
        }
        else
        {
            SetHover(grid, null, -1);
        }

        args.Handled = true;
        return true;
    }

    public static bool TryEnd(GuiElementItemSlotGridBase grid, ICoreClientAPI api, MouseEvent args)
    {
        if (!Active.TryGetValue(grid, out DragState state))
        {
            return false;
        }

        Active.Remove(grid);
        args.Handled = true;
        SetHover(grid, null, -1);
        UnpauseInventoryUpdates(api, state.Inventory);

        int applied = state.Button == EnumMouseButton.Left
            ? ApplyLeftDrag(grid, api, state)
            : ApplyRightDrag(grid, api, state);
        RefreshCraftingGrid(state.Inventory);

        ResponsiveDiagnostics.Basic(
            "CLIENT craft-drag preview commit inv={0} slots={1} applied={2} button={3}",
            state.Inventory.InventoryID,
            state.SlotIds.Count,
            applied,
            state.Button);

        return true;
    }

    public static void Render(GuiElementItemSlotGridBase grid, float deltaTime)
    {
        if (!ResponsiveVSConfigSystem.Config.Transactions.EnableCraftingGridDragPreview
            || !Active.TryGetValue(grid, out DragState state)
            || state.SlotIndices.Count == 0)
        {
            return;
        }

        LoadedTexture highlight = grid.HighlightSlotTexture;
        if (highlight == null || highlight.TextureId == 0)
        {
            return;
        }

        for (int i = 0; i < state.SlotIndices.Count; i++)
        {
            int slotIndex = state.SlotIndices[i];
            if (slotIndex < 0 || slotIndex >= grid.SlotBounds.Length)
            {
                continue;
            }

            ElementBounds bounds = grid.SlotBounds[slotIndex];
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

    private static int ApplyRightDrag(GuiElementItemSlotGridBase grid, ICoreClientAPI api, DragState state)
    {
        int applied = 0;
        ClientPreviewClickHandler.BeginVanillaBypass(grid);
        try
        {
            foreach (int slotId in state.SlotIds)
            {
                if (!CanPreviewSlot(api, state.Inventory, slotId, state.ReferenceStack))
                {
                    continue;
                }

                grid.SlotClick(api, slotId, EnumMouseButton.Right, false, false, false);
                applied++;
            }
        }
        finally
        {
            ClientPreviewClickHandler.EndVanillaBypass(grid);
        }

        return applied;
    }

    private static int ApplyLeftDrag(GuiElementItemSlotGridBase grid, ICoreClientAPI api, DragState state)
    {
        if (!(SendPacketHandlerField.GetValue(grid) is Action<object> sendPacket))
        {
            return ApplyRightDrag(grid, api, state);
        }

        ItemSlot mouseSlot = api.World.Player.InventoryManager.MouseItemSlot;
        if (mouseSlot?.Itemstack == null)
        {
            return 0;
        }

        int applied = 0;
        for (int i = 0; i < state.SlotIds.Count; i++)
        {
            int slotId = state.SlotIds[i];
            if (!CanPreviewSlot(api, state.Inventory, slotId, state.ReferenceStack))
            {
                continue;
            }

            int requested = Math.Min(LeftDragPreviewAddedSize(state, i), mouseSlot.StackSize);
            if (requested <= 0)
            {
                continue;
            }

            ItemStackMoveOperation op = new ItemStackMoveOperation(
                api.World,
                EnumMouseButton.Left,
                (EnumModifierKey)0,
                EnumMergePriority.DirectMerge,
                requested);
            op.ActingPlayer = api.World.Player;

            object packet = api.World.Player.InventoryManager.TryTransferTo(mouseSlot, state.Inventory[slotId], ref op);
            if (op.MovedQuantity <= 0)
            {
                continue;
            }

            SendPacket(sendPacket, packet);
            applied++;
        }

        return applied;
    }

    private static void RefreshCraftingGrid(InventoryCraftingGrid inventory)
    {
        if (inventory == null || FindMatchingRecipeMethod == null)
        {
            return;
        }

        long startedMs = Environment.TickCount64;
        try
        {
            FindMatchingRecipeMethod.Invoke(inventory, Array.Empty<object>());
        }
        catch (Exception exception)
        {
            ResponsiveDiagnostics.Basic(
                "CLIENT craft-drag refresh failed inv={0} error={1}",
                inventory.InventoryID,
                exception.GetBaseException().Message);
            return;
        }

        ResponsiveDiagnostics.Verbose(
            "CLIENT craft-drag refresh inv={0} elapsed={1}ms",
            inventory.InventoryID,
            Environment.TickCount64 - startedMs);
    }

    private static void UnpauseInventoryUpdates(ICoreClientAPI api, InventoryCraftingGrid inventory)
    {
        if (inventory?.InvNetworkUtil != null)
        {
            inventory.InvNetworkUtil.PauseInventoryUpdates = false;
        }

        IInventoryNetworkUtil mouseNetwork = api?.World?.Player?.InventoryManager?.MouseItemSlot?.Inventory?.InvNetworkUtil;
        if (mouseNetwork != null)
        {
            mouseNetwork.PauseInventoryUpdates = false;
        }
    }

    private static bool TryBuildPreviewSlot(DragState state, int previewIndex, out ItemSlot previewSlot)
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

    private static int RightDragPreviewAddedSize(DragState state, int previewIndex)
    {
        return previewIndex < state.ReferenceStack.StackSize ? 1 : 0;
    }

    private static int LeftDragPreviewAddedSize(DragState state, int previewIndex)
    {
        int slotCount = Math.Max(1, state.SlotIds.Count);
        int evenShare = state.ReferenceStack.StackSize / slotCount;
        if (previewIndex < slotCount - 1)
        {
            return evenShare;
        }

        return state.ReferenceStack.StackSize - evenShare * (slotCount - 1);
    }

    private static bool CanUsePreview(GuiElementItemSlotGridBase grid, ICoreClientAPI api, MouseEvent args)
    {
        return api?.Side == EnumAppSide.Client
            && grid?.Bounds?.ParentBounds != null
            && args != null
            && (args.Button == EnumMouseButton.Left || args.Button == EnumMouseButton.Right)
            && grid.Bounds.ParentBounds.PointInside(args.X, args.Y);
    }

    private static bool TryGetInventory(GuiElementItemSlotGridBase grid, out InventoryCraftingGrid inventory)
    {
        inventory = InventoryField.GetValue(grid) as InventoryCraftingGrid;
        return inventory != null;
    }

    private static bool TryGetHoveredSlot(GuiElementItemSlotGridBase grid, double x, double y, out int slotIndex, out int slotId)
    {
        slotIndex = -1;
        slotId = -1;

        if (grid?.SlotBounds == null)
        {
            return false;
        }

        if (!(RenderedSlotsField.GetValue(grid) is Vintagestory.API.Datastructures.OrderedDictionary<int, ItemSlot> renderedSlots))
        {
            return false;
        }

        for (int i = 0; i < grid.SlotBounds.Length && i < renderedSlots.Count; i++)
        {
            if (!grid.SlotBounds[i].PointInside(x, y))
            {
                continue;
            }

            if (grid.CanClickSlot?.Invoke(i) == false)
            {
                return false;
            }

            slotIndex = i;
            slotId = renderedSlots.GetKeyAtIndex(i);
            return true;
        }

        return false;
    }

    private static bool CanPreviewSlot(ICoreClientAPI api, InventoryCraftingGrid inventory, int slotId, ItemStack referenceStack)
    {
        if (api?.World == null || inventory == null || referenceStack == null || slotId < 0 || slotId >= inventory.Count - 1)
        {
            return false;
        }

        ItemStack stack = inventory[slotId]?.Itemstack;
        return stack == null || stack.Equals(api.World, referenceStack, GlobalConstants.IgnoredStackAttributes);
    }

    private static void AddSlot(GuiElementItemSlotGridBase grid, DragState state, int slotIndex, int slotId)
    {
        state.SlotIndices.Add(slotIndex);
        state.SlotIds.Add(slotId);
        SetHover(grid, state.Inventory[slotId], slotId);
    }

    private static void SetHover(GuiElementItemSlotGridBase grid, ItemSlot slot, int slotId)
    {
        HoverSlotIdField?.SetValue(grid, slotId);
        HoverInvField?.SetValue(grid, slot?.Inventory);
    }

    private static bool IsDown(ICoreClientAPI api, GlKeys key)
    {
        return api?.Input?.KeyboardKeyState != null && api.Input.KeyboardKeyState[(int)key];
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

    private sealed class DragState
    {
        public readonly ICoreClientAPI Api;
        public readonly InventoryCraftingGrid Inventory;
        public readonly EnumMouseButton Button;
        public readonly ItemStack ReferenceStack;
        public readonly List<int> SlotIndices = new List<int>();
        public readonly List<int> SlotIds = new List<int>();

        public DragState(ICoreClientAPI api, InventoryCraftingGrid inventory, EnumMouseButton button, ItemStack referenceStack)
        {
            Api = api;
            Inventory = inventory;
            Button = button;
            ReferenceStack = referenceStack;
        }
    }
}
