using System;
using System.Collections.Generic;
using System.IO;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace VintageKinematics.Api
{
    /// <summary>
    /// Shared shell for bore-style machines: inventory lifecycle, core drill state, claim-safe
    /// packet handling, dialog open/update flow, and common save/load.
    /// </summary>
    public abstract class BEBoreBase : BlockEntity
    {
        private readonly string inventoryClassName;
        protected readonly InventoryGeneric inventory;
        protected int drillDepth;
        protected bool halted;
        protected bool retracting;
        protected bool paused;
        private readonly List<BlockPos> placedColumnPositions = new List<BlockPos>();

        private GuiDialogBlockEntity clientDialog;

        protected BEBoreBase(
            string inventoryClassName,
            int inventorySize,
            NewSlotDelegate slotFactory)
        {
            this.inventoryClassName = inventoryClassName;
            inventory = new InventoryGeneric(inventorySize, inventoryClassName + "-0", null, null, slotFactory);
        }

        public InventoryBase Inventory => inventory;
        public int DrillDepth => drillDepth;
        public bool Halted => halted;
        public bool Retracting => retracting;
        public bool Paused => paused;
        public virtual bool HasUnretractedColumn => drillDepth > 0 || placedColumnPositions.Count > 0;
        protected IReadOnlyList<BlockPos> PlacedColumnPositions => placedColumnPositions;

        protected abstract int OpenDialogPacketId { get; }
        protected abstract int ToggleRetractPacketId { get; }
        protected abstract string TitleLangCode { get; }
        protected abstract string FallbackTitle { get; }

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            inventory.LateInitialize(inventoryClassName + "-" + Pos, api);
            inventory.ResolveBlocksOrItems();
            inventory.SlotModified += OnInventorySlotModified;
        }

        protected virtual void OnInventorySlotModified(int slotId)
        {
            Api?.World?.BlockAccessor.GetChunkAtBlockPos(Pos)?.MarkModified();
        }

        public virtual bool OnPlayerRightClick(IPlayer byPlayer, BlockSelection blockSel)
        {
            if (Api.World is IServerWorldAccessor)
            {
                OpenDialog(byPlayer);
            }
            return true;
        }

        private void OpenDialog(IPlayer byPlayer)
        {
            string title = Lang.Get(TitleLangCode);
            if (string.IsNullOrEmpty(title) || title == TitleLangCode) title = FallbackTitle;

            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);
            bw.Write(title);
            bw.Write(drillDepth);
            bw.Write(halted);
            bw.Write(retracting);
            bw.Write(paused);
            WriteExtraDialogState(bw);

            var tree = new TreeAttribute();
            inventory.ToTreeAttributes(tree);
            tree.ToBytes(bw);

            ((ICoreServerAPI)Api).Network.SendBlockEntityPacket((IServerPlayer)byPlayer, Pos, OpenDialogPacketId, ms.ToArray());
            byPlayer.InventoryManager.OpenInventory(inventory);
        }

        protected virtual void WriteExtraDialogState(BinaryWriter writer) { }
        protected virtual void ReadExtraDialogState(BinaryReader reader) { }

        public override void OnReceivedClientPacket(IPlayer player, int packetid, byte[] data)
        {
            if (packetid == 1001)
            {
                player.InventoryManager?.CloseInventory(inventory);
                return;
            }
            if (packetid == ToggleRetractPacketId)
            {
                if (!CheckClaim(player)) return;
                OnServerToggleRetract();
                MarkDirty(true);
                return;
            }
            if (packetid < 1000)
            {
                if (!CheckClaim(player)) return;
                inventory.InvNetworkUtil.HandleClientPacket(player, packetid, data);
                return;
            }
            base.OnReceivedClientPacket(player, packetid, data);
        }

        protected virtual void OnServerToggleRetract()
        {
            if (retracting)
            {
                retracting = false;
                paused = true;
            }
            else if (paused)
            {
                paused = false;
            }
            else
            {
                retracting = true;
                halted = false;
            }
        }

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
            drillDepth = br.ReadInt32();
            halted = br.ReadBoolean();
            retracting = br.ReadBoolean();
            paused = br.ReadBoolean();
            ReadExtraDialogState(br);

            var tree = new TreeAttribute();
            tree.FromBytes(br);
            inventory.FromTreeAttributes(tree);
            inventory.ResolveBlocksOrItems();

            if (clientDialog == null)
            {
                clientDialog = CreateClientDialog(title, capi);
                clientDialog.OnClosed += OnDialogClosed;
                clientDialog.TryOpen();
            }
            else
            {
                OnClientDialogUpdated(clientDialog);
            }
        }

        protected abstract GuiDialogBlockEntity CreateClientDialog(string title, ICoreClientAPI capi);
        protected abstract void OnClientDialogUpdated(GuiDialogBlockEntity dialog);

        private void OnDialogClosed()
        {
            clientDialog = null;
        }

        protected void SendClientToggleRetractPacket()
        {
            ((ICoreClientAPI)Api).Network.SendBlockEntityPacket(Pos, ToggleRetractPacketId);
        }

        protected void RefreshClientDialog()
        {
            if (clientDialog != null) OnClientDialogUpdated(clientDialog);
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
        {
            base.FromTreeAttributes(tree, worldAccessForResolve);
            inventory?.FromTreeAttributes(tree);
            drillDepth = tree.GetInt("drillDepth", 0);
            halted = tree.GetBool("halted", false);
            retracting = tree.GetBool("retracting", false);
            paused = tree.GetBool("paused", false);
            ReadExtraTreeAttributes(tree, worldAccessForResolve);

            if (Api != null) inventory?.ResolveBlocksOrItems();
            OnAfterTreeAttributesLoaded(worldAccessForResolve);
            RefreshClientDialog();
        }

        protected virtual void ReadExtraTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve) { }
        protected virtual void OnAfterTreeAttributesLoaded(IWorldAccessor worldAccessForResolve) { }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            inventory?.ToTreeAttributes(tree);
            tree.SetInt("drillDepth", drillDepth);
            tree.SetBool("halted", halted);
            tree.SetBool("retracting", retracting);
            tree.SetBool("paused", paused);
            WriteExtraTreeAttributes(tree);
        }

        protected virtual void WriteExtraTreeAttributes(ITreeAttribute tree) { }

        protected BlockPos CenterColumnPos(BlockPos baseCorner, int y) =>
            new BlockPos(baseCorner.X + 1, y, baseCorner.Z + 1, Pos.dimension);

        protected void ClearPlacedColumnPositions() => placedColumnPositions.Clear();

        protected void TrackPlacedColumnPosition(BlockPos columnPos)
        {
            if (columnPos != null) placedColumnPositions.Add(columnPos.Copy());
        }

        protected bool TryPlaceTrackedColumnBlock(BlockPos columnPos, int blockId)
        {
            if (blockId < 0 || columnPos == null) return false;
            if (!AutomationClaimUtil.CanAutomatedBlockAccess(Api.World, Pos, columnPos, EnumBlockAccessFlags.BuildOrBreak)) return false;

            Api.World.BlockAccessor.SetBlock(blockId, columnPos);
            TrackPlacedColumnPosition(columnPos);
            return true;
        }

        protected ItemStack RemoveTrackedColumnBlock(int expectedBlockId, ItemStack deployedStack, System.Func<Block, ItemStack> fallbackStackFactory)
        {
            if (placedColumnPositions.Count == 0) return null;
            int lastIdx = placedColumnPositions.Count - 1;
            BlockPos columnPos = placedColumnPositions[lastIdx];
            placedColumnPositions.RemoveAt(lastIdx);

            Block here = Api.World.BlockAccessor.GetBlock(columnPos);
            if (here?.Id != expectedBlockId) return null;
            if (!AutomationClaimUtil.CanAutomatedBlockAccess(Api.World, Pos, columnPos, EnumBlockAccessFlags.BuildOrBreak)) return null;

            Api.World.BlockAccessor.SetBlock(0, columnPos);
            if (deployedStack != null) return deployedStack;
            return fallbackStackFactory?.Invoke(here);
        }

        protected void RebuildPlacedColumnPositionsFromDepth()
        {
            placedColumnPositions.Clear();
            if (Api == null || Api.Side != EnumAppSide.Server || drillDepth <= 0) return;
            if (!MultiblockHelper.TryGetClaim(Block, Pos, out BlockPos baseCorner, out _)) return;

            for (int d = 1; d <= drillDepth; d++)
            {
                placedColumnPositions.Add(CenterColumnPos(baseCorner, baseCorner.Y - d));
            }
        }

        protected bool TryAdoptExistingColumn(int expectedBlockId, out BlockPos baseCorner)
        {
            baseCorner = null;
            if (Api == null || Api.Side != EnumAppSide.Server || expectedBlockId < 0) return false;
            if (!MultiblockHelper.TryGetClaim(Block, Pos, out baseCorner, out _)) return false;

            placedColumnPositions.Clear();
            for (int y = baseCorner.Y - 1; y > 0; y--)
            {
                BlockPos columnPos = CenterColumnPos(baseCorner, y);
                Block here = Api.World.BlockAccessor.GetBlock(columnPos);
                if (here?.Id != expectedBlockId) break;
                placedColumnPositions.Add(columnPos);
            }

            return placedColumnPositions.Count > 0;
        }

        protected void WritePlacedColumnPositions(ITreeAttribute tree, string key)
        {
            ITreeAttribute positionsTree = new TreeAttribute();
            positionsTree.SetInt("count", placedColumnPositions.Count);
            for (int i = 0; i < placedColumnPositions.Count; i++)
            {
                BlockPos p = placedColumnPositions[i];
                positionsTree.SetInt("x" + i, p.X);
                positionsTree.SetInt("y" + i, p.Y);
                positionsTree.SetInt("z" + i, p.Z);
                positionsTree.SetInt("d" + i, p.dimension);
            }
            tree[key] = positionsTree;
        }

        protected void ReadPlacedColumnPositions(ITreeAttribute tree, string key)
        {
            placedColumnPositions.Clear();
            ITreeAttribute positionsTree = tree.GetTreeAttribute(key);
            if (positionsTree == null) return;

            int count = positionsTree.GetInt("count", 0);
            for (int i = 0; i < count; i++)
            {
                placedColumnPositions.Add(new BlockPos(
                    positionsTree.GetInt("x" + i),
                    positionsTree.GetInt("y" + i),
                    positionsTree.GetInt("z" + i),
                    positionsTree.GetInt("d" + i, Pos.dimension)));
            }
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

        protected void DisposeDialog() => GuiDialogUtil.SafeDispose(ref clientDialog);
    }
}
