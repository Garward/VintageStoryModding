using Vintagestory.API.Common;

namespace VintageKinematics.Items
{
    public class ItemSlotDrillFuel : ItemSlot
    {
        public ItemSlotDrillFuel(InventoryBase inventory) : base(inventory)
        {
        }

        public override bool CanHold(ItemSlot sourceSlot)
        {
            return ItemPoweredDrill.IsValidFuel(sourceSlot?.Itemstack) && base.CanHold(sourceSlot);
        }

        public override bool CanTakeFrom(ItemSlot sourceSlot, EnumMergePriority priority = EnumMergePriority.AutoMerge)
        {
            return ItemPoweredDrill.IsValidFuel(sourceSlot?.Itemstack) && base.CanTakeFrom(sourceSlot, priority);
        }
    }
}
