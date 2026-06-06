using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using VintageKinematics.Api;
using VintageKinematics.Rendering;

namespace VintageKinematics.BlockEntities
{
    /// <summary>
    /// Passive inventory/controller for the crusher. Work progress is driven by the crusher head
    /// above it through the external inventory machine framework.
    /// </summary>
    public class BECrusherBasin : BEExternalInventoryMachineBase
    {
        public const int SlotInput = 0;
        public const int SlotOutputFirst = 1;
        public const int SlotOutputLast = 9;
        public const int InventorySize = 10;

        public const int PacketIdOpenDialog = 5400;

        private CrusherBasinItemRenderer itemRenderer;

        public BECrusherBasin()
            : base("crusherbasin", InventorySize, SlotInput, SlotOutputFirst, SlotOutputLast)
        {
        }

        protected override int OpenDialogPacketId => PacketIdOpenDialog;
        protected override string TitleLangCode => "vintagekinematics:basin-title";
        protected override string FallbackTitle => "Crusher Basin";

        protected override void OnAfterInventoryInitialized(ICoreAPI api)
        {
            if (api is ICoreClientAPI capi)
            {
                itemRenderer = new CrusherBasinItemRenderer(capi, Pos);
                capi.Event.RegisterRenderer(itemRenderer, EnumRenderStage.Opaque);
                itemRenderer.UpdateStack(MachineInventory[SlotInput]?.Itemstack);
            }
        }

        protected override void OnInventorySlotModified(int slotId)
        {
            base.OnInventorySlotModified(slotId);
            if (slotId == SlotInput && itemRenderer != null)
            {
                itemRenderer.UpdateStack(MachineInventory[SlotInput]?.Itemstack);
            }
        }

        protected override Vec3d OutputDropPosition()
        {
            return new Vec3d(Pos.X + 0.5, Pos.Y + 0.7, Pos.Z + 0.5);
        }

        public new void DepositOutput(ItemStack stack)
        {
            base.DepositOutput(stack);
        }

        public override void OnBlockUnloaded()
        {
            base.OnBlockUnloaded();
            DisposeRenderer();
        }

        public override void OnBlockRemoved()
        {
            base.OnBlockRemoved();
            DisposeRenderer();
        }

        private void DisposeRenderer()
        {
            if (Api is ICoreClientAPI capi && itemRenderer != null)
            {
                capi.Event.UnregisterRenderer(itemRenderer, EnumRenderStage.Opaque);
                itemRenderer.Dispose();
                itemRenderer = null;
            }
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor world)
        {
            base.FromTreeAttributes(tree, world);
            if (Api is ICoreClientAPI && itemRenderer != null)
            {
                itemRenderer.UpdateStack(MachineInventory[SlotInput]?.Itemstack);
            }
        }
    }
}
