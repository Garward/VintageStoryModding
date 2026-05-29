using System;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.Common;

namespace CraftingGhostItemFix;

public sealed class CraftingGhostItemFixModSystem : ModSystem
{
    private const string HarmonyId = "garward.craftingghostitemfix";
    private Harmony harmony;

    public override void Start(ICoreAPI api)
    {
        try
        {
            harmony = new Harmony(HarmonyId);
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            api.Logger.Notification("[CraftingGhostItemFix] Harmony patches applied.");
        }
        catch (Exception ex)
        {
            api.Logger.Error("[CraftingGhostItemFix] Failed to apply Harmony patches: {0}", ex);
        }
    }

    public override void Dispose()
    {
        harmony?.UnpatchAll(HarmonyId);
    }
}

[HarmonyPatch(typeof(InventoryCraftingGrid), nameof(InventoryCraftingGrid.ActivateSlot))]
internal static class CraftingGridActivateSlotPatch
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
        if (outputSlot == null || outputSlot.Empty || op.ActingPlayer == null)
        {
            __result = __instance.InvNetworkUtil.GetActivateSlotPacket(slotId, op);
            return false;
        }

        __result = __instance.InvNetworkUtil.GetActivateSlotPacket(slotId, op);
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

[HarmonyPatch(typeof(ItemSlotCraftingOutput), nameof(ItemSlotCraftingOutput.TryPutInto))]
internal static class CraftingOutputTryPutIntoPatch
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

[HarmonyPatch(typeof(ItemSlotCraftingOutput), "FlipWith")]
internal static class CraftingOutputFlipWithPatch
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
            ItemStackMoveOperation op = new ItemStackMoveOperation(
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

[HarmonyPatch(typeof(InventoryCraftingGrid), "FindMatchingRecipe")]
internal static class CraftingGridFindMatchingRecipePatch
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
        TreeAttribute tree = new TreeAttribute();
        tree["itemstack"] = new ItemstackAttribute(craftedStack);
        tree["byentityid"] = new LongAttribute(actingPlayer.Entity.EntityId);
        actingPlayer.Entity.World.Api.Event.PushEvent("onitemcrafted", tree);
    }
}
