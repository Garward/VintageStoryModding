using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using VintageKinematics.Api;
using VintageKinematics.Blocks;

namespace VintageKinematics.BlockEntities
{
    public partial class BEBelt
    {
        private bool CaptureNearbyItems()
        {
            if (ChainLength <= 0) return false;
            Vec3i fwd = BlockBelt.HeadOffset(Direction);
            BlockPos endPos = Pos.AddCopy(fwd.X * (ChainLength - 1), 0, fwd.Z * (ChainLength - 1));
            BlockPos minBp = new BlockPos(
                Math.Min(Pos.X, endPos.X),
                Pos.Y,
                Math.Min(Pos.Z, endPos.Z),
                Pos.dimension);
            BlockPos maxBp = new BlockPos(
                Math.Max(Pos.X, endPos.X) + 1,
                Pos.Y + 2,
                Math.Max(Pos.Z, endPos.Z) + 1,
                Pos.dimension);

            Entity[] found = Api.World.GetEntitiesInsideCuboid(minBp, maxBp,
                e => e is EntityItem ei && ei.Alive && ei.Itemstack != null);
            if (found.Length == 0) return false;

            bool changed = false;
            for (int i = 0; i < found.Length; i++)
            {
                EntityItem ei = (EntityItem)found[i];
                Vec3d epos = ei.Pos.XYZ;
                if (epos.Y < Pos.Y + BeltTopY - 0.1
                 || epos.Y > Pos.Y + BeltTopY + ItemCaptureMargin) continue;

                float p = ProjectOntoChain(epos);
                if (p <= 0.05f || p >= ChainLength - 0.05f) continue;

                items.Add(new BeltItem { Stack = ei.Itemstack.Clone(), Progress = p });
                ei.Die(EnumDespawnReason.PickedUp);
                changed = true;
            }
            return changed;
        }

        /// <summary>
        /// Hand every item on this chain to <paramref name="player"/>, and also vacuum up any
        /// EntityItems hovering above the clicked segment (those are the items the belt's capture
        /// pass skipped because they sat too close to a chain end: they look "stuck" to the
        /// player even though they are real world entities). Routes via the controller for the
        /// chain-item half; the entity sweep runs on the clicked segment's bounding column.
        /// </summary>
        public int ClaimItemsAt(BlockPos clickedSegmentPos, IPlayer player)
        {
            int claimed = 0;

            // 1. Belt-chain items: always handled by the controller.
            BEBelt ctl = IsController
                ? this
                : Api.World.BlockAccessor.GetBlockEntity(ControllerPos) as BEBelt;
            if (ctl != null && ctl.items.Count > 0)
            {
                for (int i = ctl.items.Count - 1; i >= 0; i--)
                {
                    ItemStack stack = ctl.items[i].Stack;
                    if (stack == null || stack.StackSize <= 0) { ctl.items.RemoveAt(i); continue; }
                    Vec3d at = ctl.ProgressToWorld(ctl.items[i].Progress);
                    if (!player.InventoryManager.TryGiveItemstack(stack, true))
                    {
                        Api.World.SpawnItemEntity(stack, at);
                    }
                    claimed += stack.StackSize;
                    ctl.items.RemoveAt(i);
                }
                ctl.MarkDirty(true);
            }

            // 2. EntityItems anywhere along the chain's footprint, plus one block past either
            // end. CaptureNearbyItems intentionally ignores entities within 0.05 of a chain end,
            // so a chest that spits items back onto the belt edge leaves them as world entities
            // that look pinned to the belt surface; chest collision can also nudge them sideways
            // by a fraction of a block. Sweep the whole chain bbox (inflated by 1 on every side)
            // so the player doesn't have to click the exact segment the entity sits on.
            BEBelt sweepCtl = ctl ?? this;
            Vec3i sweepFwd = BlockBelt.HeadOffset(sweepCtl.Direction ?? Direction);
            BlockPos endPos = sweepCtl.Pos.AddCopy(
                sweepFwd.X * (sweepCtl.ChainLength - 1), 0, sweepFwd.Z * (sweepCtl.ChainLength - 1));
            BlockPos minBp = new BlockPos(
                Math.Min(sweepCtl.Pos.X, endPos.X) - 1,
                sweepCtl.Pos.Y,
                Math.Min(sweepCtl.Pos.Z, endPos.Z) - 1,
                sweepCtl.Pos.dimension);
            BlockPos maxBp = new BlockPos(
                Math.Max(sweepCtl.Pos.X, endPos.X) + 2,
                sweepCtl.Pos.Y + 2,
                Math.Max(sweepCtl.Pos.Z, endPos.Z) + 2,
                sweepCtl.Pos.dimension);
            Entity[] found = Api.World.GetEntitiesInsideCuboid(minBp, maxBp,
                e => e is EntityItem ei && ei.Alive && ei.Itemstack != null);
            for (int i = 0; i < found.Length; i++)
            {
                EntityItem ei = (EntityItem)found[i];
                ItemStack stack = ei.Itemstack;
                int before = stack.StackSize;
                if (player.InventoryManager.TryGiveItemstack(stack, true))
                {
                    claimed += before;
                    ei.Die(EnumDespawnReason.PickedUp);
                }
                else if (stack.StackSize < before)
                {
                    claimed += before - stack.StackSize;
                }
            }
            return claimed;
        }

