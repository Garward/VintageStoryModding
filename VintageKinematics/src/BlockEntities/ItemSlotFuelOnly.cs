using Vintagestory.API.Common;

namespace VintageKinematics.BlockEntities
{
    public class ItemSlotFuelOnly : ItemSlot
    {
        public ItemSlotFuelOnly(InventoryBase inventory) : base(inventory) { }

        public override bool CanHold(ItemSlot sourceSlot)
        {
            return base.CanHold(sourceSlot) && IsFuel(sourceSlot, inventory);
        }

        public override bool CanTakeFrom(ItemSlot sourceSlot, EnumMergePriority priority = EnumMergePriority.AutoMerge)
        {
            return base.CanTakeFrom(sourceSlot, priority) && IsFuel(sourceSlot, inventory);
        }

        public static bool IsFuel(ItemSlot sourceSlot, InventoryBase inventory)
        {
            ItemStack stack = sourceSlot?.Itemstack;
            if (stack?.Collectible == null) return false;
            CombustibleProperties props = stack.Collectible.GetCombustibleProperties(inventory?.Api?.World, stack, null);
            return props != null && props.BurnDuration > 0f && props.BurnTemperature > 0;
        }
    }
}
