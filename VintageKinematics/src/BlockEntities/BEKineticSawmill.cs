using System.IO;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using VintageKinematics.Api;
using VintageKinematics.Crafting;
using VintageKinematics.Gui;
using VintageKinematics.Rendering;

namespace VintageKinematics.BlockEntities
{
    /// <summary>
    /// Single-block sawmill: top face inputs logs, bottom face outputs the result. Each completed
    /// kinetic-work cycle consumes 1 log and produces the recipe outputs (planks or shafts depending
    /// on the toggled mode), pushing them downward to any adjacent funnel/storage.
    /// </summary>
    public class BEKineticSawmill : BlockEntity
    {
        public const int SlotInput = 0;
        public const int SlotOutputFirst = 1;
        public const int SlotOutputLast = 4;
        public const int InventorySize = 5;

        public const int PacketIdOpenDialog = 5400;
        public const int PacketIdToggleMode = 5401;

        private const float OutputPushIntervalMs = 250f;
        private const int OutputPushBatch = 8;

        private readonly InventoryGeneric inventory;
        private IOFaceMap ioFaces;
        private SawmillMode mode = SawmillMode.Plank;
        private GuiDialogKineticSawmill clientDialog;

        public SawmillMode Mode => mode;

        public BEKineticSawmill()
        {
            inventory = new InventoryGeneric(InventorySize, "kineticsawmill-0", null, null);
        }

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            inventory.LateInitialize("kineticsawmill-" + Pos, api);
            inventory.ResolveBlocksOrItems();
            inventory.SlotModified += _ => MarkDirty(true);

            ioFaces = new IOFaceMap().MapInput(BlockFacing.UP, SlotInput);
            for (int i = SlotOutputFirst; i <= SlotOutputLast; i++)
            {
                ioFaces.MapOutput(BlockFacing.DOWN, i);
            }
            ioFaces.Apply(inventory);

            if (api.Side == EnumAppSide.Server)
            {
                RegisterGameTickListener(OnServerPushTick, (int)OutputPushIntervalMs);
            }

            BEBehaviorKineticWorker worker = GetBehavior<BEBehaviorKineticWorker>();
            if (worker != null) worker.OnWorkCompleted += OnWorkCycle;
        }

        private void OnServerPushTick(float dt)
        {
            if (ioFaces == null) return;
            foreach (BlockFacing face in ioFaces.OutputFaces)
            {
                foreach (int slotId in ioFaces.OutputSlotsFor(face))
                {
                    ItemSlot slot = inventory[slotId];
                    if (slot.Empty) continue;
                    int moved = InventoryPusher.TryPush(Api.World, Pos, face, slot, OutputPushBatch);
                    if (moved > 0) MarkDirty(true);
                }
            }
        }

        private static readonly AssetLocation SawSound = new AssetLocation("sounds/block/woodchop.ogg");

        private void OnWorkCycle(KineticWorkCompletedArgs args)
        {
            ItemSlot slot = inventory[SlotInput];
            if (slot.Empty) return;

            ItemStack input = slot.Itemstack;
            var registry = Api.ModLoader.GetModSystem<KineticSawmillRecipeRegistry>();
            var recipe = registry?.FindRecipe(input, mode);
            if (recipe == null) return;

            Api.World.PlaySoundAt(SawSound, Pos.X + 0.5, Pos.Y + 0.5, Pos.Z + 0.5, null, randomizePitch: true, range: 16, volume: 0.6f);

            slot.TakeOut(1);
            slot.MarkDirty();

            if (recipe.Outputs == null) return;
            foreach (var jstack in recipe.Outputs)
            {
                if (jstack?.ResolvedItemstack == null) continue;
                DepositOutput(jstack.ResolvedItemstack.Clone());
            }
        }

        private void DepositOutput(ItemStack stack)
        {
            if (stack == null || stack.StackSize <= 0) return;

            for (int i = SlotOutputFirst; i <= SlotOutputLast; i++)
            {
                ItemSlot s = inventory[i];
                if (s.Empty) continue;
                if (!s.Itemstack.Collectible.Code.Equals(stack.Collectible.Code)) continue;
                int max = s.Itemstack.Collectible.MaxStackSize;
                int free = max - s.Itemstack.StackSize;
                if (free <= 0) continue;
                int take = System.Math.Min(free, stack.StackSize);
                s.Itemstack.StackSize += take;
                stack.StackSize -= take;
                s.MarkDirty();
                if (stack.StackSize <= 0) return;
            }

            for (int i = SlotOutputFirst; i <= SlotOutputLast; i++)
            {
                ItemSlot s = inventory[i];
                if (!s.Empty) continue;
                s.Itemstack = stack.Clone();
                s.MarkDirty();
                return;
            }

            Vec3d at = new Vec3d(Pos.X + 0.5, Pos.Y + 0.1, Pos.Z + 0.5);
            Api.World.SpawnItemEntity(stack, at);
        }

