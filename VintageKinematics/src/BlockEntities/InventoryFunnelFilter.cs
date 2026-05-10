using Vintagestory.API.Common;

namespace VintageKinematics.BlockEntities
{
    // Ghost inventory: clicking a slot snapshots a copy of the cursor stack as a filter
    // pattern, but never moves items between slot and cursor. Items also can't leave the
    // slot, so breaking the funnel never duplicates the templated stack. Slot count is
    // fixed at the inventory's allocated capacity; per-tier "active" slot count is
    // enforced by the BE / dialog layer above.
    public class InventoryFunnelFilter : InventoryGeneric
    {
        public InventoryFunnelFilter(int slotCount, string className, ICoreAPI api)
            : base(slotCount, className, null, api, (slotId, self) => new ItemSlotFunnelFilter(self))
        {
        }
    }

    public class ItemSlotFunnelFilter : ItemSlot
    {
        public ItemSlotFunnelFilter(InventoryBase inventory) : base(inventory)
        {
            MaxSlotStackSize = 1;
        }

        // CanHold true keeps the slot from rendering as a "rejecting drop" target;
        // the actual transfer guards are CanTakeFrom/CanTake/TryPutInto/TryFlipWith below,
        // and player clicks are handled by the ActivateSlot override which never moves stacks.
        public override bool CanHold(ItemSlot sourceSlot) => true;
        public override bool CanTake() => false;
        public override bool CanTakeFrom(ItemSlot sourceSlot, EnumMergePriority priority = EnumMergePriority.AutoMerge) => false;
        public override int TryPutInto(ItemSlot sinkSlot, ref ItemStackMoveOperation op) => 0;
        public override bool TryFlipWith(ItemSlot itemSlot) => false;

        public override void ActivateSlot(ItemSlot sourceSlot, ref ItemStackMoveOperation op)
        {
            if (op.MouseButton != EnumMouseButton.Left && op.MouseButton != EnumMouseButton.Right) return;

            if (sourceSlot.Empty)
            {
                if (Itemstack != null)
                {
                    Itemstack = null;
                    OnItemSlotModified(null);
                }
            }
            else
            {
                ItemStack copy = sourceSlot.Itemstack.Clone();
                copy.StackSize = 1;
                Itemstack = copy;
                OnItemSlotModified(Itemstack);
            }
            op.MovedQuantity = 0;
        }
    }
}