        private bool TryMovePastEnd(BeltItem item, bool atHeadEnd, float velocity)
        {
            if (TryHandOffToNeighbor(item, atHeadEnd)) return true;

            if (HasBeltAtExit(atHeadEnd))
            {
                item.Progress = atHeadEnd ? ChainLength - ItemEndStopMargin : ItemEndStopMargin;
                return false;
            }

            if (HasBlockAtExit(atHeadEnd))
            {
                item.Progress = atHeadEnd ? ChainLength - ItemEndStopMargin : ItemEndStopMargin;
                return false;
            }

            float eject = atHeadEnd ? ChainLength : 0f;
            Vec3d pos = ProgressToWorld(eject);
            // Bump up slightly so it doesn't immediately collide back into the belt.
            pos.Y += 0.05;
            Vec3i fwd = BlockBelt.HeadOffset(Direction);
            float forwardSign = atHeadEnd ? 1f : -1f;
            float speed = MathF.Abs(velocity) * ItemEjectVelocityScale + 0.05f;
            Vec3d motion = new Vec3d(fwd.X * forwardSign * speed, 0.05, fwd.Z * forwardSign * speed);
            Api.World.SpawnItemEntity(item.Stack, pos, motion);
            return true;
        }

        /// <summary>
        /// If the block one past the exiting end is another belt and the projected progress lands
        /// within that chain's interior, insert directly into its controller and return true.
        /// Avoids the EntityItem round-trip which can drop the item between adjacent belts.
        /// </summary>
        private bool TryHandOffToNeighbor(BeltItem item, bool atHeadEnd)
        {
            if (Direction == null) return false;
            Vec3i fwd = BlockBelt.HeadOffset(Direction);
            int exitOffset = atHeadEnd ? ChainLength : -1;
            BlockPos nextPos = Pos.AddCopy(fwd.X * exitOffset, 0, fwd.Z * exitOffset);
            BlockEntity nextBe = Api.World.BlockAccessor.GetBlockEntity(nextPos);
            if (nextBe is BEFunnel funnel)
            {
                return funnel.TryAcceptFromBelt(item.Stack);
            }
            if (nextBe is IAutomationItemSink sink)
            {
                return sink.TryAcceptFromBelt(item.Stack);
            }

            // If the next block is another belt, hand off directly into its chain so the item never
            // round-trips through the entity layer.
            if (nextBe is BEBelt nb)
            {
                BEBelt ctl = nb.IsController
                    ? nb
                    : Api.World.BlockAccessor.GetBlockEntity(nb.ControllerPos) as BEBelt;
                if (ctl == null || ctl.ChainLength <= 0) return false;

                Vec3d wp = ProgressToWorld(atHeadEnd ? ChainLength : 0f);
                float p = ctl.ProjectOntoChain(wp);
                if (p < -0.05f || p > ctl.ChainLength + 0.05f) return false;
                if (p < 0.05f) p = 0.05f;
                if (p > ctl.ChainLength - 0.05f) p = ctl.ChainLength - 0.05f;

                return ctl.TryInsertItem(item.Stack, p);
            }

            // Otherwise, if the next block is a container with at least one input slot mapped to the
            // face the belt is approaching from, push the stack directly into it. This is how belts
            // feed machine input slots without a funnel in between. Honours the target's IOFaceMap
            // automatically via InventoryPusher.
            BlockEntity targetBe = MultiblockHelper.GetMultiblockAwareBE(Api.World, nextPos);
            if (targetBe is IBlockEntityContainer)
            {
                BlockFacing exitFace = BlockFacing.FromNormal(new Vec3i(
                    fwd.X * (atHeadEnd ? 1 : -1),
                    0,
                    fwd.Z * (atHeadEnd ? 1 : -1)));
                if (exitFace == null) return false;

                // Push must originate from the segment adjacent to the container, not the
                // controller's Pos (which is the chain's start). Otherwise InventoryPusher
                // computes targetPos = Pos + exitFace, which on a multi-segment belt lands on
                // the second segment of THIS chain, and the BEBelt target path would happily
                // reinsert the item near the start, causing it to loop.
                BlockPos pushFrom = atHeadEnd
                    ? Pos.AddCopy(fwd.X * (ChainLength - 1), 0, fwd.Z * (ChainLength - 1))
                    : Pos;

                DummySlot probe = new DummySlot(item.Stack);
                int moved = InventoryPusher.TryPush(Api.World, pushFrom, exitFace, probe);
                // Probe shares the stack reference, so item.Stack.StackSize has already been
                // reduced. Caller drops the belt item only when its stack is fully consumed; partial
                // acceptance lets the remainder fall off as an entity (see TryMovePastEnd).
                return probe.Empty || (item.Stack?.StackSize ?? 0) <= 0;
            }

            return false;
        }

