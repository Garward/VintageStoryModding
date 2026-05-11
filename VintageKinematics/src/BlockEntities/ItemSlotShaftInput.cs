using Vintagestory.API.Common;

namespace VintageKinematics.BlockEntities
{
    // Accepts only shaft / encasedshaft items. Used by the bore's drill-rod input slots so a
    // player can't dump random goods into the machine and confuse the descent logic.
    public class ItemSlotShaftInput : ItemSlot
    {
        public ItemSlotShaftInput(InventoryBase inventory) : base(inventory) { }

        public override bool CanHold(ItemSlot sourceSlot)
        {
            if (sourceSlot?.Itemstack?.Collectible == null) return false;
            return IsAcceptedCode(sourceSlot.Itemstack.Collectible.Code?.FirstCodePart());
        }

        public override bool CanTake() => true;

        public static bool IsAcceptedCode(string firstPart)
        {
            return firstPart == "shaft" || firstPart == "encasedshaft";
        }
    }
}
