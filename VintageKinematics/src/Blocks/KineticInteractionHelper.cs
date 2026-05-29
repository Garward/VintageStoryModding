using Vintagestory.API.Common;
using VintageKinematics.Items;

namespace VintageKinematics.Blocks
{
    internal static class KineticInteractionHelper
    {
        public static bool ShouldDeferToHeldWrench(IPlayer player)
        {
            CollectibleObject collectible = player?.InventoryManager?.ActiveHotbarSlot?.Itemstack?.Collectible;
            return collectible is ItemKineticWrench
                || collectible?.Code?.Domain == "vintagekinematics" && collectible.Code.Path == "wrench-kinetic";
        }
    }
}
