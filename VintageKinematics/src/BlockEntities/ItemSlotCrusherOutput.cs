using Vintagestory.API.Common;

namespace VintageKinematics.BlockEntities
{
    // Output slot: the player can take items out, but cannot put items in (manually OR via
    // shift-click). Direct assignment via slot.Itemstack = ... still works for deposits from
    // the crusher process, since CanHold/CanTakeFrom only gate the move-API path.
    public class ItemSlotCrusherOutput : ItemSlot
    {
        public ItemSlotCrusherOutput(InventoryBase inventory) : base(inventory) { }

        public override bool CanHold(ItemSlot sourceSlot) => false;
        public override bool CanTakeFrom(ItemSlot sourceSlot, EnumMergePriority priority = EnumMergePriority.AutoMerge) => false;
    }
}
