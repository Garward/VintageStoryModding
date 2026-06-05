using System;
using System.IO;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using VintageKinematics.Gui;

namespace VintageKinematics.Api
{
    public class FilterDialogController
    {
        private const int PacketIdCloseInventory = 1001;

        private readonly BlockEntity owner;
        private readonly InventoryGeneric inventory;
        private readonly string inventoryClassName;
        private readonly string titleLangCode;
        private readonly string fallbackTitle;
        private readonly string auditName;
        private readonly int openDialogPacketId;
        private readonly int toggleModePacketId;
        private readonly int setFilterPacketId;
        private readonly int toggleFuzzyPacketId;
        private readonly Func<int> activeSlotCount;
        private readonly Func<bool> centerSingleSlot;
        private readonly System.Func<ItemStack, bool> validateFilterStack;

        private bool suppressClientSync;
        private GuiDialogFunnelFilter dialog;

        public FilterDialogController(
            BlockEntity owner,
            InventoryGeneric inventory,
            string inventoryClassName,
            string titleLangCode,
            string fallbackTitle,
            string auditName,
            int openDialogPacketId,
            int toggleModePacketId,
            int setFilterPacketId,
            int toggleFuzzyPacketId,
            Func<int> activeSlotCount,
            Func<bool> centerSingleSlot = null,
            System.Func<ItemStack, bool> validateFilterStack = null)
        {
            this.owner = owner;
            this.inventory = inventory;
            this.inventoryClassName = inventoryClassName;
            this.titleLangCode = titleLangCode;
            this.fallbackTitle = fallbackTitle;
            this.auditName = auditName;
            this.openDialogPacketId = openDialogPacketId;
            this.toggleModePacketId = toggleModePacketId;
            this.setFilterPacketId = setFilterPacketId;
            this.toggleFuzzyPacketId = toggleFuzzyPacketId;
            this.activeSlotCount = activeSlotCount;
            this.centerSingleSlot = centerSingleSlot;
            this.validateFilterStack = validateFilterStack;
        }

        public InventoryGeneric Inventory => inventory;
        public bool Whitelist { get; private set; }
        public bool Fuzzy { get; private set; }
        public int ActiveSlotCount => GameMath.Clamp(activeSlotCount?.Invoke() ?? inventory.Count, 0, inventory.Count);

        public int ExtraTogglePacketId { get; set; } = -1;
        public Func<bool> CanToggleExtra { get; set; }
        public Action ToggleExtraServer { get; set; }
        public Action ToggleExtraClient { get; set; }
        public Func<string> ExtraToggleLabel { get; set; }
        public Action<ITreeAttribute> WriteExtraState { get; set; }
        public Action<ITreeAttribute> ReadExtraState { get; set; }

        public void Initialize(ICoreAPI api)
        {
            inventory.LateInitialize(inventoryClassName + "-" + owner.Pos, api);
            inventory.ResolveBlocksOrItems();

            if (api.Side == EnumAppSide.Client)
            {
                inventory.SlotModified += OnClientFilterSlotModified;
            }
        }

        public bool Matches(ItemStack stack)
        {
            return ItemFilterMatcher.Matches(stack, inventory, ActiveSlotCount, Whitelist, Fuzzy);
        }

        public ItemFilterResult Evaluate(ItemStack stack)
        {
            return ItemFilterMatcher.Evaluate(stack, inventory, ActiveSlotCount, Whitelist, Fuzzy);
        }

        private void OnClientFilterSlotModified(int slotId)
        {
            if (owner.Api?.Side != EnumAppSide.Client || suppressClientSync) return;

            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);
            bw.Write(slotId);
            ItemSlot slot = inventory[slotId];
            if (slot.Empty)
            {
                bw.Write(false);
            }
            else
            {
                bw.Write(true);
                slot.Itemstack.ToBytes(bw);
            }

            ((ICoreClientAPI)owner.Api).Network.SendBlockEntityPacket(owner.Pos, setFilterPacketId, ms.ToArray());
        }

        public bool Open(IPlayer byPlayer)
        {
            if (owner.Api.World is IServerWorldAccessor)
            {
                string title = Lang.Get(titleLangCode);
                if (string.IsNullOrEmpty(title) || title == titleLangCode) title = fallbackTitle;

                using var ms = new MemoryStream();
                using var bw = new BinaryWriter(ms);
                bw.Write(title);
                bw.Write(Whitelist);
                bw.Write(Fuzzy);
                var tree = new TreeAttribute();
                WriteToTree(tree);
                tree.ToBytes(bw);

                ((ICoreServerAPI)owner.Api).Network.SendBlockEntityPacket((IServerPlayer)byPlayer, owner.Pos, openDialogPacketId, ms.ToArray());
                byPlayer.InventoryManager.OpenInventory(inventory);
            }

            return true;
        }

