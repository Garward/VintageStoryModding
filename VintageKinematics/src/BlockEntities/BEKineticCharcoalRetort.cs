using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using VintageKinematics.Api;
using VintageKinematics.Network;
using VintageKinematics.Rendering;

namespace VintageKinematics.BlockEntities
{
    public class BEKineticCharcoalRetort : BEKineticItemProcessorBase<ItemStack>, IKineticWorkRateModifier
    {
        public const int SlotInputFirst = 0;
        public const int SlotInputLast = 63;
        public const int SlotOutputFirst = 64;
        public const int SlotOutputLast = 127;
        public const int InventorySize = 128;

        private const float RetortSmokeIntervalMs = 1000f;
        private const int MaxBellowsAssistCount = 2;
        private const float BellowsWorkRateBonusPerUnit = 0.5f;
        private static readonly Vec3d SmokeStackLocalPosition = new Vec3d(14.2f / 16f, 32.8f / 16f, -6.2f / 16f);

        public BEKineticCharcoalRetort()
            : base("kineticcharcoalretort", InventorySize, SlotInputFirst, SlotInputLast, SlotOutputFirst, SlotOutputLast, CreateRetortSlot)
        {
        }

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            NormalizeStoredStacks();

            if (api.Side == EnumAppSide.Server)
            {
                RegisterGameTickListener(OnSmokeTick, (int)RetortSmokeIntervalMs);
            }
        }

        private static ItemSlot CreateRetortSlot(int slotId, InventoryBase inventory)
        {
            return slotId <= SlotInputLast
                ? new ItemSlotFirewoodInput(inventory)
                : new ItemSlotCrusherOutput(inventory);
        }

        protected override IOFaceMap BuildIOFaceMap()
        {
            IOFaceMap jsonMap = BuildJsonIOFaceMap();
            if (jsonMap != null) return jsonMap;

            BlockFacing facing = MultiblockHelper.PlacementFacingFromVariant(Block);
            BlockFacing inputFace = MultiblockHelper.LeftOf(facing);
            BlockFacing outputFace = MultiblockHelper.RightOf(facing);

            IOFaceMap map = new IOFaceMap(Pos);
            foreach (BlockPos cell in MultiblockHelper.CellsOnFace(Block, Pos, BlockFacing.UP))
            {
                for (int i = SlotInputFirst; i <= SlotInputLast; i++)
                {
                    map.MapInput(cell, BlockFacing.UP, i);
                }
            }
            foreach (BlockPos cell in MultiblockHelper.CellsOnFace(Block, Pos, inputFace))
            {
                for (int i = SlotInputFirst; i <= SlotInputLast; i++)
                {
                    map.MapInput(cell, inputFace, i);
                }
            }
            foreach (BlockPos cell in MultiblockHelper.CellsOnFace(Block, Pos, outputFace))
            {
                for (int i = SlotOutputFirst; i <= SlotOutputLast; i++)
                {
                    map.MapOutput(cell, outputFace, i);
                }
            }
            foreach (BlockPos cell in MultiblockHelper.CellsOnFace(Block, Pos, BlockFacing.DOWN))
            {
                for (int i = SlotOutputFirst; i <= SlotOutputLast; i++)
                {
                    map.MapOutput(cell, BlockFacing.DOWN, i);
                }
            }
            return map;
        }

        protected override ItemStack FindRecipe(ItemStack input)
        {
            return null;
        }

        protected override System.Collections.Generic.IEnumerable<ItemStack> GetOutputs(ItemStack recipe)
        {
            yield break;
        }

        protected override GuiDialogBlockEntity CreateClientDialog(string title, ICoreClientAPI capi)
        {
            return null;
        }

        protected override void OnInventorySlotModified(int slotId)
        {
            NormalizeStoredStacks();
            base.OnInventorySlotModified(slotId);
        }

        protected override void ReadState(ITreeAttribute tree)
        {
            NormalizeStoredStacks();
        }

        protected override void OnWorkCycle(KineticWorkCompletedArgs args)
        {
            ItemSlot input = FindInputSlot();
            if (input == null) return;

            ItemStack output = CharcoalStack();
            if (output == null) return;
            if (!CanDepositOutput(output)) return;

            input.TakeOut(1);
            input.MarkDirty();
            DepositOutput(output);
            MarkDirty(true);
        }

        private void OnSmokeTick(float dt)
        {
            if (!IsPoweredForWork()) return;
            if (FindInputSlot() == null) return;

            Vec3d at = LocalShapePointToWorld(SmokeStackLocalPosition);
            var particles = new SimpleParticleProperties(
                minQuantity: 1,
                maxQuantity: 2,
                color: ColorUtil.ToRgba(120, 70, 70, 70),
                minPos: at.AddCopy(-0.05, 0.0, -0.05),
                maxPos: at.AddCopy(0.05, 0.02, 0.05),
                minVelocity: new Vec3f(-0.012f, 0.04f, -0.012f),
                maxVelocity: new Vec3f(0.012f, 0.075f, 0.012f),
                lifeLength: 2.5f,
                gravityEffect: -0.03f,
                minSize: 0.16f,
                maxSize: 0.32f,
                model: EnumParticleModel.Quad
            );
            Api.World.SpawnParticles(particles);
        }

