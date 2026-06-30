using System;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using VintageKinematics.Api;
using VintageKinematics.Blocks;

namespace VintageKinematics.BlockEntities
{
    public partial class BEBelt
    {
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
            pos.Y += 0.05;
            Vec3i fwd = BlockBelt.HeadOffset(Direction);
            float forwardSign = atHeadEnd ? 1f : -1f;
            float speed = MathF.Abs(velocity) * ItemEjectVelocityScale + 0.05f;
            Vec3d motion = new Vec3d(fwd.X * forwardSign * speed, 0.05, fwd.Z * forwardSign * speed);
            Api.World.SpawnItemEntity(item.Stack, pos, motion);
            return true;
        }

        /// <summary>
        /// If the block one past the exiting end can accept this item, hand it off directly.
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
            if (nextBe is BEBelt nb)
            {
                return TryHandOffToBelt(item, atHeadEnd, nb);
            }

            return TryPushIntoAdjacentContainer(item, atHeadEnd, fwd, nextPos);
        }

        private bool TryHandOffToBelt(BeltItem item, bool atHeadEnd, BEBelt nextBelt)
        {
            BEBelt ctl = nextBelt.IsController
                ? nextBelt
                : Api.World.BlockAccessor.GetBlockEntity(nextBelt.ControllerPos) as BEBelt;
            if (ctl == null || ctl.ChainLength <= 0) return false;

            Vec3d wp = ProgressToWorld(atHeadEnd ? ChainLength : 0f);
            float p = ctl.ProjectOntoChain(wp);
            if (p < -0.05f || p > ctl.ChainLength + 0.05f) return false;
            if (p < 0.05f) p = 0.05f;
            if (p > ctl.ChainLength - 0.05f) p = ctl.ChainLength - 0.05f;

            return ctl.TryInsertItem(item.Stack, p);
        }

        private bool TryPushIntoAdjacentContainer(BeltItem item, bool atHeadEnd, Vec3i fwd, BlockPos nextPos)
        {
            BlockEntity targetBe = MultiblockHelper.GetMultiblockAwareBE(Api.World, nextPos);
            if (targetBe is not IBlockEntityContainer) return false;

            BlockFacing exitFace = BlockFacing.FromNormal(new Vec3i(
                fwd.X * (atHeadEnd ? 1 : -1),
                0,
                fwd.Z * (atHeadEnd ? 1 : -1)));
            if (exitFace == null) return false;

            // Push must originate from the segment adjacent to the container, not the controller.
            BlockPos pushFrom = atHeadEnd
                ? Pos.AddCopy(fwd.X * (ChainLength - 1), 0, fwd.Z * (ChainLength - 1))
                : Pos;

            DummySlot probe = new DummySlot(item.Stack);
            InventoryPusher.TryPush(Api.World, pushFrom, exitFace, probe);
            return probe.Empty || (item.Stack?.StackSize ?? 0) <= 0;
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
                if (item.Stack.StackSize < before) return false;
            }

            return false;
        }
    }
}