        public bool OnReceivedClientPacket(IPlayer player, int packetid, byte[] data)
        {
            if (packetid == PacketIdCloseInventory)
            {
                player.InventoryManager?.CloseInventory(inventory);
                return true;
            }

            if (packetid == toggleModePacketId)
            {
                if (!CheckClaim(player)) return true;
                Whitelist = !Whitelist;
                owner.MarkDirty(true);
                return true;
            }

            if (packetid == toggleFuzzyPacketId)
            {
                if (!CheckClaim(player)) return true;
                Fuzzy = !Fuzzy;
                owner.MarkDirty(true);
                return true;
            }

            if (packetid == ExtraTogglePacketId && ToggleExtraServer != null)
            {
                if (!CheckClaim(player)) return true;
                if (CanToggleExtra != null && !CanToggleExtra()) return true;
                ToggleExtraServer();
                owner.MarkDirty(true);
                return true;
            }

            if (packetid == setFilterPacketId)
            {
                if (!CheckClaim(player)) return true;
                ApplySetFilterPacket(data);
                return true;
            }

            if (packetid < 1000)
            {
                if (!CheckClaim(player)) return true;
                // Ghost filter slots are synchronized by setFilterPacketId. Letting vanilla
                // inventory packets mutate them can race the custom packet and clear filters.
                return true;
            }

            return false;
        }

        private void ApplySetFilterPacket(byte[] data)
        {
            using var ms = new MemoryStream(data);
            using var br = new BinaryReader(ms);
            int slotId = br.ReadInt32();
            if (slotId < 0 || slotId >= ActiveSlotCount) return;

            bool hasStack = br.ReadBoolean();
            ItemStack stack = null;
            if (hasStack)
            {
                stack = new ItemStack();
                stack.FromBytes(br);
                stack.ResolveBlockOrItem(owner.Api.World);
                stack.StackSize = 1;
                if (validateFilterStack != null && !validateFilterStack(stack)) return;
            }

            inventory[slotId].Itemstack = stack;
            inventory[slotId].MarkDirty();
            owner.MarkDirty(true);
        }

        private bool CheckClaim(IPlayer player)
        {
            if (owner.Api.World.Claims.TryAccess(player, owner.Pos, EnumBlockAccessFlags.Use)) return true;
            owner.Api.World.Logger.Audit("Player {0} sent {1} filter packet at {2} but has no claim access. Rejected.", player.PlayerName, auditName, owner.Pos);
            return false;
        }

        public bool OnReceivedServerPacket(int packetid, byte[] data)
        {
            if (packetid != openDialogPacketId) return false;

            ICoreClientAPI capi = owner.Api as ICoreClientAPI;
            if (capi == null) return true;

            using var ms = new MemoryStream(data);
            using var br = new BinaryReader(ms);
            string title = br.ReadString();
            Whitelist = br.ReadBoolean();
            Fuzzy = br.ReadBoolean();
            var tree = new TreeAttribute();
            tree.FromBytes(br);
            ReadFromTree(tree);

            bool showExtraToggle = ExtraToggleLabel != null && (CanToggleExtra == null || CanToggleExtra());

            if (dialog == null)
            {
                dialog = new GuiDialogFunnelFilter(
                    title,
                    inventory,
                    owner.Pos,
                    ActiveSlotCount,
                    () => Whitelist,
                    () => Fuzzy,
                    OnClientToggleMode,
                    OnClientToggleFuzzy,
                    capi,
                    showExtraToggle ? ExtraToggleLabel : null,
                    showExtraToggle ? OnClientToggleExtra : null,
                    centerSingleSlot: centerSingleSlot?.Invoke() ?? false);
                dialog.OnClosed += OnDialogClosed;
                dialog.TryOpen();
            }
            else
            {
                dialog.OnFilterStateUpdated();
            }

            return true;
        }

        private void OnClientToggleMode()
        {
            Whitelist = !Whitelist;
            ((ICoreClientAPI)owner.Api).Network.SendBlockEntityPacket(owner.Pos, toggleModePacketId);
            dialog?.OnFilterStateUpdated();
        }

        private void OnClientToggleFuzzy()
        {
            Fuzzy = !Fuzzy;
            ((ICoreClientAPI)owner.Api).Network.SendBlockEntityPacket(owner.Pos, toggleFuzzyPacketId);
            dialog?.OnFilterStateUpdated();
        }

        private void OnClientToggleExtra()
        {
            if (CanToggleExtra != null && !CanToggleExtra()) return;
            ToggleExtraClient?.Invoke();
            ((ICoreClientAPI)owner.Api).Network.SendBlockEntityPacket(owner.Pos, ExtraTogglePacketId);
            dialog?.OnFilterStateUpdated();
        }

        private void OnDialogClosed()
        {
            dialog = null;
        }

        public void WriteToTree(ITreeAttribute tree)
        {
            inventory?.ToTreeAttributes(tree);
            tree.SetBool("whitelist", Whitelist);
            tree.SetBool("fuzzy", Fuzzy);
            WriteExtraState?.Invoke(tree);
        }

        public void ReadFromTree(ITreeAttribute tree)
        {
            suppressClientSync = true;
            try
            {
                inventory.FromTreeAttributes(tree);
            }
            finally
            {
                suppressClientSync = false;
            }

            Whitelist = tree.GetBool("whitelist", false);
            Fuzzy = tree.GetBool("fuzzy", false);
            ReadExtraState?.Invoke(tree);

            if (owner.Api != null) inventory.ResolveBlocksOrItems();
            dialog?.OnFilterStateUpdated();
        }

        public void DisposeDialog()
        {
            GuiDialogUtil.SafeDispose(ref dialog);
        }
    }
}