        private bool HasBeltAtExit(bool atHeadEnd)
        {
            if (Direction == null) return false;
            Vec3i fwd = BlockBelt.HeadOffset(Direction);
            int exitOffset = atHeadEnd ? ChainLength : -1;
            BlockPos nextPos = Pos.AddCopy(fwd.X * exitOffset, 0, fwd.Z * exitOffset);
            return Api.World.BlockAccessor.GetBlockEntity(nextPos) is BEBelt;
        }

        private bool HasBlockAtExit(bool atHeadEnd)
        {
            if (Direction == null) return false;
            Vec3i fwd = BlockBelt.HeadOffset(Direction);
            int exitOffset = atHeadEnd ? ChainLength : -1;
            BlockPos nextPos = Pos.AddCopy(fwd.X * exitOffset, 0, fwd.Z * exitOffset);
            Block block = Api.World.BlockAccessor.GetBlock(nextPos);
            return block != null && block.Id != 0;
        }

        private bool IsAtBlockedExit(float progress, float velocity, out bool atHeadEnd)
        {
            atHeadEnd = velocity > 0f;
            if (velocity > 0f)
            {
                return progress >= ChainLength - ItemEndStopMargin && HasBlockAtExit(true);
            }
            if (velocity < 0f)
            {
                atHeadEnd = false;
                return progress <= ItemEndStopMargin && HasBlockAtExit(false);
            }
            return false;
        }

