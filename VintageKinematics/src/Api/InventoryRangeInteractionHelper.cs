using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VintageKinematics.Api
{
    /// <summary>
    /// Reusable "crate style" interaction helpers for machines that expose large slot ranges
    /// through in-world right-click interactions instead of a normal GUI.
    /// </summary>
    public static class InventoryRangeInteractionHelper
    {
        public static int PutFromSlotIntoRange(
            IWorldAccessor world,
            InventoryBase inventory,
            int firstSlot,
            int lastSlot,
            ItemSlot source,
            int quantity)
        {
            if (world == null || inventory == null || source == null || source.Empty || quantity <= 0) return 0;

            int remaining = quantity;
            int movedTotal = 0;
            for (int i = firstSlot; i <= lastSlot && remaining > 0 && !source.Empty; i++)
            {
                ItemSlot target = inventory[i];
                if (!target.CanHold(source)) continue;

                int moved = source.TryPutInto(world, target, remaining);
                if (moved <= 0) continue;

                movedTotal += moved;
                remaining -= moved;
                target.MarkDirty();
            }

            if (movedTotal > 0) source.MarkDirty();
            return movedTotal;
        }

        public static int TakeFromRangeToPlayer(
            IWorldAccessor world,
            IPlayer player,
            InventoryBase inventory,
            int firstSlot,
            int lastSlot,
            int quantity)
        {
            if (world == null || player?.InventoryManager == null || inventory == null || quantity <= 0) return 0;

            ItemSlot sourceSlot = FirstNonEmptySlot(inventory, firstSlot, lastSlot);
            if (sourceSlot == null || sourceSlot.Empty) return 0;

            FillSlotFromRange(inventory, sourceSlot, firstSlot, lastSlot, quantity);

            ItemStack stack = sourceSlot.TakeOut(quantity);
            if (stack == null || stack.StackSize <= 0) return 0;

            int originalQuantity = stack.StackSize;
            player.InventoryManager.TryGiveItemstack(stack, true);

            int remaining = stack?.StackSize ?? 0;
            int taken = originalQuantity - remaining;
            if (remaining > 0)
            {
                DepositStackDirect(inventory, stack, firstSlot, lastSlot);
            }

            if (taken > 0) sourceSlot.MarkDirty();
            return taken;
        }

        public static void FillSlotFromRange(InventoryBase inventory, ItemSlot target, int firstSlot, int lastSlot, int targetQuantity)
        {
            if (inventory == null || target == null || target.Empty) return;

            for (int i = firstSlot; i <= lastSlot && target.StackSize < targetQuantity; i++)
            {
                ItemSlot source = inventory[i];
                if (source == target || source.Empty) continue;
                if (!source.Itemstack.Collectible.Code.Equals(target.Itemstack.Collectible.Code)) continue;

                int needed = targetQuantity - target.StackSize;
                int moved = System.Math.Min(needed, source.StackSize);
                target.Itemstack.StackSize += moved;
                source.Itemstack.StackSize -= moved;
                target.MarkDirty();
                source.MarkDirty();
                if (source.Itemstack.StackSize <= 0) source.Itemstack = null;
            }
        }

        public static void DepositStackDirect(
            InventoryBase inventory,
            ItemStack stack,
            int firstSlot,
            int lastSlot,
            ItemSlot skipSlot = null)
        {
            if (inventory == null || stack == null || stack.StackSize <= 0) return;

            for (int i = firstSlot; i <= lastSlot && stack.StackSize > 0; i++)
            {
                ItemSlot target = inventory[i];
                if (target == skipSlot || target.Empty) continue;
                if (!target.Itemstack.Collectible.Code.Equals(stack.Collectible.Code)) continue;

                int free = target.Itemstack.Collectible.MaxStackSize - target.Itemstack.StackSize;
                if (free <= 0) continue;

                int moved = System.Math.Min(free, stack.StackSize);
                target.Itemstack.StackSize += moved;
                stack.StackSize -= moved;
                target.MarkDirty();
            }

            for (int i = firstSlot; i <= lastSlot && stack.StackSize > 0; i++)
            {
                ItemSlot target = inventory[i];
                if (target == skipSlot || !target.Empty) continue;

                int moved = System.Math.Min(stack.Collectible.MaxStackSize, stack.StackSize);
                ItemStack placed = stack.Clone();
                placed.StackSize = moved;
                target.Itemstack = placed;
                stack.StackSize -= moved;
                target.MarkDirty();
            }
        }

        public static ItemSlot FirstNonEmptySlot(InventoryBase inventory, int firstSlot, int lastSlot)
        {
            if (inventory == null) return null;
            for (int i = firstSlot; i <= lastSlot; i++)
            {
                ItemSlot slot = inventory[i];
                if (!slot.Empty) return slot;
            }
            return null;
        }

        public static int CountItems(InventoryBase inventory, int firstSlot, int lastSlot)
        {
            if (inventory == null) return 0;
            int count = 0;
            for (int i = firstSlot; i <= lastSlot; i++)
            {
                ItemSlot slot = inventory[i];
                if (!slot.Empty) count += slot.StackSize;
            }
            return count;
        }

        public static int CapacityItems(ItemStack stack, int slotCount)
        {
            return stack?.Collectible?.MaxStackSize * slotCount ?? 0;
        }

        public static void PlayBuildSound(IPlayer player)
        {
            player?.Entity?.World?.PlaySoundAt(
                new AssetLocation("game:sounds/player/build"),
                player.Entity,
                player,
                true,
                16);
        }
    }
}
