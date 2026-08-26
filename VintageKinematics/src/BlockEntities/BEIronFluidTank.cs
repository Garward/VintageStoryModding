using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.GameContent;
using VintageKinematics.Blocks;

namespace VintageKinematics.BlockEntities
{
    public class BEIronFluidTank : BlockEntityLiquidContainer
    {
        private const float DefaultCapacityLitres = 100f;

        public override string InventoryClassName => "ironfluidtank";

        public BEIronFluidTank()
        {
            inventory = new InventoryGeneric(
                1,
                null,
                null,
                (_, self) => new ItemSlotLiquidOnly(self, DefaultCapacityLitres));
            inventory.SlotModified += _ => MarkDirty(true);
        }

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);

            float capacity = (Block as BlockIronFluidTank)?.CapacityLitres ?? DefaultCapacityLitres;
            if (inventory[0] is ItemSlotLiquidOnly liquidSlot)
            {
                liquidSlot.CapacityLitres = capacity;
            }
        }

        public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
        {
            ItemStack content = GetContent();
            float capacity = (Block as BlockIronFluidTank)?.CapacityLitres ?? DefaultCapacityLitres;
            if (content == null)
            {
                dsc.AppendLine(Lang.Get("vintagekinematics:ironfluidtank-empty", capacity));
                return;
            }

            WaterTightContainableProps props = BlockLiquidContainerBase.GetContainableProps(content);
            float litres = props?.ItemsPerLitre > 0f ? content.StackSize / props.ItemsPerLitre : 0f;
            dsc.AppendLine(Lang.Get(
                "vintagekinematics:ironfluidtank-contents",
                litres,
                capacity,
                content.GetName()));
        }
    }
}
