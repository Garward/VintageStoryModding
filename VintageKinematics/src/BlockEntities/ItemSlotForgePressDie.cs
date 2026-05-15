using Vintagestory.API.Common;

namespace VintageKinematics.BlockEntities
{
    public class ItemSlotForgePressDie : ItemSlot
    {
        public ItemSlotForgePressDie(InventoryBase inventory) : base(inventory) { }

        public override bool CanHold(ItemSlot sourceSlot)
        {
            return base.CanHold(sourceSlot) && IsForgePressDie(sourceSlot);
        }

        public override bool CanTakeFrom(ItemSlot sourceSlot, EnumMergePriority priority = EnumMergePriority.AutoMerge)
        {
            return base.CanTakeFrom(sourceSlot, priority) && IsForgePressDie(sourceSlot);
        }

        public static bool IsForgePressDie(ItemSlot sourceSlot)
        {
            ItemStack stack = sourceSlot?.Itemstack;
            return stack?.Collectible?.Attributes?["forgePressDie"].AsBool(false) == true;
        }
    }
}
