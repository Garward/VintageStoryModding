using System;
using System.IO;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using VintageKinematics.Api;
using VintageKinematics.Api.Storage;
using VintageKinematics.Storage.Operations;

namespace VintageKinematics.BlockEntities.Storage
{
    /// <summary>Filtered belt adapter. It never owns warehouse items or bypasses the terminal.</summary>
    public sealed partial class BEKineticWarehousePort : IDirectionalAutomationItemSink, IKineticConsumer
    {
        private const int FilterSlots = 5;

        public const int PacketIdOpenFilter = 6750;
        public const int PacketIdToggleFilterMode = 6751;
        public const int PacketIdSetFilter = 6752;
        public const int PacketIdToggleFuzzy = 6753;

        private readonly FilterDialogController filter;
        private long lastExportEntryId;
        private float driveRPM;

        internal bool FilterWhitelist => filter.Whitelist;
        internal bool FilterFuzzy => filter.Fuzzy;
        internal int FilterSlotCount => FilterSlots;
        internal int OutputIntervalMs => StoragePortTransferPolicy.NormalizeOutputIntervalMs(
            Block?.Attributes?["vkStorageOutputIntervalMs"]
                .AsInt(StoragePortTransferPolicy.DefaultOutputIntervalMs)
            ?? StoragePortTransferPolicy.DefaultOutputIntervalMs);
        internal float MaximumOutputItemsPerSecond =>
            StoragePortTransferPolicy.MaximumItemsPerSecond(OutputIntervalMs, TransferRate);
        internal int ConfiguredFilterCount
        {
            get
            {
                int count = 0;
                for (int slotId = 0; slotId < filter.ActiveSlotCount; slotId++)
                {
                    if (!filter.Inventory[slotId].Empty) count++;
                }
                return count;
            }
        }

        internal bool IsDrivePowered => PortRole == StoragePortRole.ControllerAccess
            && float.IsFinite(driveRPM)
            && MathF.Abs(driveRPM) >= StoragePowerPolicy.NormalizeMinimumRPM(
                Api?.ModLoader.GetModSystem<KineticConfigSystem>()?.Config?.StorageMinimumRPM ?? 16f);
        internal float CurrentDriveStressImpact =>
            GetBehavior<BEBehaviorKinetic>()?.StressImpact ?? 0f;

        public BEKineticWarehousePort()
        {
            filter = new FilterDialogController(
                this,
                new InventoryFunnelFilter(FilterSlots, "warehouseportfilter", null),
                "warehouseportfilter",
                "vintagekinematics:warehouse-port-filter-title",
                "Warehouse Port Filter",
                "warehouse port",
                PacketIdOpenFilter,
                PacketIdToggleFilterMode,
                PacketIdSetFilter,
                PacketIdToggleFuzzy,
                () => FilterSlots);
        }

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            filter.Initialize(api);
            if (api.Side == EnumAppSide.Server && PortRole == StoragePortRole.Export)
            {
                RegisterGameTickListener(OnOutputTick, OutputIntervalMs);
            }
            if (api.Side == EnumAppSide.Server && PortRole == StoragePortRole.ControllerAccess)
            {
                RegisterDelayedCallback(_ => NotifyControllerPower(), 100);
            }
        }

        public void OnNetworkRPMChanged(float newRPM, IKineticNetworkInfo network)
        {
            driveRPM = network == null || network.IsConflicted || network.IsOverstressed
                ? 0f
                : newRPM;
            NotifyControllerPower();
        }

        public bool OnPlayerRightClick(IPlayer player)
        {
            if (PortRole == StoragePortRole.ControllerAccess) return false;
            return filter.Open(player);
        }

        public bool TryAcceptFromBelt(ItemStack stack)
        {
            // Belts in current VK builds use the source-aware overload. Fail closed if an
            // older caller cannot prove it is feeding the visible input face.
            return false;
        }

        public bool TryAcceptFromBelt(ItemStack stack, BlockPos beltPosition)
        {
            if (beltPosition == null || !beltPosition.Equals(Pos.AddCopy(Facing))) return false;
            return TryImport(stack);
        }

        public bool TryAcceptFromFunnel(ItemStack stack) => false;

        public bool TryAcceptFromFunnel(ItemStack stack, BlockPos funnelPosition)
        {
            if (funnelPosition == null || !funnelPosition.Equals(Pos.AddCopy(Facing))) return false;
            return TryImport(stack);
        }

