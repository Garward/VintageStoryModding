using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VintageKinematics.Api
{
    public static class MachineOutputHelper
    {
        public static void DepositOrPush(
            BlockEntity owner,
            InventoryBase inventory,
            int firstSlot,
            int lastSlot,
            ItemStack stack,
            IEnumerable<FaceMapEntry> outputEntries,
            int pushBatch,
            Vec3d dropAt)
        {
            if (owner?.Api?.World == null || inventory == null || stack == null || stack.StackSize <= 0) return;

            if (TryDepositIntoSlots(inventory, firstSlot, lastSlot, stack)) return;

            TryFlushOutputs(owner, inventory, outputEntries, pushBatch);
            if (TryDepositIntoSlots(inventory, firstSlot, lastSlot, stack)) return;

            TryPushStackToOutputs(owner, stack, outputEntries);
            if (stack.StackSize <= 0) return;

            TryFlushOutputs(owner, inventory, outputEntries, pushBatch);
            if (TryDepositIntoSlots(inventory, firstSlot, lastSlot, stack)) return;

            owner.Api.World.SpawnItemEntity(stack, dropAt);
        }

        public static void FlushOutputs(
            BlockEntity owner,
            InventoryBase inventory,
            IEnumerable<FaceMapEntry> outputEntries,
            int pushBatch)
        {
            TryFlushOutputs(owner, inventory, outputEntries, pushBatch);
        }

        private static bool TryDepositIntoSlots(InventoryBase inventory, int firstSlot, int lastSlot, ItemStack stack)
        {
            if (stack == null || stack.StackSize <= 0) return true;

            for (int i = firstSlot; i <= lastSlot; i++)
            {
                ItemSlot slot = inventory[i];
                if (slot.Empty) continue;
                if (!slot.Itemstack.Collectible.Code.Equals(stack.Collectible.Code)) continue;

                int max = slot.Itemstack.Collectible.MaxStackSize;
                int free = max - slot.Itemstack.StackSize;
                if (free <= 0) continue;

                int take = System.Math.Min(free, stack.StackSize);
                slot.Itemstack.StackSize += take;
                stack.StackSize -= take;
                slot.MarkDirty();
                if (stack.StackSize <= 0) return true;
            }

            for (int i = firstSlot; i <= lastSlot; i++)
            {
                ItemSlot slot = inventory[i];
                if (!slot.Empty) continue;

                int max = stack.Collectible.MaxStackSize;
                int take = System.Math.Min(max, stack.StackSize);
                ItemStack placed = stack.Clone();
                placed.StackSize = take;
                slot.Itemstack = placed;
                stack.StackSize -= take;
                slot.MarkDirty();
                if (stack.StackSize <= 0) return true;
            }

            return false;
        }

        private static void TryFlushOutputs(
            BlockEntity owner,
            InventoryBase inventory,
            IEnumerable<FaceMapEntry> outputEntries,
            int pushBatch)
        {
            if (owner?.Api?.World == null || inventory == null || outputEntries == null) return;

            bool movedAny = false;
            foreach (FaceMapEntry entry in outputEntries)
            {
                foreach (int slotId in entry.SlotIds)
                {
                    ItemSlot slot = inventory[slotId];
                    if (slot.Empty) continue;
                    int moved = InventoryPusher.TryPush(owner.Api.World, entry.Cell, entry.Face, slot, pushBatch);
                    movedAny |= moved > 0;
                }
            }

            if (movedAny) owner.MarkDirty(true);
        }

        private static void TryPushStackToOutputs(
            BlockEntity owner,
            ItemStack stack,
            IEnumerable<FaceMapEntry> outputEntries)
        {
            if (owner?.Api?.World == null || outputEntries == null || stack == null || stack.StackSize <= 0) return;

            DummySlot directOutput = new DummySlot(stack);
            bool movedAny = false;
            foreach (FaceMapEntry entry in outputEntries)
            {
                if (directOutput.Empty) break;
                int moved = InventoryPusher.TryPush(owner.Api.World, entry.Cell, entry.Face, directOutput, directOutput.Itemstack.StackSize);
                movedAny |= moved > 0;
            }

            if (movedAny) owner.MarkDirty(true);
        }
    }
}