        private bool IsPoweredForWork()
        {
            BEBehaviorKinetic kinetic = GetBehavior<BEBehaviorKinetic>();
            BEBehaviorKineticWorker worker = GetBehavior<BEBehaviorKineticWorker>();
            float minRpm = Math.Max(0.01f, worker?.MinRPM ?? 0.01f);
            return kinetic != null
                && !kinetic.IsConflicted
                && !(kinetic.Network?.IsOverstressed ?? false)
                && MathF.Abs(kinetic.ActualRPM) >= minRpm;
        }

        public float ModifyKineticWorkRPM(float rpm, float minRPM)
        {
            if (MathF.Abs(rpm) < minRPM) return rpm;

            int bellowsCount = PoweredBellowsCount();
            if (bellowsCount <= 0) return rpm;

            float multiplier = 1f + bellowsCount * BellowsWorkRateBonusPerUnit;
            return rpm * multiplier;
        }

        private ItemSlot FindInputSlot()
        {
            for (int i = SlotInputFirst; i <= SlotInputLast; i++)
            {
                ItemSlot slot = MachineInventory[i];
                if (slot.Empty) continue;
                if (IsFirewood(slot.Itemstack)) return slot;
            }
            return null;
        }

        private bool CanDepositOutput(ItemStack stack)
        {
            for (int i = SlotOutputFirst; i <= SlotOutputLast; i++)
            {
                ItemSlot slot = MachineInventory[i];
                if (slot.Empty) return true;
                if (slot.Itemstack?.Collectible == stack.Collectible
                    && slot.Itemstack.StackSize < slot.Itemstack.Collectible.MaxStackSize)
                {
                    return true;
                }
            }
            return false;
        }

        protected override void DepositOutput(ItemStack stack)
        {
            Vec3d at = new Vec3d(Pos.X + 0.5, Pos.Y + 0.1, Pos.Z + 0.5);
            MachineOutputHelper.DepositOrPush(this, MachineInventory, SlotOutputFirst, SlotOutputLast, stack, MachineIOFaces?.OutputEntries, OutputPushBatch, at);
        }

        private ItemStack CharcoalStack()
        {
            Item item = Api.World.GetItem(new AssetLocation("game", "charcoal"));
            return item == null ? null : new ItemStack(item, 1);
        }

        private static bool IsFirewood(ItemStack stack)
        {
            AssetLocation code = stack?.Collectible?.Code;
            return code != null && code.Domain == "game" && code.Path == "firewood";
        }

        public override bool OnPlayerRightClick(IPlayer byPlayer, BlockSelection blockSel)
        {
            return HandleCrateStyleRightClick(byPlayer, crateInput: true, crateOutput: true, inputFilter: IsFirewood);
        }

        public override void GetBlockInfo(IPlayer forPlayer, System.Text.StringBuilder dsc)
        {
            base.GetBlockInfo(forPlayer, dsc);

            int firewood = InventoryRangeInteractionHelper.CountItems(MachineInventory, SlotInputFirst, SlotInputLast);
            int charcoal = InventoryRangeInteractionHelper.CountItems(MachineInventory, SlotOutputFirst, SlotOutputLast);
            int bellowsCount = PoweredBellowsCount();
            dsc.AppendLine(Lang.Get(
                "vintagekinematics:kineticcharcoalretort-firewood",
                firewood,
                InventoryRangeInteractionHelper.CapacityItems(FirewoodStack(), SlotInputLast - SlotInputFirst + 1)));
            dsc.AppendLine(Lang.Get(
                "vintagekinematics:kineticcharcoalretort-charcoal",
                charcoal,
                InventoryRangeInteractionHelper.CapacityItems(CharcoalStack(), SlotOutputLast - SlotOutputFirst + 1)));
            dsc.AppendLine($"Bellows assist: {bellowsCount}/{MaxBellowsAssistCount} ({1f + bellowsCount * BellowsWorkRateBonusPerUnit:0.##}x)");
        }

