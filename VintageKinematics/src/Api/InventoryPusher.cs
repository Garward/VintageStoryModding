using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VintageKinematics.Api
{
    /// <summary>
    /// Static helper that pushes from a source slot into the inventory of the BE on the neighbouring
    /// block in a given direction. Mirrors the funnel's push behaviour so any BE that owns outputs
    /// can actively eject into adjacent storage without re-implementing the slot-selection dance.
    /// </summary>
    public static class InventoryPusher
    {
        /// <summary>
        /// Try to move up to <paramref name="maxQuantity"/> items from <paramref name="source"/> into
        /// the inventory of the BE adjacent to <paramref name="fromPos"/> on <paramref name="toFace"/>.
        /// Returns the number of items moved. Honours the target's <c>OnGetAutoPushIntoSlot</c> when
        /// set (restricted push); falls back to <c>GetBestSuitedSlot</c> otherwise.
        /// </summary>
        public static int TryPush(IWorldAccessor world, BlockPos fromPos, BlockFacing toFace, ItemSlot source, int maxQuantity = int.MaxValue)
        {
            if (source == null || source.Empty || maxQuantity <= 0) return 0;

            BlockPos targetPos = fromPos.AddCopy(toFace);
            BlockEntity targetBe = MultiblockHelper.GetMultiblockAwareBE(world, targetPos);
            if (targetBe is not IBlockEntityContainer container) return 0;

            IInventory inventory = container.Inventory;
            if (inventory == null || inventory.PutLocked) return 0;

            BlockFacing fromFace = toFace.Opposite;
            bool restrictedPush = inventory is InventoryGeneric pushInv && pushInv.OnGetAutoPushIntoSlot != null;
            InventoryBase invBase = inventory as InventoryBase;

            int requested = System.Math.Min(maxQuantity, source.Itemstack.StackSize);
            DummySlot probe = new DummySlot(source.Itemstack.Clone());
            probe.Itemstack.StackSize = requested;
            int startSize = probe.Itemstack.StackSize;
            var skip = new List<ItemSlot>();

            while (!probe.Empty && probe.Itemstack.StackSize > 0)
            {
                ItemSlot targetSlot = invBase?.GetAutoPushIntoSlot(fromFace, probe);
                int moved = 0;

                if (targetSlot != null)
                {
                    moved = MoveIntoSlot(world, probe, targetSlot);
                    if (moved <= 0) skip.Add(targetSlot);
                }
                else if (restrictedPush)
                {
                    break;
                }

                if (moved <= 0)
                {
                    WeightedSlot weighted = inventory.GetBestSuitedSlot(probe, null, skip);
                    targetSlot = weighted?.slot;
                    if (targetSlot == null) break;

                    moved = MoveIntoSlot(world, probe, targetSlot);
                    if (moved <= 0)
                    {
                        skip.Add(targetSlot);
                        if (skip.Count >= inventory.Count) break;
                    }
                }

                if (moved > 0)
                {
                    targetSlot.MarkDirty();
                    targetBe.MarkDirty(true);
                    skip.Clear();
                }
            }

            int remaining = probe.Empty ? 0 : probe.Itemstack.StackSize;
            int movedTotal = startSize - remaining;
            if (movedTotal <= 0) return 0;

            source.Itemstack.StackSize -= movedTotal;
            if (source.Itemstack.StackSize <= 0) source.Itemstack = null;
            source.MarkDirty();
            return movedTotal;
        }

        private static int MoveIntoSlot(IWorldAccessor world, ItemSlot source, ItemSlot target)
        {
            if (source == null || target == null || source.Empty) return 0;
            ItemStackMoveOperation op = new ItemStackMoveOperation(
                world,
                EnumMouseButton.Left,
                0,
                EnumMergePriority.DirectMerge,
                source.Itemstack.StackSize);
            return source.TryPutInto(target, ref op);
        }
    }
}
