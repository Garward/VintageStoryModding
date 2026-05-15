using Vintagestory.API.Common;

namespace VintageKinematics.BlockEntities
{
    public class ItemSlotForgePressInput : ItemSlot
    {
        public ItemSlotForgePressInput(InventoryBase inventory) : base(inventory) { }

        public override bool CanHold(ItemSlot sourceSlot)
        {
            return base.CanHold(sourceSlot)
                && !ItemSlotFuelOnly.IsFuel(sourceSlot, inventory)
                && !ItemSlotForgePressDie.IsForgePressDie(sourceSlot);
        }

        public override bool CanTakeFrom(ItemSlot sourceSlot, EnumMergePriority priority = EnumMergePriority.AutoMerge)
        {
            return base.CanTakeFrom(sourceSlot, priority)
                && !ItemSlotFuelOnly.IsFuel(sourceSlot, inventory)
                && !ItemSlotForgePressDie.IsForgePressDie(sourceSlot);
        }
    }
}
