using System;
using System.IO;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using VintageKinematics.Gui;
using VintageKinematics.Rendering;

namespace VintageKinematics.Api
{
    public abstract class BEKineticForgePressBase : BlockEntityOpenableContainer, IFaceMappedContainer
    {
        private readonly string inventoryClassName;
        protected readonly InventoryGeneric inventory;
        private IOFaceMap ioFaces;
        private GuiDialogBlockEntity clientDialog;

        protected BEKineticForgePressBase(
            string inventoryClassName,
            int inventorySize,
            NewSlotDelegate slotFactory)
        {
            this.inventoryClassName = inventoryClassName;
            inventory = new InventoryGeneric(inventorySize, inventoryClassName + "-0", null, null, slotFactory);
        }

        public override InventoryBase Inventory => inventory;
        public override string InventoryClassName => inventoryClassName;
        public IOFaceMap IOFaces => ioFaces;

        protected IOFaceMap MachineIOFaces => ioFaces;
        protected virtual int OpenDialogPacketId => 5400;
        protected virtual float OutputPushIntervalMs => 250f;
        protected virtual int OutputPushBatch => 8;
        protected virtual string TitleLangCode => "vintagekinematics:" + inventoryClassName + "-title";
        protected virtual string FallbackTitle => inventoryClassName;

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            inventory.LateInitialize(inventoryClassName + "-" + Pos, api);
            inventory.ResolveBlocksOrItems();
            inventory.SlotModified += OnInventorySlotModified;

            OnAfterInventoryInitialized();
            RebuildIOFaceMap();

            if (api.Side == EnumAppSide.Server)
            {
                RegisterGameTickListener(OnServerPushTick, (int)OutputPushIntervalMs);
            }

            BEBehaviorKineticWorker worker = GetBehavior<BEBehaviorKineticWorker>();
            if (worker != null) worker.OnWorkCompleted += OnWorkCycle;
        }

        protected virtual void OnAfterInventoryInitialized() { }

        protected void RebuildIOFaceMap()
        {
            ioFaces = BuildIOFaceMap();
            ioFaces?.Apply(inventory);
        }

        protected abstract IOFaceMap BuildIOFaceMap();
        protected abstract GuiDialogBlockEntity CreateClientDialog(string title, ICoreClientAPI capi);

        protected virtual void OnInventorySlotModified(int slotId)
        {
            Api?.World?.BlockAccessor.GetChunkAtBlockPos(Pos)?.MarkModified();
        }

        protected virtual void OnServerPushTick(float dt)
        {
            MachineOutputHelper.FlushOutputs(this, inventory, ioFaces?.OutputEntries, OutputPushBatch);
        }

        protected virtual void OnWorkCycle(KineticWorkCompletedArgs args) { }

        public override bool OnPlayerRightClick(IPlayer byPlayer, BlockSelection blockSel)
        {
            if (Api.World is IServerWorldAccessor)
            {
                OpenDialog(byPlayer);
            }
            return true;
        }

        protected virtual void OpenDialog(IPlayer byPlayer)
        {
            string title = Lang.Get(TitleLangCode);
            if (string.IsNullOrEmpty(title) || title == TitleLangCode) title = FallbackTitle;

            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);
            bw.Write(title);

            var tree = new TreeAttribute();
            inventory.ToTreeAttributes(tree);
            WriteDialogState(tree);
            tree.ToBytes(bw);

            ((ICoreServerAPI)Api).Network.SendBlockEntityPacket((IServerPlayer)byPlayer, Pos, OpenDialogPacketId, ms.ToArray());
            byPlayer.InventoryManager.OpenInventory(inventory);
        }

        public override void OnReceivedClientPacket(IPlayer player, int packetid, byte[] data)
        {
            if (packetid == 1001)
            {
                player.InventoryManager?.CloseInventory(inventory);
                return;
            }

            if (HandleCustomClientPacket(player, packetid, data)) return;

            if (packetid < 1000)
            {
                if (!CheckClaim(player)) return;
                inventory.InvNetworkUtil.HandleClientPacket(player, packetid, data);
                return;
            }

            base.OnReceivedClientPacket(player, packetid, data);
        }

        protected virtual bool HandleCustomClientPacket(IPlayer player, int packetid, byte[] data) => false;

        protected bool CheckClaim(IPlayer player)
        {
            if (Api.World.Claims.TryAccess(player, Pos, EnumBlockAccessFlags.Use)) return true;
            Api.World.Logger.Audit("Player {0} sent {1} packet at {2} but has no claim access. Rejected.", player.PlayerName, inventoryClassName, Pos);
            return false;
        }

        public override void OnReceivedServerPacket(int packetid, byte[] data)
        {
            if (packetid != OpenDialogPacketId)
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
            ReadDialogState(tree);

            if (clientDialog == null)
            {
                clientDialog = CreateClientDialog(title, capi);
                clientDialog.OnClosed += () => clientDialog = null;
                clientDialog.TryOpen();
            }
            else
            {
                OnClientDialogUpdated(clientDialog);
            }
        }

        protected virtual void WriteDialogState(ITreeAttribute tree) => WriteState(tree);
        protected virtual void ReadDialogState(ITreeAttribute tree) => ReadState(tree);
        protected virtual void OnClientDialogUpdated(GuiDialogBlockEntity dialog) { }
        protected void RefreshClientDialog() => OnClientDialogUpdated(clientDialog);

        public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tessThreadTesselator)
        {
            string[] excluded = KineticMeshSplitter.CollectManagedElements(this);
            MeshData body = KineticMeshSplitter.TesselateBodyExcluding(Api as ICoreClientAPI, Block, tessThreadTesselator, excluded);
            if (body != null) mesher.AddMeshData(body);
            return true;
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            inventory?.ToTreeAttributes(tree);
            WriteState(tree);
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
        {
            base.FromTreeAttributes(tree, worldForResolving);
            inventory?.FromTreeAttributes(tree);
            ReadState(tree);
            if (Api != null)
            {
                inventory?.ResolveBlocksOrItems();
                OnAfterInventoryInitialized();
                RebuildIOFaceMap();
            }
            OnAfterStateRead();
            RefreshClientDialog();
        }

        protected virtual void WriteState(ITreeAttribute tree) { }
        protected virtual void ReadState(ITreeAttribute tree) { }
        protected virtual void OnAfterStateRead() { }

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

        private void DisposeDialog() => GuiDialogUtil.SafeDispose(ref clientDialog);
    }
}