        private void NormalizeStoredStacks()
        {
            if (MachineInventory == null) return;

            for (int i = SlotInputFirst; i <= SlotInputLast; i++)
            {
                ItemSlot slot = MachineInventory[i];
                if (slot.Empty) continue;
                if (IsFirewood(slot.Itemstack)) continue;
                if (IsCharcoal(slot.Itemstack))
                {
                    MoveSlotContentsToRange(slot, SlotOutputFirst, SlotOutputLast);
                    continue;
                }
                slot.Itemstack = null;
            }

            for (int i = SlotOutputFirst; i <= SlotOutputLast; i++)
            {
                ItemSlot slot = MachineInventory[i];
                if (slot.Empty) continue;
                if (IsCharcoal(slot.Itemstack)) continue;
                if (IsFirewood(slot.Itemstack))
                {
                    MoveSlotContentsToRange(slot, SlotInputFirst, SlotInputLast);
                    continue;
                }
                slot.Itemstack = null;
            }
        }

        private void MoveSlotContentsToRange(ItemSlot source, int firstSlot, int lastSlot)
        {
            if (source == null || source.Empty) return;
            InventoryRangeInteractionHelper.DepositStackDirect(MachineInventory, source.Itemstack, firstSlot, lastSlot, source);

            if (!source.Empty)
            {
                source.Itemstack = null;
                source.MarkDirty();
            }
        }

        private ItemStack FirewoodStack()
        {
            AssetLocation code = new AssetLocation("game", "firewood");
            Item item = Api?.World?.GetItem(code);
            if (item != null) return new ItemStack(item, 1);
            Block block = Api?.World?.GetBlock(code);
            return block == null ? null : new ItemStack(block, 1);
        }

        private static bool IsCharcoal(ItemStack stack)
        {
            AssetLocation code = stack?.Collectible?.Code;
            return code != null && code.Domain == "game" && code.Path == "charcoal";
        }

        private int PoweredBellowsCount()
        {
            if (Api?.World == null) return 0;

            int count = 0;
            foreach (BlockPos pos in BellowsAttachPositions())
            {
                if (IsPoweredBellowsAt(pos) && ++count >= MaxBellowsAssistCount) return count;
            }
            return count;
        }

        private BlockPos[] BellowsAttachPositions()
        {
            if (!MultiblockHelper.TryGetClaim(Block, Pos, out BlockPos baseCorner, out Vec3i size))
            {
                return new[] { Pos.AddCopy(MultiblockHelper.RightOf(MultiblockHelper.PlacementFacingFromVariant(Block))) };
            }

            int rotateY = (int)(Block?.Shape?.rotateY ?? 0f);
            BlockFacing bellowsFace = RotateFacingY(BlockFacing.WEST, rotateY);
            Vec3i[] baseCells =
            {
                new Vec3i(0, 0, 0),
                new Vec3i(0, 0, 2)
            };

            var positions = new System.Collections.Generic.List<BlockPos>();
            foreach (Vec3i baseCell in baseCells)
            {
                Vec3i cell = BEBehaviorKineticMultiblock.RotateCellY(baseCell, rotateY, size.X, size.Z);
                positions.Add(new BlockPos(
                    baseCorner.X + cell.X + bellowsFace.Normali.X,
                    baseCorner.Y + cell.Y + bellowsFace.Normali.Y,
                    baseCorner.Z + cell.Z + bellowsFace.Normali.Z,
                    Pos.dimension));
            }
            return positions.ToArray();
        }

        private bool IsPoweredBellowsAt(BlockPos pos)
        {
            BlockEntity be = Api.World.BlockAccessor.GetBlockEntity(pos);
            if (be == null) return false;

            string path = be.Block?.Code?.Path ?? "";
            if (!path.Contains("bellows")) return false;

            BEBehaviorKinetic kinetic = be.GetBehavior<BEBehaviorKinetic>();
            return kinetic != null && Math.Abs(kinetic.ActualRPM) >= KineticNetwork.MinAbsRPM;
        }

        private Vec3d LocalShapePointToWorld(Vec3d local)
        {
            double x = local.X;
            double z = local.Z;
            int rotateY = (int)(Block?.Shape?.rotateY ?? 0f);
            int steps = (((rotateY / 90) % 4) + 4) % 4;
            for (int i = 0; i < steps; i++)
            {
                double dx = x - 0.5;
                double dz = z - 0.5;
                x = 0.5 + dz;
                z = 0.5 - dx;
            }
            return new Vec3d(Pos.X + x, Pos.Y + local.Y, Pos.Z + z);
        }

        private static BlockFacing RotateFacingY(BlockFacing facing, int rotateY)
        {
            int turns = (((rotateY / 90) % 4) + 4) % 4;
            BlockFacing result = facing;
            for (int i = 0; i < turns; i++)
            {
                if (result == BlockFacing.NORTH) result = BlockFacing.WEST;
                else if (result == BlockFacing.WEST) result = BlockFacing.SOUTH;
                else if (result == BlockFacing.SOUTH) result = BlockFacing.EAST;
                else if (result == BlockFacing.EAST) result = BlockFacing.NORTH;
            }
            return result;
        }
    }
}
