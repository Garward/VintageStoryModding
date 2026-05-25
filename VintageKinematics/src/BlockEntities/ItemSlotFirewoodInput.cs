using Vintagestory.API.Common;

namespace VintageKinematics.BlockEntities
{
    public class ItemSlotFirewoodInput : ItemSlot
    {
        public ItemSlotFirewoodInput(InventoryBase inventory) : base(inventory) { }

        public override bool CanHold(ItemSlot sourceSlot)
        {
            if (sourceSlot?.Itemstack?.Collectible?.Code == null) return false;
            return sourceSlot.Itemstack.Collectible.Code.Domain == "game"
                && sourceSlot.Itemstack.Collectible.Code.Path == "firewood"
                && base.CanHold(sourceSlot);
        }
    }
}