        private bool TryImport(ItemStack stack)
        {
            if (Api?.Side != EnumAppSide.Server
                || PortRole != StoragePortRole.Import
                || stack?.Collectible == null
                || stack.StackSize <= 0
                || !filter.Matches(stack))
            {
                return false;
            }

            BEKineticWarehouseTerminal controller = ResolveController();
            if (controller == null) return false;
            StorageTransferResult result = controller.TryAutomationInsert(stack, TransferRate);
            MarkDirty(false);
            return result.Success && stack.StackSize <= 0;
        }

        private void OnOutputTick(float dt)
        {
            if (Api?.Side != EnumAppSide.Server || PortRole != StoragePortRole.Export) return;
            if (!TryResolveOutputBelt(out BEBelt belt, out float progress)) return;
            if (!belt.CanInsertItem(progress)) return;

            BEKineticWarehouseTerminal controller = ResolveController();
            if (controller == null) return;
            StorageTransferResult result = controller.TryAutomationExtract(
                filter.Matches,
                TransferRate,
                lastExportEntryId,
                out ItemStack extracted,
                out long selectedEntryId);
            if (!result.Success || extracted == null) return;

            if (belt.TryInsertItem(extracted, progress))
            {
                lastExportEntryId = selectedEntryId;
                MarkDirty(false);
                return;
            }
            if (!controller.RestoreAutomationExtraction(extracted))
            {
                Api.World.Logger.Error(
                    "[VintageKinematics] Warehouse output at {0} could not restore a failed belt handoff.",
                    Pos);
            }
        }

        private bool TryResolveOutputBelt(out BEBelt controller, out float progress)
        {
            controller = null;
            progress = 0f;
            BlockPos target = Pos.AddCopy(Facing);
            if (Api.World.BlockAccessor.GetBlockEntity(target) is not BEBelt belt) return false;
            controller = belt.IsController
                ? belt
                : Api.World.BlockAccessor.GetBlockEntity(belt.ControllerPos) as BEBelt;
            if (controller == null || controller.ChainLength <= 0) return false;

            var targetCenter = new Vec3d(target.X + 0.5, target.Y + BEBelt.BeltTopY, target.Z + 0.5);
            progress = controller.ProjectOntoChain(targetCenter);
            progress = Math.Clamp(progress, 0.05f, controller.ChainLength - 0.05f);
            return true;
        }

        private BEKineticWarehouseTerminal ResolveController()
        {
            if (ControllerPos == null || string.IsNullOrWhiteSpace(WarehouseId)) return null;
            return Api.World.BlockAccessor.GetBlockEntity(ControllerPos)
                is BEKineticWarehouseTerminal controller
                && controller.WarehouseId == WarehouseId
                    ? controller
                    : null;
        }

        private void NotifyControllerPower()
        {
            if (Api?.Side != EnumAppSide.Server) return;
            ResolveController()?.UpdateDrivePortPower(this, IsDrivePowered);
        }

        internal void UpdateStructureStress(int capacityCellCount)
        {
            if (Api?.Side != EnumAppSide.Server
                || PortRole != StoragePortRole.ControllerAccess) return;

            VintageKinematicsConfig config =
                Api.ModLoader.GetModSystem<KineticConfigSystem>()?.Config
                ?? new VintageKinematicsConfig();
            float impact = StoragePowerPolicy.CalculateStressImpact(
                config.StorageRequiresKineticPower,
                config.StorageBaseStressImpact,
                config.StorageStressImpactPerCell,
                capacityCellCount,
                config.ResolveConsumerStress(Block?.Code?.FirstCodePart()));
            Api.ModLoader.GetModSystem<Network.KineticNetworkManager>()
                ?.TryUpdateConsumerStressImpact(Pos, impact);
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

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            filter.WriteToTree(tree);
            tree.SetLong("lastExportEntryId", lastExportEntryId);
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
        {
            base.FromTreeAttributes(tree, worldForResolving);
            filter.ReadFromTree(tree);
            lastExportEntryId = Math.Max(0, tree.GetLong("lastExportEntryId"));
        }

        public override void OnBlockUnloaded()
        {
            driveRPM = 0f;
            NotifyControllerPower();
            filter.DisposeDialog();
            base.OnBlockUnloaded();
        }

        public override void OnBlockRemoved()
        {
            driveRPM = 0f;
            NotifyControllerPower();
            filter.DisposeDialog();
            base.OnBlockRemoved();
        }
    }
}
