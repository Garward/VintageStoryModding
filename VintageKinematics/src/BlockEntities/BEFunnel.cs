using System;
using System.Collections.Generic;
using System.IO;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using VintageKinematics.Api;
using VintageKinematics.Gui;

namespace VintageKinematics.BlockEntities
{
    /// <summary>
    /// Directional belt/storage output adapter. Belts can hand item stacks to this block entity,
    /// and inventories directly above can be drained into the configured output face.
    /// A single ghost filter slot (whitelist/blacklist toggle) gates every transfer.
    /// </summary>
    public class BEFunnel : BlockEntity
    {
        // 50 ms matches belt tick cadence so funnels keep up with max-RPM producers.
        // The tick early-exits cheaply when nothing is available to pull.
        private const int AutoPullTickMs = 50;
        // 5 always allocated so save/load is tier-stable; copper only uses slot 0.
        private const int MaxFilterSlots = 5;

        public const int PacketIdOpenDialog = 5300;
        public const int PacketIdToggleMode = 5301;
        public const int PacketIdSetFilter = 5302;
        public const int PacketIdToggleFuzzy = 5303;

        public BlockFacing OutputFacing => BlockFunnelFacing(Block?.Variant?["facing"]);

        private InventoryFunnelFilter filterInv;
        private bool whitelist;
        private bool fuzzy;
        private bool suppressClientSync;
        private GuiDialogFunnelFilter invDialog;

        public bool Whitelist => whitelist;
        public bool Fuzzy => fuzzy;
        public bool IsIron => Block?.Variant?["tier"] == "iron";
        public int ActiveFilterSlotCount => IsIron ? 5 : 1;
        public int PullQuantity => IsIron ? 16 : 1;

        public BEFunnel()
        {
            filterInv = new InventoryFunnelFilter(MaxFilterSlots, "funnelfilter", null);
        }

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            filterInv.LateInitialize("funnelfilter-" + Pos, api);
            filterInv.ResolveBlocksOrItems();

            if (api.Side == EnumAppSide.Server)
            {
                RegisterGameTickListener(OnServerTick, AutoPullTickMs);
            }
            else
            {
                // Slot transaction packets unreliably route into our ghost slot, so we
                // forward filter changes directly via a BE packet instead.
                filterInv.SlotModified += OnClientFilterSlotModified;
            }
        }

        private void OnClientFilterSlotModified(int slotId)
        {
            if (Api?.Side != EnumAppSide.Client) return;
            if (suppressClientSync) return;

            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);
            bw.Write(slotId);
            ItemSlot s = filterInv[slotId];
            if (s.Empty)
            {
                bw.Write(false);
            }
            else
            {
                bw.Write(true);
                s.Itemstack.ToBytes(bw);
            }

