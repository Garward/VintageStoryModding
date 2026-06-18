using System.IO;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using VintageKinematics.Api;
using VintageKinematics.Gui;
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
        public const int PacketIdOpenDialog = 5800;

        private readonly InventoryGeneric inventory;
        private GuiDialogCoalMotor clientDialog;
        public string DialogTitle { get; private set; }

        public override InventoryBase Inventory => inventory;
        public override string InventoryClassName => "coalmotor";

        public BECoalMotor()
        {
            inventory = new InventoryGeneric(1, "coalmotor-0", null, null);
        }

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            inventory.SlotModified += _ => Api.World.BlockAccessor.GetChunkAtBlockPos(Pos)?.MarkModified();

            string title = Lang.Get("vintagekinematics:coalmotor-title");
            if (string.IsNullOrEmpty(title) || title == "vintagekinematics:coalmotor-title") title = "Coal Motor";
            DialogTitle = title;

            if (api.Side == EnumAppSide.Server)
            {
                RegisterGameTickListener(OnFuelTick, KineticGeneratorAttributes.TickMs(Block, 500));
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

                using var ms = new MemoryStream();
                using var bw = new BinaryWriter(ms);
                bw.Write(title);
                var tree = new TreeAttribute();
                inventory.ToTreeAttributes(tree);
                tree.ToBytes(bw);

                ((ICoreServerAPI)Api).Network.SendBlockEntityPacket(
                    (IServerPlayer)byPlayer, Pos, PacketIdOpenDialog, ms.ToArray());
                byPlayer.InventoryManager.OpenInventory(inventory);
            }
            return true;
        }

        public override void OnReceivedClientPacket(IPlayer player, int packetid, byte[] data)
        {
            if (packetid == 1001)
            {
                player.InventoryManager?.CloseInventory(inventory);
                return;
            }
            if (packetid < 1000)
            {
                if (!Api.World.Claims.TryAccess(player, Pos, EnumBlockAccessFlags.Use))
                {
                    Api.World.Logger.Audit("Player {0} sent coalmotor packet at {1} but has no claim access. Rejected.", player.PlayerName, Pos);
                    return;
                }
                inventory.InvNetworkUtil.HandleClientPacket(player, packetid, data);
                return;
            }
            base.OnReceivedClientPacket(player, packetid, data);
        }

        public override void OnReceivedServerPacket(int packetid, byte[] data)
        {
            if (packetid != PacketIdOpenDialog)
            {
                base.OnReceivedServerPacket(packetid, data);
                return;
            }

            ICoreClientAPI capi = Api as ICoreClientAPI;
            if (capi == null) return;

            using var ms = new MemoryStream(data);
            using var br = new BinaryReader(ms);
            string title = br.ReadString();
            var tree = new TreeAttribute();
            tree.FromBytes(br);
            inventory.FromTreeAttributes(tree);
            inventory.ResolveBlocksOrItems();

            if (clientDialog == null)
            {
                clientDialog = new GuiDialogCoalMotor(title, inventory, Pos, capi);
                clientDialog.OnClosed += () => clientDialog = null;
                clientDialog.TryOpen();
            }
        }

        public override void OnBlockUnloaded()
        {
            base.OnBlockUnloaded();
            GuiDialogUtil.SafeDispose(ref clientDialog);
        }

        public override void OnBlockRemoved()
        {
            base.OnBlockRemoved();
            GuiDialogUtil.SafeDispose(ref clientDialog);
        }

        public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
        {
            base.GetBlockInfo(forPlayer, dsc);

            float activeSeconds = GetBehavior<BEBehaviorKineticSource>()?.EstimatedRemainingSeconds() ?? 0f;
            float queuedSeconds = EstimateQueuedFuelSeconds();
            float totalSeconds = activeSeconds + queuedSeconds;
            if (totalSeconds <= 0f) return;

            dsc.AppendLine(Lang.Get("vintagekinematics:coalmotor-fuel-time", FormatDuration(totalSeconds)));
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            inventory.ToTreeAttributes(tree);
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
        {
            base.FromTreeAttributes(tree, worldForResolving);
            inventory.FromTreeAttributes(tree);
            inventory.ResolveBlocksOrItems();
        }

        private void OnFuelTick(float dt)
        {
            BEBehaviorKineticSource src = GetBehavior<BEBehaviorKineticSource>();
            if (src == null) return;

            if (src.DecaySeconds > KineticGeneratorAttributes.SourceRefreshThresholdSeconds(Block, 1f)) return;

            ItemSlot slot = inventory[SlotFuel];
            if (slot.Empty) return;

            ItemStack stack = slot.Itemstack;
            CombustibleProperties props = stack.Collectible.GetCombustibleProperties(Api.World, stack, Pos);
            if (props == null || props.BurnDuration <= 0f) return;

            slot.TakeOut(1);
            slot.MarkDirty();
            float fuelUsageSpeed = Api.ModLoader.GetModSystem<KineticConfigSystem>()?.Config?.ResolveCoalMotorFuelUsageSpeed() ?? 1f;
            src.Wind(props.BurnDuration / fuelUsageSpeed, KineticSourceDirection.ForHorizontalSide(Block, "n"));
        }

        private float EstimateQueuedFuelSeconds()
        {
            ItemSlot slot = inventory[SlotFuel];
            if (slot.Empty) return 0f;

            ItemStack stack = slot.Itemstack;
            CombustibleProperties props = stack?.Collectible?.GetCombustibleProperties(Api?.World, stack, Pos);
            if (props == null || props.BurnDuration <= 0f) return 0f;

            float fuelUsageSpeed = Api?.ModLoader.GetModSystem<KineticConfigSystem>()?.Config?.ResolveCoalMotorFuelUsageSpeed() ?? 1f;
            if (fuelUsageSpeed <= 0f) fuelUsageSpeed = 1f;

            return props.BurnDuration * stack.StackSize / fuelUsageSpeed;
        }

        private static string FormatDuration(float seconds)
        {
            int total = System.Math.Max(0, (int)System.MathF.Ceiling(seconds));
            int hours = total / 3600;
            int minutes = (total % 3600) / 60;
            int secs = total % 60;

            if (hours > 0) return $"{hours}h {minutes:00}m";
            if (minutes > 0) return $"{minutes}m {secs:00}s";
            return $"{secs}s";
        }
    }
}
