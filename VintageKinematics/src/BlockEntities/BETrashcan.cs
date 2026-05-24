using System.IO;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using VintageKinematics.Api;
using VintageKinematics.Gui;

namespace VintageKinematics.BlockEntities
{
    /// <summary>
    /// Filtered automation sink. It is deliberately not an inventory; only belts and funnels call
    /// its accept methods, so adjacent machine outputs cannot delete items directly.
    /// </summary>
    public class BETrashcan : BlockEntity, IAutomationItemSink
    {
        private const int FilterSlotCount = 1;

        public const int PacketIdOpenDialog = 5400;
        public const int PacketIdToggleMode = 5401;
        public const int PacketIdSetFilter = 5402;
        public const int PacketIdToggleFuzzy = 5403;

        private InventoryFunnelFilter filterInv;
        private bool whitelist;
        private bool fuzzy;
        private bool suppressClientSync;
        private GuiDialogFunnelFilter invDialog;

        public bool Whitelist => whitelist;
        public bool Fuzzy => fuzzy;

        public BETrashcan()
        {
            filterInv = new InventoryFunnelFilter(FilterSlotCount, "trashcanfilter", null);
        }

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            filterInv.LateInitialize("trashcanfilter-" + Pos, api);
            filterInv.ResolveBlocksOrItems();

            if (api.Side == EnumAppSide.Client)
            {
                filterInv.SlotModified += OnClientFilterSlotModified;
            }
        }

        public bool TryAcceptFromBelt(ItemStack stack) => TryDelete(stack);

        public bool TryAcceptFromFunnel(ItemStack stack) => TryDelete(stack);

        public bool MatchesFilter(ItemStack stack) => ItemFilterMatcher.Matches(stack, filterInv, FilterSlotCount, whitelist, fuzzy);

        private bool TryDelete(ItemStack stack)
        {
            if (Api?.Side != EnumAppSide.Server) return false;
            if (stack == null || stack.StackSize <= 0) return true;
            if (!MatchesFilter(stack)) return false;

            stack.StackSize = 0;
            MarkDirty(false);
            return true;
        }

        private void OnClientFilterSlotModified(int slotId)
        {
            if (Api?.Side != EnumAppSide.Client || suppressClientSync) return;

            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);
            bw.Write(slotId);
            ItemSlot slot = filterInv[slotId];
            if (slot.Empty)
            {
                bw.Write(false);
            }
            else
            {
                bw.Write(true);
                slot.Itemstack.ToBytes(bw);
            }

            ((ICoreClientAPI)Api).Network.SendBlockEntityPacket(Pos, PacketIdSetFilter, ms.ToArray());
        }

        public bool OnPlayerRightClick(IPlayer byPlayer, BlockSelection blockSel)
        {
            if (Api.World is IServerWorldAccessor)
            {
                string title = Lang.Get("vintagekinematics:trashcan-filter-title");
                if (string.IsNullOrEmpty(title) || title == "vintagekinematics:trashcan-filter-title") title = "Trashcan Filter";

                using var ms = new MemoryStream();
                using var bw = new BinaryWriter(ms);
                bw.Write(title);
                bw.Write(whitelist);
                bw.Write(fuzzy);
                var tree = new TreeAttribute();
                filterInv.ToTreeAttributes(tree);
                tree.ToBytes(bw);

                ((ICoreServerAPI)Api).Network.SendBlockEntityPacket(
                    (IServerPlayer)byPlayer, Pos, PacketIdOpenDialog, ms.ToArray());
                byPlayer.InventoryManager.OpenInventory(filterInv);
            }

            return true;
        }

        public override void OnReceivedClientPacket(IPlayer player, int packetid, byte[] data)
        {
            if (packetid == 1001)
            {
                player.InventoryManager?.CloseInventory(filterInv);
                return;
            }
            if (packetid == PacketIdToggleMode)
            {
                if (!CheckClaim(player)) return;
                whitelist = !whitelist;
                MarkDirty(true);
                return;
            }
            if (packetid == PacketIdToggleFuzzy)
            {
                if (!CheckClaim(player)) return;
                fuzzy = !fuzzy;
                MarkDirty(true);
                return;
            }
            if (packetid == PacketIdSetFilter)
            {
                if (!CheckClaim(player)) return;
                using var ms = new MemoryStream(data);
                using var br = new BinaryReader(ms);
                int slotId = br.ReadInt32();
                if (slotId != 0) return;

                bool hasStack = br.ReadBoolean();
                ItemStack stack = null;
                if (hasStack)
                {
                    stack = new ItemStack();
                    stack.FromBytes(br);
                    stack.ResolveBlockOrItem(Api.World);
                    stack.StackSize = 1;
                }

                filterInv[0].Itemstack = stack;
                filterInv[0].MarkDirty();
                MarkDirty(true);
                return;
            }
            if (packetid < 1000)
            {
                if (!CheckClaim(player)) return;
                filterInv.InvNetworkUtil.HandleClientPacket(player, packetid, data);
            }
        }

        private bool CheckClaim(IPlayer player)
        {
            if (Api.World.Claims.TryAccess(player, Pos, EnumBlockAccessFlags.Use)) return true;
            Api.World.Logger.Audit("Player {0} sent trashcan filter packet at {1} but has no claim access. Rejected.", player.PlayerName, Pos);
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
            whitelist = br.ReadBoolean();
            fuzzy = br.ReadBoolean();
            var tree = new TreeAttribute();
            tree.FromBytes(br);

            suppressClientSync = true;
            try
            {
                filterInv.FromTreeAttributes(tree);
                filterInv.ResolveBlocksOrItems();
            }
            finally
            {
                suppressClientSync = false;
            }

            if (invDialog == null)
            {
                invDialog = new GuiDialogFunnelFilter(
                    title, filterInv, Pos, FilterSlotCount,
                    () => whitelist, () => fuzzy,
                    OnClientToggleMode, OnClientToggleFuzzy, capi);
                invDialog.OnClosed += OnDialogClosed;
                invDialog.TryOpen();
            }
            else
            {
                invDialog.OnFilterStateUpdated();
            }
        }

        private void OnClientToggleMode()
        {
            whitelist = !whitelist;
            ((ICoreClientAPI)Api).Network.SendBlockEntityPacket(Pos, PacketIdToggleMode);
            invDialog?.OnFilterStateUpdated();
        }

        private void OnClientToggleFuzzy()
        {
            fuzzy = !fuzzy;
            ((ICoreClientAPI)Api).Network.SendBlockEntityPacket(Pos, PacketIdToggleFuzzy);
            invDialog?.OnFilterStateUpdated();
        }

        private void OnDialogClosed()
        {
            invDialog = null;
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            filterInv?.ToTreeAttributes(tree);
            tree.SetBool("whitelist", whitelist);
            tree.SetBool("fuzzy", fuzzy);
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
        {
            base.FromTreeAttributes(tree, worldForResolving);
            filterInv ??= new InventoryFunnelFilter(FilterSlotCount, "trashcanfilter", null);
            suppressClientSync = true;
            try
            {
                filterInv.FromTreeAttributes(tree);
            }
            finally
            {
                suppressClientSync = false;
            }

            whitelist = tree.GetBool("whitelist", false);
            fuzzy = tree.GetBool("fuzzy", false);

            if (Api != null) filterInv.ResolveBlocksOrItems();
            invDialog?.OnFilterStateUpdated();
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

        private void DisposeDialog() => GuiDialogUtil.SafeDispose(ref invDialog);
    }
}
