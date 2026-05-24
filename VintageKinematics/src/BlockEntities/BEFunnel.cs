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

        public bool TryAcceptFromBelt(ItemStack stack) => TryAcceptFromPush(stack);

        public bool TryAcceptFromPush(ItemStack stack)
        {
            if (Api?.Side != EnumAppSide.Server) return false;
            if (stack == null || stack.StackSize <= 0) return true;
            if (!MatchesFilter(stack)) return false;

            return TryOutputStack(stack);
        }

        // All-empty filter: whitelist blocks everything, blacklist allows everything.
        // Otherwise the stack matches if any active filter slot matches it.
        public bool MatchesFilter(ItemStack stack) => ItemFilterMatcher.Matches(stack, filterInv, ActiveFilterSlotCount, whitelist, fuzzy);

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

            ItemSlot sourceSlot = GetPullSlot(inventory, sourceBe, sourcePos);
            if (sourceSlot == null || sourceSlot.Empty || !sourceSlot.CanTake()) return;

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

        private ItemSlot GetPullSlot(IInventory inventory, BlockEntity sourceBe, BlockPos sourceCell)
        {
            // Cell-aware VK source: only pull from explicitly declared (cell, DOWN) outputs.
            // The face on the source touching the funnel is DOWN regardless of multiblock layout.
            // The mapped slot is the contract: if its contents fail the filter, we don't scan
            // around it (that would let the funnel reach into input slots).
            if (sourceBe is IFaceMappedContainer faceMapped)
            {
                ItemSlot mapped = faceMapped.IOFaces.GetPullSlot(inventory, sourceCell, BlockFacing.DOWN);
                if (mapped == null || mapped.Empty || !mapped.CanTake()) return null;
                return MatchesFilter(mapped.Itemstack) ? mapped : null;
            }

            // Vanilla quern's inventory has slot 0 = input (grain) and slot 1 = output (flour);
            // it overrides GetAutoPushIntoSlot to return slot 0, but doesn't expose a face-only
            // pull at all. The scanning fallback below would happily grab slot 0 and unfeed the
            // quern, so route quern pulls to the output slot explicitly.
            if (inventory is InventoryQuern quern)
            {
                ItemSlot outSlot = quern[1];
                if (outSlot == null || outSlot.Empty || !outSlot.CanTake()) return null;
                return MatchesFilter(outSlot.Itemstack) ? outSlot : null;
            }

            InventoryBase invBase = inventory as InventoryBase;

            // Plain storage (vanilla chest, barrel, vessel, basket, shelf, etc.) all use
            // InventoryGeneric. The vanilla chest's OnGetAutoPullFromSlot just returns the
            // first non-empty slot with no face restriction, so honouring that as the only
            // pull candidate strands everything behind a filtered-out slot 0. Scan the
            // whole inventory for the first filter-matching slot instead.
            if (invBase is InventoryGeneric)
            {
                for (int i = 0; i < inventory.Count; i++)
                {
                    ItemSlot s = inventory[i];
                    if (s == null || s.Empty || !s.CanTake()) continue;
                    if (!MatchesFilter(s.Itemstack)) continue;
                    return s;
                }
                return null;
            }

            // Machine-specific InventoryBase subclass (or non-InventoryBase inventory): honour
            // the delegate's choice strictly so we don't reach into input slots that were
            // deliberately walled off from auto-pull. Filter is still applied on top.
            ItemSlot slot = invBase?.GetAutoPullFromSlot(BlockFacing.DOWN);
            if (slot == null || slot.Empty || !slot.CanTake()) return null;
            return MatchesFilter(slot.Itemstack) ? slot : null;
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

            if (targetBe is IAutomationItemSink sink)
            {
                return sink.TryAcceptFromFunnel(stack);
            }

            if (targetBe is not IBlockEntityContainer) return false;

            DummySlot probe = new DummySlot(stack);
            int moved = InventoryPusher.TryPush(Api.World, Pos, output, probe);
            return moved > 0 && (stack.StackSize <= 0);
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

        private void DisposeDialog() => GuiDialogUtil.SafeDispose(ref invDialog);

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
