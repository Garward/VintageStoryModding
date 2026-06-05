using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using VintageKinematics.Api;

namespace VintageKinematics.BlockEntities
{
    public enum FunnelPulseMode
    {
        Continuous = 0,
        One = 1,
        HalfStack = 2,
        FullStack = 3
    }

    /// <summary>
    /// Directional belt/storage output adapter. Belts can hand item stacks to this block entity,
    /// and inventories directly above can be drained into the configured output face.
    /// A single ghost filter slot (whitelist/blacklist toggle) gates every transfer.
    /// </summary>
    public class BEFunnel : BlockEntity, IKineticActivatable
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
        public const int PacketIdTogglePulseMode = 5304;

        public BlockFacing OutputFacing => BlockFunnelFacing(Block?.Variant?["facing"]);

        private readonly FilterDialogController filter;
        private FunnelPulseMode pulseMode = FunnelPulseMode.Continuous;

        public bool Whitelist => filter.Whitelist;
        public bool Fuzzy => filter.Fuzzy;
        public bool IsIron => Block?.Variant?["tier"] == "iron";
        public int ActiveFilterSlotCount => IsIron ? 5 : 1;
        public int PullQuantity => IsIron ? 16 : 1;
        public FunnelPulseMode PulseMode => IsIron ? pulseMode : FunnelPulseMode.Continuous;

        public BEFunnel()
        {
            filter = new FilterDialogController(
                this,
                new InventoryFunnelFilter(MaxFilterSlots, "funnelfilter", null),
                "funnelfilter",
                "vintagekinematics:funnel-filter-title",
                "Funnel Filter",
                "funnel",
                PacketIdOpenDialog,
                PacketIdToggleMode,
                PacketIdSetFilter,
                PacketIdToggleFuzzy,
                () => ActiveFilterSlotCount,
                centerSingleSlot: () => ActiveFilterSlotCount == 1);
            filter.ExtraTogglePacketId = PacketIdTogglePulseMode;
            filter.CanToggleExtra = () => IsIron;
            filter.ToggleExtraServer = CyclePulseMode;
            filter.ToggleExtraClient = CyclePulseMode;
            filter.ExtraToggleLabel = GetPulseModeLabel;
            filter.WriteExtraState = tree => tree.SetInt("pulseMode", (int)PulseMode);
            filter.ReadExtraState = tree => pulseMode = (FunnelPulseMode)tree.GetInt("pulseMode", (int)FunnelPulseMode.Continuous);
        }

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            filter.Initialize(api);

