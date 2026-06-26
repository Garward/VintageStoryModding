using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using VintageKinematics.BlockEntities;
using VintageKinematics.Blocks;

namespace VintageKinematics.Api
{
    /// <summary>
    /// Static helper that pushes from a source slot into the inventory of the BE on the neighbouring
    /// block in a given direction. Mirrors the funnel's push behaviour so any BE that owns outputs
    /// can actively eject into adjacent storage without re-implementing the slot-selection dance.
    /// </summary>
    public static class InventoryPusher
    {
        /// <summary>
        /// Try to move up to <paramref name="maxQuantity"/> items from <paramref name="source"/> into
        /// the inventory of the BE adjacent to <paramref name="fromPos"/> on <paramref name="toFace"/>.
        /// Returns the number of items moved. Honours the target's <c>OnGetAutoPushIntoSlot</c> when
        /// set (restricted push); falls back to <c>GetBestSuitedSlot</c> otherwise.
        /// </summary>
        public static int TryPush(IWorldAccessor world, BlockPos fromPos, BlockFacing toFace, ItemSlot source, int maxQuantity = int.MaxValue)
        {
            if (source == null || source.Empty || maxQuantity <= 0) return 0;

            BlockPos targetPos = fromPos.AddCopy(toFace);

            // Some blocks (e.g. crusher head sitting on its basin) declare themselves transparent
            // to downward push so a funnel above the head still reaches the basin's inventory.
            if (toFace == BlockFacing.DOWN)
            {
                Block intermediate = world.BlockAccessor.GetBlock(targetPos);
                if (intermediate?.Attributes?["ioPassthroughDown"].AsBool(false) == true)
                {
                    targetPos = targetPos.DownCopy();
                }
            }

            BlockEntity targetBe = MultiblockHelper.GetMultiblockAwareBE(world, targetPos);

            // Belts and funnels aren't IBlockEntityContainer targets for direct insertion, but
            // output faces should still feed them just like they feed adjacent storage.
            if (targetBe is BEBelt belt) return TryPushOntoBelt(world, belt, toFace, source, maxQuantity);
            if (targetBe is BEFunnel funnel) return TryPushIntoFunnel(funnel, source, maxQuantity);
            if (targetBe is BlockEntityBarrel barrel) return BarrelAutomation.TryPushItemIntoBarrel(world, barrel, source, maxQuantity);

            if (targetBe is not IBlockEntityContainer container) return 0;

            IInventory inventory = container.Inventory;
            if (inventory == null || inventory.PutLocked) return 0;

            BlockFacing fromFace = toFace.Opposite;
            IOFaceMap faceMap = (targetBe as IFaceMappedContainer)?.IOFaces;
            bool restrictedPush = faceMap != null
                || (inventory is InventoryGeneric pushInv && pushInv.OnGetAutoPushIntoSlot != null);
            InventoryBase invBase = inventory as InventoryBase;

            int requested = System.Math.Min(maxQuantity, source.Itemstack.StackSize);
            DummySlot probe = new DummySlot(source.Itemstack.Clone());
            probe.Itemstack.StackSize = requested;
            int startSize = probe.Itemstack.StackSize;
            var skip = new List<ItemSlot>();

            while (!probe.Empty && probe.Itemstack.StackSize > 0)
            {
                int targetMoveLimit = int.MaxValue;
                ItemSlot targetSlot = faceMap != null
                    ? faceMap.GetPushSlot(inventory, targetPos, fromFace, probe, out targetMoveLimit)
                    : invBase?.GetAutoPushIntoSlot(fromFace, probe);
                int moved = 0;

                if (targetSlot != null)
                {
                    moved = MoveIntoSlot(world, probe, targetSlot, targetMoveLimit);
                    if (moved <= 0) skip.Add(targetSlot);
                }
                else if (restrictedPush)
                {
                    break;
                }

                if (moved <= 0)
                {
                    if (restrictedPush) break;

                    WeightedSlot weighted = inventory.GetBestSuitedSlot(probe, null, skip);
                    targetSlot = weighted?.slot;
                    if (targetSlot == null) break;

                    moved = MoveIntoSlot(world, probe, targetSlot);
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

            int remaining = probe.Empty ? 0 : probe.Itemstack.StackSize;
            int movedTotal = startSize - remaining;
            if (movedTotal <= 0) return 0;

            source.Itemstack.StackSize -= movedTotal;
            if (source.Itemstack.StackSize <= 0) source.Itemstack = null;
            source.MarkDirty();
            return movedTotal;
        }

        private static int TryPushOntoBelt(IWorldAccessor world, BEBelt belt, BlockFacing outputFace, ItemSlot source, int maxQuantity)
        {
            BEBelt controller = belt.IsController
                ? belt
                : world.BlockAccessor.GetBlockEntity(belt.ControllerPos) as BEBelt;
            if (controller == null || controller.ChainLength <= 0) return 0;
            if (!BeltMovesAwayFromOutput(controller, outputFace)) return 0;

            Vec3d targetCenter = new Vec3d(
                belt.Pos.X + 0.5,
                belt.Pos.Y + BEBelt.BeltTopY,
                belt.Pos.Z + 0.5);
            float progress = controller.ProjectOntoChain(targetCenter);
            if (progress < 0.05f) progress = 0.05f;
            if (progress > controller.ChainLength - 0.05f) progress = controller.ChainLength - 0.05f;

            int requested = System.Math.Min(maxQuantity, source.Itemstack.StackSize);
            ItemStack moving = source.Itemstack.Clone();
            moving.StackSize = requested;

            if (!controller.TryInsertItem(moving, progress)) return 0;

            source.Itemstack.StackSize -= requested;
            if (source.Itemstack.StackSize <= 0) source.Itemstack = null;
            source.MarkDirty();
            return requested;
        }

        private static bool BeltMovesAwayFromOutput(BEBelt controller, BlockFacing outputFace)
        {
            if (controller == null || outputFace == null) return false;

            BEBehaviorKinetic kinetic = controller.GetBehavior<BEBehaviorKinetic>();
            float velocity = controller.ChainVelocity(kinetic?.ActualRPM ?? 0f);
            if (System.MathF.Abs(velocity) < 0.0001f) return false;

            Vec3i head = BlockBelt.HeadOffset(controller.Direction);
            int sign = velocity > 0f ? 1 : -1;
            return outputFace.Normali.X == head.X * sign
                && outputFace.Normali.Z == head.Z * sign;
        }

        private static int TryPushIntoFunnel(BEFunnel funnel, ItemSlot source, int maxQuantity)
        {
            int requested = System.Math.Min(maxQuantity, source.Itemstack.StackSize);
            ItemStack moving = source.Itemstack.Clone();
            moving.StackSize = requested;

            funnel.TryAcceptFromPush(moving);

            int remaining = moving?.StackSize ?? 0;
            int moved = requested - remaining;
            if (moved <= 0) return 0;

            source.Itemstack.StackSize -= moved;
            if (source.Itemstack.StackSize <= 0) source.Itemstack = null;
            source.MarkDirty();
            return moved;
        }

        private static int MoveIntoSlot(IWorldAccessor world, ItemSlot source, ItemSlot target, int maxQuantity = int.MaxValue)
        {
            if (source == null || target == null || source.Empty || maxQuantity <= 0) return 0;
            ItemStackMoveOperation op = new ItemStackMoveOperation(
                world,
                EnumMouseButton.Left,
                0,
                EnumMergePriority.DirectMerge,
                System.Math.Min(source.Itemstack.StackSize, maxQuantity));
            return source.TryPutInto(target, ref op);
        }
    }
}
