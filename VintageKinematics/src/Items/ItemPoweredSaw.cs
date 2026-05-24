using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace VintageKinematics.Items
{
    public class ItemPoweredSaw : ItemPoweredDrill
    {
        protected override string FuelDialogTitleLangCode => "vintagekinematics:poweredsaw-title";
        protected override string NoFuelLangCode => "vintagekinematics:poweredsaw-no-fuel";
        protected override string SpinningElementName => "blade";

        public override string GetHeldTpHitAnimation(ItemSlot slot, Entity byEntity)
        {
            return "drillactive";
        }

        public override string GetHeldTpUseAnimation(ItemSlot activeHotbarSlot, Entity forEntity)
        {
            return "drill";
        }
    }
}
