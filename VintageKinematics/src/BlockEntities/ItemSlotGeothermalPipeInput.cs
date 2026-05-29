using Vintagestory.API.Common;

namespace VintageKinematics.BlockEntities
{
    public class ItemSlotGeothermalPipeInput : ItemSlot
    {
        public ItemSlotGeothermalPipeInput(InventoryBase inventory) : base(inventory) { }

        public override bool CanHold(ItemSlot sourceSlot)
        {
            if (sourceSlot?.Itemstack?.Collectible == null) return false;
            return IsAcceptedCode(sourceSlot.Itemstack.Collectible.Code);
        }

        public override bool CanTake() => true;

        public static bool IsAcceptedCode(AssetLocation code)
        {
            return code != null
                && code.Domain == "vintagekinematics"
                && code.Path == "geothermalpipe";
        }
    }
}
