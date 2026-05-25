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
    public class BEKineticCharcoalRetort : BlockEntityOpenableContainer, IFaceMappedContainer, IKineticWorkRateModifier
    {
        public const int SlotInputFirst = 0;
        public const int SlotInputLast = 63;
        public const int SlotOutputFirst = 64;
        public const int SlotOutputLast = 127;
        public const int InventorySize = 128;

        private const float OutputPushIntervalMs = 250f;
        private const float RetortSmokeIntervalMs = 1000f;
        private const int OutputPushBatch = 8;
        private const int MaxBellowsAssistCount = 2;
        private const float BellowsWorkRateBonusPerUnit = 0.5f;
        private static readonly Vec3d SmokeStackLocalPosition = new Vec3d(14.2f / 16f, 32.8f / 16f, -6.2f / 16f);

        private readonly InventoryGeneric inventory;
        private IOFaceMap ioFaces;

        public override InventoryBase Inventory => inventory;
        public override string InventoryClassName => "kineticcharcoalretort";
        public IOFaceMap IOFaces => ioFaces;

        public BEKineticCharcoalRetort()
        {
            inventory = new InventoryGeneric(InventorySize, "kineticcharcoalretort-0", null, null, (slotId, self) =>
            {
                return slotId <= SlotInputLast
                    ? new ItemSlotFirewoodInput(self)
                    : new ItemSlotCrusherOutput(self);
            });
        }

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            inventory.LateInitialize("kineticcharcoalretort-" + Pos, api);
            inventory.ResolveBlocksOrItems();
            inventory.SlotModified += _ =>
            {
                NormalizeStoredStacks();
                MarkDirty(true);
            };
            NormalizeStoredStacks();

            ConfigureIOFaceMap();

            if (api.Side == EnumAppSide.Server)
            {
                RegisterGameTickListener(OnServerPushTick, (int)OutputPushIntervalMs);
                RegisterGameTickListener(OnSmokeTick, (int)RetortSmokeIntervalMs);
            }

            BEBehaviorKineticWorker worker = GetBehavior<BEBehaviorKineticWorker>();
            if (worker != null) worker.OnWorkCompleted += OnWorkCycle;
        }

        private void ConfigureIOFaceMap()
        {
            BlockFacing facing = PlacementFacingFromVariant();
            BlockFacing inputFace = LeftOf(facing);
            BlockFacing outputFace = RightOf(facing);

            ioFaces = new IOFaceMap(Pos);
            foreach (BlockPos cell in CellsOnFace(BlockFacing.UP))
            {
                for (int i = SlotInputFirst; i <= SlotInputLast; i++)
                {
                    ioFaces.MapInput(cell, BlockFacing.UP, i);
                }
            }
            foreach (BlockPos cell in CellsOnFace(inputFace))
            {
                for (int i = SlotInputFirst; i <= SlotInputLast; i++)
                {
                    ioFaces.MapInput(cell, inputFace, i);
                }
            }
            foreach (BlockPos cell in CellsOnFace(outputFace))
            {
                for (int i = SlotOutputFirst; i <= SlotOutputLast; i++)
                {
                    ioFaces.MapOutput(cell, outputFace, i);
                }
            }
            foreach (BlockPos cell in CellsOnFace(BlockFacing.DOWN))
            {
                for (int i = SlotOutputFirst; i <= SlotOutputLast; i++)
                {
                    ioFaces.MapOutput(cell, BlockFacing.DOWN, i);
                }
            }
            ioFaces.Apply(inventory);
        }

        private void OnServerPushTick(float dt)
        {
            if (ioFaces == null) return;
            foreach (FaceMapEntry entry in ioFaces.OutputEntries)
            {
                foreach (int slotId in entry.SlotIds)
                {
                    ItemSlot slot = inventory[slotId];
                    if (slot.Empty) continue;
                    int moved = InventoryPusher.TryPush(Api.World, entry.Cell, entry.Face, slot, OutputPushBatch);
                    if (moved > 0) MarkDirty(true);
                }
            }
        }

        private void OnWorkCycle(KineticWorkCompletedArgs args)
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
                ItemSlot slot = inventory[i];
                if (slot.Empty) continue;
                if (IsFirewood(slot.Itemstack)) return slot;
            }
            return null;
        }

        private bool CanDepositOutput(ItemStack stack)
        {
            for (int i = SlotOutputFirst; i <= SlotOutputLast; i++)
            {
                ItemSlot slot = inventory[i];
                if (slot.Empty) return true;
                if (slot.Itemstack?.Collectible == stack.Collectible
                    && slot.Itemstack.StackSize < slot.Itemstack.Collectible.MaxStackSize)
                {
                    return true;
                }
            }
            return false;
        }

        private void DepositOutput(ItemStack stack)
        {
            Vec3d at = new Vec3d(Pos.X + 0.5, Pos.Y + 0.1, Pos.Z + 0.5);
            MachineOutputHelper.DepositOrPush(this, inventory, SlotOutputFirst, SlotOutputLast, stack, ioFaces?.OutputEntries, OutputPushBatch, at);
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
            if (byPlayer?.InventoryManager?.ActiveHotbarSlot == null) return true;
            if (Api?.World is IServerWorldAccessor && !Api.World.Claims.TryAccess(byPlayer, Pos, EnumBlockAccessFlags.Use))
            {
                Api.World.Logger.Audit("Player {0} tried to use charcoal retort at {1} but has no claim access. Rejected.", byPlayer.PlayerName, Pos);
                return true;
            }

            bool put = byPlayer.Entity?.Controls?.ShiftKey == true;
            bool bulk = byPlayer.Entity?.Controls?.CtrlKey == true;
            ItemSlot hotbarSlot = byPlayer.InventoryManager.ActiveHotbarSlot;

            if (put)
            {
                if (!hotbarSlot.Empty && IsFirewood(hotbarSlot.Itemstack))
                {
                    TryPutFirewood(byPlayer, hotbarSlot, bulk);
                }
                return true;
            }

            TryTakeCharcoal(byPlayer, bulk);
            return true;
        }

        private void TryPutFirewood(IPlayer byPlayer, ItemSlot hotbarSlot, bool bulk)
        {
            int remaining = bulk ? hotbarSlot.StackSize : 1;
            if (remaining <= 0) return;

            int movedTotal = 0;
            for (int i = SlotInputFirst; i <= SlotInputLast && remaining > 0 && !hotbarSlot.Empty; i++)
            {
                ItemSlot target = inventory[i];
                if (!target.CanHold(hotbarSlot)) continue;

                int moved = hotbarSlot.TryPutInto(Api.World, target, remaining);
                if (moved <= 0) continue;

                movedTotal += moved;
                remaining -= moved;
                target.MarkDirty();
                if (!bulk) break;
            }

            if (movedTotal > 0)
            {
                hotbarSlot.MarkDirty();
                DidMoveItems(byPlayer);
                Api.World.Logger.Audit("{0} Put {1}xfirewood into {2} at {3}.",
                    byPlayer.PlayerName, movedTotal, Block?.Code, Pos);
                MarkDirty(true);
            }
        }

        private bool TryTakeCharcoal(IPlayer byPlayer, bool bulk)
        {
            ItemSlot sourceSlot = FirstNonEmptySlot(SlotOutputFirst, SlotOutputLast);
            if (sourceSlot == null || sourceSlot.Empty) return true;

            int requestedQuantity = bulk ? sourceSlot.Itemstack.Collectible.MaxStackSize : 1;
            FillSlotFromRange(sourceSlot, SlotOutputFirst, SlotOutputLast, requestedQuantity);

            ItemStack stack = sourceSlot.TakeOut(requestedQuantity);
            int originalQuantity = stack.StackSize;
            byPlayer.InventoryManager.TryGiveItemstack(stack, true);

            int remaining = stack?.StackSize ?? 0;
            int taken = originalQuantity - remaining;
            if (remaining > 0)
            {
                DepositStackDirect(stack, SlotOutputFirst, SlotOutputLast);
            }

            if (taken > 0)
            {
                DidMoveItems(byPlayer);
                Api.World.Logger.Audit("{0} Took {1}xcharcoal from {2} at {3}.",
                    byPlayer.PlayerName, taken, Block?.Code, Pos);
                sourceSlot.MarkDirty();
                MarkDirty(true);
            }

            return true;
        }

        private void FillSlotFromRange(ItemSlot target, int firstSlot, int lastSlot, int targetQuantity)
        {
            if (target == null || target.Empty) return;
            for (int i = firstSlot; i <= lastSlot && target.StackSize < targetQuantity; i++)
            {
                ItemSlot source = inventory[i];
                if (source == target || source.Empty) continue;
                if (!source.Itemstack.Collectible.Code.Equals(target.Itemstack.Collectible.Code)) continue;

                int needed = targetQuantity - target.StackSize;
                int moved = System.Math.Min(needed, source.StackSize);
                target.Itemstack.StackSize += moved;
                source.Itemstack.StackSize -= moved;
                target.MarkDirty();
                source.MarkDirty();
                if (source.Itemstack.StackSize <= 0) source.Itemstack = null;
            }
        }

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
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
        {
            base.FromTreeAttributes(tree, worldForResolving);
            inventory?.FromTreeAttributes(tree);
            if (Api != null)
            {
                inventory?.ResolveBlocksOrItems();
                NormalizeStoredStacks();
                ConfigureIOFaceMap();
            }
        }

        public override void GetBlockInfo(IPlayer forPlayer, System.Text.StringBuilder dsc)
        {
            base.GetBlockInfo(forPlayer, dsc);

            int firewood = CountItems(SlotInputFirst, SlotInputLast);
            int charcoal = CountItems(SlotOutputFirst, SlotOutputLast);
            int bellowsCount = PoweredBellowsCount();
            dsc.AppendLine(Lang.Get("vintagekinematics:kineticcharcoalretort-firewood", firewood, CapacityItems(SlotInputFirst)));
            dsc.AppendLine(Lang.Get("vintagekinematics:kineticcharcoalretort-charcoal", charcoal, CapacityItems(SlotOutputFirst)));
            dsc.AppendLine($"Bellows assist: {bellowsCount}/{MaxBellowsAssistCount} ({1f + bellowsCount * BellowsWorkRateBonusPerUnit:0.##}x)");
        }

        private void NormalizeStoredStacks()
        {
            if (inventory == null) return;

            for (int i = SlotInputFirst; i <= SlotInputLast; i++)
            {
                ItemSlot slot = inventory[i];
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
                ItemSlot slot = inventory[i];
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
            DepositStackDirect(source.Itemstack, firstSlot, lastSlot, source);

            if (!source.Empty)
            {
                source.Itemstack = null;
                source.MarkDirty();
            }
        }

        private void DepositStackDirect(ItemStack stack, int firstSlot, int lastSlot, ItemSlot skipSlot = null)
        {
            if (stack == null || stack.StackSize <= 0) return;
            for (int i = firstSlot; i <= lastSlot && stack.StackSize > 0; i++)
            {
                ItemSlot target = inventory[i];
                if (target == skipSlot) continue;
                if (target.Empty) continue;
                if (!target.Itemstack.Collectible.Code.Equals(stack.Collectible.Code)) continue;

                int free = target.Itemstack.Collectible.MaxStackSize - target.Itemstack.StackSize;
                if (free <= 0) continue;

                int moved = System.Math.Min(free, stack.StackSize);
                target.Itemstack.StackSize += moved;
                stack.StackSize -= moved;
                target.MarkDirty();
            }

            for (int i = firstSlot; i <= lastSlot && stack.StackSize > 0; i++)
            {
                ItemSlot target = inventory[i];
                if (target == skipSlot) continue;
                if (!target.Empty) continue;

                int moved = System.Math.Min(stack.Collectible.MaxStackSize, stack.StackSize);
                ItemStack placed = stack.Clone();
                placed.StackSize = moved;
                target.Itemstack = placed;
                stack.StackSize -= moved;
                target.MarkDirty();
            }
        }

        private ItemSlot FirstNonEmptySlot(int firstSlot, int lastSlot)
        {
            for (int i = firstSlot; i <= lastSlot; i++)
            {
                ItemSlot slot = inventory[i];
                if (!slot.Empty) return slot;
            }
            return null;
        }

        private int CountItems(int firstSlot, int lastSlot)
        {
            int count = 0;
            for (int i = firstSlot; i <= lastSlot; i++)
            {
                ItemSlot slot = inventory[i];
                if (!slot.Empty) count += slot.StackSize;
            }
            return count;
        }

        private int CapacityItems(int firstSlot)
        {
            ItemStack stack = firstSlot == SlotInputFirst ? FirewoodStack() : CharcoalStack();
            return stack?.Collectible?.MaxStackSize * 64 ?? 0;
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

        private void DidMoveItems(IPlayer byPlayer)
        {
            byPlayer.Entity?.World?.PlaySoundAt(
                new AssetLocation("game:sounds/player/build"),
                byPlayer.Entity,
                byPlayer,
                true,
                16);
        }

        private BlockPos[] CellsOnFace(BlockFacing face)
        {
            if (!MultiblockHelper.TryGetClaim(Block, Pos, out BlockPos baseCorner, out Vec3i size))
            {
                return new[] { Pos };
            }

            var cells = new System.Collections.Generic.List<BlockPos>();
            for (int x = 0; x < size.X; x++)
            for (int y = 0; y < size.Y; y++)
            for (int z = 0; z < size.Z; z++)
            {
                if (!IsOnClaimFace(face, x, y, z, size)) continue;
                cells.Add(new BlockPos(baseCorner.X + x, baseCorner.Y + y, baseCorner.Z + z, Pos.dimension));
            }
            return cells.ToArray();
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
                return new[] { Pos.AddCopy(RightOf(PlacementFacingFromVariant())) };
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

        private static bool IsOnClaimFace(BlockFacing face, int x, int y, int z, Vec3i size)
        {
            if (face == BlockFacing.WEST) return x == 0;
            if (face == BlockFacing.EAST) return x == size.X - 1;
            if (face == BlockFacing.DOWN) return y == 0;
            if (face == BlockFacing.UP) return y == size.Y - 1;
            if (face == BlockFacing.NORTH) return z == 0;
            if (face == BlockFacing.SOUTH) return z == size.Z - 1;
            return false;
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

        private BlockFacing PlacementFacingFromVariant()
        {
            string side = Block?.Variant?["side"] ?? "n";
            switch (side)
            {
                case "n": return BlockFacing.NORTH;
                case "e": return BlockFacing.EAST;
                case "s": return BlockFacing.SOUTH;
                case "w": return BlockFacing.WEST;
                default: return BlockFacing.NORTH;
            }
        }

        private static BlockFacing LeftOf(BlockFacing facing)
        {
            if (facing == BlockFacing.NORTH) return BlockFacing.WEST;
            if (facing == BlockFacing.EAST) return BlockFacing.NORTH;
            if (facing == BlockFacing.SOUTH) return BlockFacing.EAST;
            if (facing == BlockFacing.WEST) return BlockFacing.SOUTH;
            return BlockFacing.EAST;
        }

        private static BlockFacing RightOf(BlockFacing facing)
        {
            if (facing == BlockFacing.NORTH) return BlockFacing.EAST;
            if (facing == BlockFacing.EAST) return BlockFacing.SOUTH;
            if (facing == BlockFacing.SOUTH) return BlockFacing.WEST;
            if (facing == BlockFacing.WEST) return BlockFacing.NORTH;
            return BlockFacing.WEST;
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