            if (api.Side == EnumAppSide.Server)
            {
                RegisterGameTickListener(OnServerTick, AutoPullTickMs);
            }
        }

        public bool TryAcceptFromBelt(ItemStack stack) => TryAcceptFromPush(stack);

        public bool TryAcceptFromPush(ItemStack stack)
        {
            if (Api?.Side != EnumAppSide.Server) return false;
            if (stack == null || stack.StackSize <= 0) return true;
            if (!MatchesFilter(stack)) return false;

            return TryOutputStack(stack);
        }

        // All-empty filter means default behavior. Once a filter exists, whitelist/blacklist
        // and fuzzy matching decide whether the stack can pass.
        public bool MatchesFilter(ItemStack stack) => filter.Matches(stack);

        private void OnServerTick(float dt)
        {
            if (Api?.Side != EnumAppSide.Server) return;

            BlockFacing output = OutputFacing;
            if (output == BlockFacing.UP) return;
            if (PulseMode != FunnelPulseMode.Continuous) return;

            if (TryPullFromSource(Pos.AddCopy(BlockFacing.UP), BlockFacing.DOWN, PullQuantity)) return;

            BlockPos sideSourcePos = Pos.AddCopy(output.Opposite);
            if (Api.World.BlockAccessor.GetBlockEntity(sideSourcePos) is BlockEntityBarrel barrel && !barrel.Sealed)
            {
                TryPullFromSource(sideSourcePos, output, PullQuantity);
            }
        }

        public bool OnKineticActivate(IWorldAccessor world, BlockPos targetPos, BlockFacing activatedFace, BlockPos activatorPos, float signedRPM)
        {
            if (Api?.Side != EnumAppSide.Server) return false;
            if (!IsIron || PulseMode == FunnelPulseMode.Continuous) return false;

            BlockFacing output = OutputFacing;
            if (output == BlockFacing.UP) return false;

            if (TryPulseFromSource(Pos.AddCopy(BlockFacing.UP), BlockFacing.DOWN)) return true;

            BlockPos sideSourcePos = Pos.AddCopy(output.Opposite);
            if (Api.World.BlockAccessor.GetBlockEntity(sideSourcePos) is BlockEntityBarrel barrel && !barrel.Sealed)
            {
                return TryPulseFromSource(sideSourcePos, output);
            }

            return false;
        }

        private bool TryPulseFromSource(BlockPos sourcePos, BlockFacing sourceFace)
        {
            BlockEntity sourceBe = MultiblockHelper.GetMultiblockAwareBE(Api.World, sourcePos);
            if (sourceBe is not IBlockEntityContainer container) return false;

            IInventory inventory = container.Inventory;
            if (inventory == null || inventory.TakeLocked) return false;

            ItemSlot sourceSlot = GetPullSlot(inventory, sourceBe, sourcePos, sourceFace);
            if (sourceSlot == null || sourceSlot.Empty || !sourceSlot.CanTake()) return false;

            return TryPullFromSlot(sourceSlot, sourceBe, ActivationPullQuantity(sourceSlot.Itemstack));
        }

        private bool TryPullFromSource(BlockPos sourcePos, BlockFacing sourceFace, int maxQuantity)
        {
            BlockEntity sourceBe = MultiblockHelper.GetMultiblockAwareBE(Api.World, sourcePos);
            if (sourceBe is not IBlockEntityContainer container) return false;

            IInventory inventory = container.Inventory;
            if (inventory == null || inventory.TakeLocked) return false;

            ItemSlot sourceSlot = GetPullSlot(inventory, sourceBe, sourcePos, sourceFace);
            if (sourceSlot == null || sourceSlot.Empty || !sourceSlot.CanTake()) return false;

            return TryPullFromSlot(sourceSlot, sourceBe, maxQuantity);
        }

        private bool TryPullFromSlot(ItemSlot sourceSlot, BlockEntity sourceBe, int maxQuantity)
        {
            if (sourceSlot == null || sourceSlot.Empty || !sourceSlot.CanTake()) return false;
            maxQuantity = Math.Max(1, maxQuantity);

            ItemStack moving = sourceSlot.Itemstack.Clone();
            moving.StackSize = Math.Min(moving.StackSize, maxQuantity);
            int startSize = moving.StackSize;

            TryOutputStack(moving);

            int remaining = moving?.StackSize ?? 0;
            int moved = startSize - remaining;
            if (moved <= 0) return false;

            sourceSlot.TakeOut(moved);
            sourceSlot.MarkDirty();
            sourceBe.MarkDirty(true);
            MarkDirty(false);
            return true;
        }

        private int ActivationPullQuantity(ItemStack stack)
        {
            if (stack == null) return 0;
            return PulseMode switch
            {
                FunnelPulseMode.One => 1,
                FunnelPulseMode.HalfStack => Math.Max(1, (stack.StackSize + 1) / 2),
                FunnelPulseMode.FullStack => stack.StackSize,
                _ => PullQuantity
            };
        }

        private ItemSlot GetPullSlot(IInventory inventory, BlockEntity sourceBe, BlockPos sourceCell, BlockFacing sourceFace)
        {
            // Cell-aware VK source: only pull from explicitly declared outputs on the face
            // touching the funnel. For the normal above-source case this is DOWN; side barrel
            // support passes the horizontal face that points toward the funnel.
            // The mapped slot is the contract: if its contents fail the filter, we don't scan
            // around it (that would let the funnel reach into input slots).
            if (sourceBe is IFaceMappedContainer faceMapped)
            {
                ItemSlot mapped = faceMapped.IOFaces.GetPullSlot(inventory, sourceCell, sourceFace);
                if (mapped == null || mapped.Empty || !mapped.CanTake()) return null;
                return MatchesFilter(mapped.Itemstack) ? mapped : null;
            }

            if (sourceBe is BlockEntityBarrel barrel)
            {
                if (barrel.Sealed) return null;
                ItemSlot itemSlot = inventory[0];
                if (itemSlot == null || itemSlot.Empty || !itemSlot.CanTake()) return null;
                return MatchesFilter(itemSlot.Itemstack) ? itemSlot : null;
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

            if (targetBe is BlockEntityBarrel barrel)
            {
                return TryOutputToBarrel(stack, barrel);
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

        private bool TryOutputToBarrel(ItemStack stack, BlockEntityBarrel barrel)
        {
            if (stack == null || stack.StackSize <= 0) return true;
            if (barrel == null || barrel.Sealed) return false;

            IInventory inventory = barrel.Inventory;
            if (inventory == null || inventory.PutLocked || inventory.Count == 0) return false;

            DummySlot source = new DummySlot(stack);
            int startSize = stack.StackSize;
            int moved = BarrelAutomation.TryPushItemIntoBarrel(Api.World, barrel, source, stack.StackSize);

            if (moved <= 0) return false;

            int remaining = source.Empty ? 0 : source.Itemstack.StackSize;
            stack.StackSize = remaining;
            MarkDirty(false);
            return startSize > stack.StackSize;
        }

        public bool OnPlayerRightClick(IPlayer byPlayer, BlockSelection blockSel)
        {
            return filter.Open(byPlayer);
        }

        public override void OnReceivedClientPacket(IPlayer player, int packetid, byte[] data)
        {
            if (filter.OnReceivedClientPacket(player, packetid, data)) return;
            base.OnReceivedClientPacket(player, packetid, data);
        }

        public override void OnReceivedServerPacket(int packetid, byte[] data)
        {
            if (filter.OnReceivedServerPacket(packetid, data)) return;
            base.OnReceivedServerPacket(packetid, data);
        }

        private void CyclePulseMode()
        {
            pulseMode = PulseMode switch
            {
                FunnelPulseMode.Continuous => FunnelPulseMode.One,
                FunnelPulseMode.One => FunnelPulseMode.HalfStack,
                FunnelPulseMode.HalfStack => FunnelPulseMode.FullStack,
                _ => FunnelPulseMode.Continuous
            };
        }

        private string GetPulseModeLabel()
        {
            return PulseMode switch
            {
                FunnelPulseMode.One => Lang.Get("vintagekinematics:funnel-pulse-one"),
                FunnelPulseMode.HalfStack => Lang.Get("vintagekinematics:funnel-pulse-half"),
                FunnelPulseMode.FullStack => Lang.Get("vintagekinematics:funnel-pulse-full"),
                _ => Lang.Get("vintagekinematics:funnel-pulse-continuous")
            };
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            filter.WriteToTree(tree);
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
        {
            base.FromTreeAttributes(tree, worldForResolving);
            filter.ReadFromTree(tree);
        }

        public override void OnBlockUnloaded()
        {
            base.OnBlockUnloaded();
            filter.DisposeDialog();
        }

        public override void OnBlockRemoved()
        {
            base.OnBlockRemoved();
            filter.DisposeDialog();
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
