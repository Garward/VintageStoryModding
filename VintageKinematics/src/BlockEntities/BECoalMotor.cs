using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using VintageKinematics.Api;
using VintageKinematics.Rendering;

namespace VintageKinematics.BlockEntities
{
    /// <summary>
    /// Single-slot fuel inventory that drives a <see cref="BEBehaviorKineticSource"/>.
    /// Each tick, if the source is idle and the slot holds a combustible item, one unit is
    /// consumed and the source is wound for that item's burn duration.
    /// </summary>
    public class BECoalMotor : BlockEntityOpenableContainer
    {
        public const int SlotFuel = 0;

        private readonly InventoryGeneric inventory;

        public override InventoryBase Inventory => inventory;
        public override string InventoryClassName => "coalmotor";

        public BECoalMotor()
        {
            inventory = new InventoryGeneric(1, "coalmotor-0", null, null);
        }

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            inventory.SlotModified += _ => MarkDirty(true);

            if (api.Side == EnumAppSide.Server)
            {
                RegisterGameTickListener(OnFuelTick, 500);
            }
        }

        public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tessThreadTesselator)
        {
            string[] excluded = KineticMeshSplitter.CollectManagedElements(this);
            var body = KineticMeshSplitter.TesselateBodyExcluding(Api as ICoreClientAPI, Block, tessThreadTesselator, excluded);
            if (body != null) mesher.AddMeshData(body);
            return true;
        }

        public override bool OnPlayerRightClick(IPlayer byPlayer, BlockSelection blockSel)
        {
            if (Api.World is IServerWorldAccessor)
            {
                string title = Lang.Get("vintagekinematics:coalmotor-title");
                if (string.IsNullOrEmpty(title) || title == "vintagekinematics:coalmotor-title") title = "Coal Motor";
                byte[] data = BlockEntityContainerOpen.ToBytes("BlockEntityInventory", title, 1, inventory);
                ((ICoreServerAPI)Api).Network.SendBlockEntityPacket(
                    (IServerPlayer)byPlayer, Pos, (int)EnumBlockContainerPacketId.OpenInventory, data);
                byPlayer.InventoryManager.OpenInventory(inventory);
            }
            return true;
        }

        private void OnFuelTick(float dt)
        {
            BEBehaviorKineticSource src = GetBehavior<BEBehaviorKineticSource>();
            if (src == null) return;

            // Top up before the current burn fully ends so RPM stays continuous when fuel is queued.
            if (src.DecaySeconds > 1f) return;

            ItemSlot slot = inventory[SlotFuel];
            if (slot.Empty) return;

            ItemStack stack = slot.Itemstack;
            CombustibleProperties props = stack.Collectible.GetCombustibleProperties(Api.World, stack, Pos);
            if (props == null || props.BurnDuration <= 0f) return;

            slot.TakeOut(1);
            slot.MarkDirty();
            src.Wind(props.BurnDuration);
        }
    }
}