        public bool OnPlayerRightClick(IPlayer byPlayer, BlockSelection blockSel)
        {
            if (Api.World is IServerWorldAccessor)
            {
                string title = Lang.Get("vintagekinematics:kineticsawmill-title");
                if (string.IsNullOrEmpty(title) || title == "vintagekinematics:kineticsawmill-title") title = "Sawmill";

                using var ms = new MemoryStream();
                using var bw = new BinaryWriter(ms);
                bw.Write(title);
                bw.Write((int)mode);
                var tree = new TreeAttribute();
                inventory.ToTreeAttributes(tree);
                tree.ToBytes(bw);
                byte[] data = ms.ToArray();

                ((ICoreServerAPI)Api).Network.SendBlockEntityPacket(
                    (IServerPlayer)byPlayer, Pos, PacketIdOpenDialog, data);
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
            if (packetid == PacketIdToggleMode)
            {
                if (!CheckClaim(player)) return;
                mode = mode == SawmillMode.Plank ? SawmillMode.Shaft : SawmillMode.Plank;
                MarkDirty(true);
                return;
            }
            if (packetid < 1000)
            {
                if (!CheckClaim(player)) return;
                inventory.InvNetworkUtil.HandleClientPacket(player, packetid, data);
            }
        }

        private bool CheckClaim(IPlayer player)
        {
            if (Api.World.Claims.TryAccess(player, Pos, EnumBlockAccessFlags.Use)) return true;
            Api.World.Logger.Audit("Player {0} sent sawmill packet at {1} but has no claim access. Rejected.", player.PlayerName, Pos);
            return false;
        }

        public override void OnReceivedServerPacket(int packetid, byte[] data)
        {
            if (packetid != PacketIdOpenDialog) return;

            ICoreClientAPI capi = Api as ICoreClientAPI;
            if (capi == null) return;

            using var ms = new MemoryStream(data);
            using var br = new BinaryReader(ms);
            string title = br.ReadString();
            mode = (SawmillMode)br.ReadInt32();
            var tree = new TreeAttribute();
            tree.FromBytes(br);
            inventory.FromTreeAttributes(tree);
            inventory.ResolveBlocksOrItems();

            if (clientDialog == null)
            {
                clientDialog = new GuiDialogKineticSawmill(
                    title, inventory, Pos,
                    () => mode, OnClientToggleMode, capi);
                clientDialog.OnClosed += OnDialogClosed;
                clientDialog.TryOpen();
            }
            else
            {
                clientDialog.OnModeUpdated();
            }
        }

        private void OnClientToggleMode()
        {
            mode = mode == SawmillMode.Plank ? SawmillMode.Shaft : SawmillMode.Plank;
            ((ICoreClientAPI)Api).Network.SendBlockEntityPacket(Pos, PacketIdToggleMode);
            clientDialog?.OnModeUpdated();
        }

        private void OnDialogClosed()
        {
            clientDialog = null;
        }

        public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tessThreadTesselator)
        {
            string[] excluded = KineticMeshSplitter.CollectManagedElements(this);
            var body = KineticMeshSplitter.TesselateBodyExcluding(Api as ICoreClientAPI, Block, tessThreadTesselator, excluded);
            if (body != null) mesher.AddMeshData(body);
            return true;
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            inventory?.ToTreeAttributes(tree);
            tree.SetInt("sawmillMode", (int)mode);
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
        {
            base.FromTreeAttributes(tree, worldForResolving);
            inventory?.FromTreeAttributes(tree);
            mode = (SawmillMode)tree.GetInt("sawmillMode", 0);
            if (Api != null) inventory?.ResolveBlocksOrItems();
            clientDialog?.OnModeUpdated();
        }

        public override void OnBlockUnloaded()
        {
            base.OnBlockUnloaded();
            DisposeDialog();
        }

        public override void OnBlockRemoved()
        {
            base.OnBlockRemoved();
            DisposeDialog();
        }

        private void DisposeDialog()
        {
            if (clientDialog == null) return;
            if (clientDialog.IsOpened()) clientDialog.TryClose();
            clientDialog.Dispose();
            clientDialog = null;
        }
    }
}