        private bool TryTransferToAdjacentAutomationSink(BeltItem item)
        {
            if (item?.Stack == null || item.Stack.StackSize <= 0) return true;
            BlockPos beltPos = SegmentPosForProgress(item.Progress);

            foreach (BlockFacing face in BlockFacing.ALLFACES)
            {
                BlockPos neighbor = beltPos.AddCopy(face);
                BlockEntity neighborBe = Api.World.BlockAccessor.GetBlockEntity(neighbor);
                int before = item.Stack.StackSize;
                bool fullyAccepted;

                if (neighborBe is BEFunnel funnel)
                {
                    if (funnel.OutputFacing == face.Opposite) continue;
                    fullyAccepted = funnel.TryAcceptFromBelt(item.Stack);
                }
                else if (neighborBe is IAutomationItemSink sink)
                {
                    fullyAccepted = sink.TryAcceptFromBelt(item.Stack);
                }
                else
                {
                    continue;
                }

                if (fullyAccepted || item.Stack.StackSize <= 0) return true;

                // Partial transfer succeeded; keep the remainder on the belt.
                if (item.Stack.StackSize < before) return false;
            }

            return false;
        }

        private BlockPos SegmentPosForProgress(float progress)
        {
            Vec3i fwd = BlockBelt.HeadOffset(Direction);
            int index = (int)MathF.Floor(progress);
            if (index < 0) index = 0;
            if (index >= ChainLength) index = ChainLength - 1;
            return Pos.AddCopy(fwd.X * index, 0, fwd.Z * index);
        }

        public void InsertItem(ItemStack stack, float progress)
        {
            if (stack == null) return;
            items.Add(new BeltItem { Stack = stack, Progress = progress });
            MarkDirty(true);
        }

        public bool TryInsertItem(ItemStack stack, float progress)
        {
            if (stack == null || stack.StackSize <= 0) return true;

            for (int i = 0; i < items.Count; i++)
            {
                if (MathF.Abs(items[i].Progress - progress) < ItemInsertClearance) return false;
            }

            InsertItem(stack, progress);
            return true;
        }

        /// <summary>Project a world position onto the chain axis, returning the progress value.</summary>
        public float ProjectOntoChain(Vec3d worldPos)
        {
            if (Direction == null) return 0f;
            Vec3i fwd = BlockBelt.HeadOffset(Direction);
            double dx = worldPos.X - (Pos.X + 0.5);
            double dz = worldPos.Z - (Pos.Z + 0.5);
            return (float)(dx * fwd.X + dz * fwd.Z) + 0.5f;
        }

        /// <summary>Map a chain progress value to a world position on the belt-top surface.</summary>
        public Vec3d ProgressToWorld(float progress)
        {
            Vec3i fwd = BlockBelt.HeadOffset(Direction);
            return new Vec3d(
                Pos.X + 0.5 + (progress - 0.5f) * fwd.X,
                Pos.Y + BeltTopY,
                Pos.Z + 0.5 + (progress - 0.5f) * fwd.Z);
        }

        /// <summary>
        /// Per-variant sign that maps "positive RPM around the pulley axis" onto
        /// "progress increases (items travel toward the head end)". For axis=X variants n/s,
        /// positive RPM moves the belt top toward +Z; for axis=Z variants e/w, positive RPM
        /// moves the belt top toward -X. Combined with each variant's HeadOffset, that yields:
        /// </summary>
        public static int HeadDirSign(string direction) => direction switch
        {
            "n" => -1,
            "e" => -1,
            "s" =>  1,
            "w" =>  1,
            _   =>  1
        };

        private void DumpAllItems()
        {
            if (items.Count == 0) return;
            for (int i = 0; i < items.Count; i++)
            {
                Vec3d pos = ProgressToWorld(items[i].Progress);
                Api.World.SpawnItemEntity(items[i].Stack, pos);
            }
            items.Clear();
            MarkDirty(true);
        }
    }
}