            ((ICoreClientAPI)Api).Network.SendBlockEntityPacket(Pos, PacketIdSetFilter, ms.ToArray());
        }

        public bool TryAcceptFromBelt(ItemStack stack)
        {
            if (Api?.Side != EnumAppSide.Server) return false;
            if (stack == null || stack.StackSize <= 0) return true;
            if (!MatchesFilter(stack)) return false;

            return TryOutputStack(stack);
        }

        // All-empty filter: whitelist blocks everything, blacklist allows everything.
        // Otherwise the stack matches if any active filter slot matches it (exact or
        // wildcard-by-base); whitelist allows matches, blacklist blocks them.
        public bool MatchesFilter(ItemStack stack)
        {
            if (stack?.Collectible == null) return false;

            int active = ActiveFilterSlotCount;
            bool anyFilled = false;
            bool anyMatch = false;

            for (int i = 0; i < active; i++)
            {
                ItemSlot fs = filterInv[i];
                if (fs.Empty) continue;
                anyFilled = true;

                bool match;
                if (fuzzy)
                {
                    AssetLocation pattern = ToFuzzyPattern(fs.Itemstack.Collectible.Code);
                    match = WildcardUtil.Match(pattern, stack.Collectible.Code);
                }
                else
                {
                    match = fs.Itemstack.Collectible == stack.Collectible;
                }

                if (match) { anyMatch = true; break; }
            }

            if (!anyFilled) return !whitelist;
            return whitelist ? anyMatch : !anyMatch;
        }

        // Fuzzy pattern: keep everything up to the last '-' segment and replace it with '*'.
        // game:nugget-copper → game:nugget-*  ;  game:sand-red → game:sand-*
        // Code with no dash falls back to exact match (no useful pattern to derive).
        private static AssetLocation ToFuzzyPattern(AssetLocation code)
        {
            string path = code.Path;
            int lastDash = path.LastIndexOf('-');
            if (lastDash < 0) return code;
            return new AssetLocation(code.Domain, path.Substring(0, lastDash + 1) + "*");
        }

        private void OnServerTick(float dt)
        {
            if (Api?.Side != EnumAppSide.Server) return;

            BlockFacing output = OutputFacing;
            if (output == BlockFacing.UP) return;

            BlockPos sourcePos = Pos.AddCopy(BlockFacing.UP);
            BlockEntity sourceBe = MultiblockHelper.GetMultiblockAwareBE(Api.World, sourcePos);
            if (sourceBe is not IBlockEntityContainer container) return;

            IInventory inventory = container.Inventory;
            if (inventory == null || inventory.TakeLocked) return;

            ItemSlot sourceSlot = GetPullSlot(inventory);
            if (sourceSlot == null || sourceSlot.Empty || !sourceSlot.CanTake()) return;
            if (!MatchesFilter(sourceSlot.Itemstack)) return;

            ItemStack moving = sourceSlot.Itemstack.Clone();
            moving.StackSize = Math.Min(moving.StackSize, PullQuantity);
            int startSize = moving.StackSize;

            TryOutputStack(moving);

            int remaining = moving?.StackSize ?? 0;
            int moved = startSize - remaining;
            if (moved <= 0) return;

            sourceSlot.TakeOut(moved);
            sourceSlot.MarkDirty();
            sourceBe.MarkDirty(true);
            MarkDirty(false);
        }

        private ItemSlot GetPullSlot(IInventory inventory)
        {
            InventoryBase invBase = inventory as InventoryBase;
            ItemSlot slot = invBase?.GetAutoPullFromSlot(BlockFacing.DOWN);
            if (slot != null && !slot.Empty) return slot;

            // If the inventory has opted in to auto-pull semantics (e.g. our sieve restricting
            // pulls to its output slots), respect the null result. Otherwise fall back to
            // scanning so plain chests / barrels still drain.
            if (invBase is InventoryGeneric ig && ig.OnGetAutoPullFromSlot != null) return null;

            for (int i = 0; i < inventory.Count; i++)
            {
                slot = inventory[i];
                if (slot != null && !slot.Empty && slot.CanTake()) return slot;
            }

            return null;
        }

        private bool TryOutputStack(ItemStack stack)
        {
            if (stack == null || stack.StackSize <= 0) return true;

            BlockFacing output = OutputFacing;
            BlockPos targetPos = Pos.AddCopy(output);
            BlockEntity targetBe = MultiblockHelper.GetMultiblockAwareBE(Api.World, targetPos);

            if (targetBe is BEBelt belt)
            {
                return TryOutputToBelt(stack, belt);
            }

            if (targetBe is not IBlockEntityContainer container) return false;

            return TryOutputToInventory(stack, container, output, targetBe);
        }

        private bool TryOutputToBelt(ItemStack stack, BEBelt belt)
        {
            if (stack == null || stack.StackSize <= 0) return true;

            BEBelt controller = belt.IsController
                ? belt
                : Api.World.BlockAccessor.GetBlockEntity(belt.ControllerPos) as BEBelt;
            if (controller == null || controller.ChainLength <= 0) return false;

            Vec3d targetCenter = new Vec3d(
                belt.Pos.X + 0.5,
                belt.Pos.Y + BEBelt.BeltTopY,
                belt.Pos.Z + 0.5);
            float progress = controller.ProjectOntoChain(targetCenter);
            if (progress < 0.05f) progress = 0.05f;
            if (progress > controller.ChainLength - 0.05f) progress = controller.ChainLength - 0.05f;

            if (!controller.TryInsertItem(stack.Clone(), progress)) return false;

            stack.StackSize = 0;
            return true;
        }

        private bool TryOutputToInventory(ItemStack stack, IBlockEntityContainer container, BlockFacing output, BlockEntity targetBe)
        {
            IInventory inventory = container.Inventory;
            if (inventory == null || inventory.PutLocked) return false;

            DummySlot source = new DummySlot(stack.Clone());
            int startSize = source.Itemstack.StackSize;
            var skip = new List<ItemSlot>();
            bool restrictedPush = inventory is InventoryGeneric pushInv && pushInv.OnGetAutoPushIntoSlot != null;

            while (!source.Empty && source.Itemstack.StackSize > 0)
            {
                ItemSlot targetSlot = (inventory as InventoryBase)?.GetAutoPushIntoSlot(output.Opposite, source);
                int moved = 0;

                if (targetSlot != null)
                {
                    moved = MoveIntoSlot(source, targetSlot);
                    if (moved <= 0) skip.Add(targetSlot);
                }
                else if (restrictedPush)
                {
                    // Inventory explicitly disallowed pushing on this face — don't bypass it.
                    break;
                }

                if (moved <= 0)
                {
                    WeightedSlot weighted = inventory.GetBestSuitedSlot(source, null, skip);
                    targetSlot = weighted?.slot;
                    if (targetSlot == null) break;

                    moved = MoveIntoSlot(source, targetSlot);
                    if (moved <= 0)
                    {
                        skip.Add(targetSlot);
                        if (skip.Count >= inventory.Count) break;
                    }
                }

                if (moved > 0)
                {
                    targetSlot.MarkDirty();
                    targetBe.MarkDirty(true);
                    skip.Clear();
                }
            }

            int remaining = source.Empty ? 0 : source.Itemstack.StackSize;
            int movedTotal = startSize - remaining;
            if (movedTotal <= 0) return false;

            stack.StackSize -= movedTotal;
            if (stack.StackSize < 0) stack.StackSize = 0;
            return stack.StackSize <= 0;
        }

        private int MoveIntoSlot(ItemSlot source, ItemSlot target)
        {
            if (source == null || target == null || source.Empty) return 0;
            ItemStackMoveOperation op = new ItemStackMoveOperation(
                Api.World,
                EnumMouseButton.Left,
                0,
                EnumMergePriority.DirectMerge,
                source.Itemstack.StackSize);
            return source.TryPutInto(target, ref op);
        }

        public bool OnPlayerRightClick(IPlayer byPlayer, BlockSelection blockSel)
        {
            if (Api.World is IServerWorldAccessor)
            {
                string title = Lang.Get("vintagekinematics:funnel-filter-title");
                if (string.IsNullOrEmpty(title) || title == "vintagekinematics:funnel-filter-title") title = "Funnel Filter";

                using var ms = new MemoryStream();
                using var bw = new BinaryWriter(ms);
                bw.Write(title);
                bw.Write(whitelist);
                bw.Write(fuzzy);
                var tree = new TreeAttribute();
                filterInv.ToTreeAttributes(tree);
                tree.ToBytes(bw);
                byte[] data = ms.ToArray();

                ((ICoreServerAPI)Api).Network.SendBlockEntityPacket(
                    (IServerPlayer)byPlayer, Pos, PacketIdOpenDialog, data);
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
                if (slotId < 0 || slotId >= ActiveFilterSlotCount) return;
                bool hasStack = br.ReadBoolean();
                ItemStack stack = null;
                if (hasStack)
                {
                    stack = new ItemStack();
                    stack.FromBytes(br);
                    stack.ResolveBlockOrItem(Api.World);
                    stack.StackSize = 1;
                }
                filterInv[slotId].Itemstack = stack;
                filterInv[slotId].MarkDirty();
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
            Api.World.Logger.Audit("Player {0} sent funnel filter packet at {1} but has no claim access. Rejected.", player.PlayerName, Pos);
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
                    title, filterInv, Pos, ActiveFilterSlotCount,
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

        // Click handlers: flip locally for instant label refresh, then forward to server.
        // The server is authoritative; if we ever drift, the next BE sync corrects us.
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
            filterInv ??= new InventoryFunnelFilter(MaxFilterSlots, "funnelfilter", null);
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

        private void DisposeDialog()
        {
            if (invDialog == null) return;
            if (invDialog.IsOpened()) invDialog.TryClose();
            invDialog.Dispose();
            invDialog = null;
        }

        public static BlockFacing BlockFunnelFacing(string code)
        {
            return code switch
            {
                "north" => BlockFacing.NORTH,
                "east" => BlockFacing.EAST,
                "south" => BlockFacing.SOUTH,
                "west" => BlockFacing.WEST,
                "down" => BlockFacing.DOWN,
                "up" => BlockFacing.UP,
                _ => BlockFacing.DOWN
            };
        }
    }
}
