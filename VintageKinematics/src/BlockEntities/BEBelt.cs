using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using VintageKinematics.Api;
using VintageKinematics.Blocks;
using VintageKinematics.Network;
using VintageKinematics.Rendering;

namespace VintageKinematics.BlockEntities
{
    /// <summary>
    /// Position of a belt segment within its chain. <c>Solo</c> = standalone single-segment belt.
    /// </summary>
    public enum EnumBeltPart { Solo, Start, Middle, End }

    /// <summary>
    /// Belt segment block entity. Phase 1: chain assembly + static render only — no items yet.
    /// Stores a back-pointer to the chain controller (the <see cref="EnumBeltPart.Start"/> segment),
    /// the segment's index in the chain, and total chain length. The controller is authoritative
    /// for shared state; non-controller segments delegate item queries up.
    /// </summary>
    public class BEBelt : BlockEntity
    {
        public const int MaxChainLength = 64;

        /// <summary>Position of the controller (Start segment). Equals own Pos if this is the controller.</summary>
        public BlockPos ControllerPos { get; private set; }
        /// <summary>0-based index in the chain.</summary>
        public int IndexInChain { get; private set; }
        /// <summary>Total number of segments in this chain (controller is authoritative).</summary>
        public int ChainLength { get; private set; } = 1;
        /// <summary>This segment's role in the chain.</summary>
        public EnumBeltPart Part { get; private set; } = EnumBeltPart.Solo;

        /// <summary>Direction variant of this belt block (n/e/s/w).</summary>
        public string Direction { get; private set; }

        /// <summary>True if a shaft is inserted through this segment (visual + future kinetic tap-off).</summary>
        public bool HasShaft { get; private set; }

        /// <summary>Axis (x/y/z) of the inserted shaft, for round-tripping on extraction. Empty when none.</summary>
        public string InsertedShaftAxis { get; private set; }

        /// <summary>
        /// Item-list — only populated on the controller. Items are in chain order with
        /// progress in [0, ChainLength], 0 = tail face of Start, ChainLength = head face of End.
        /// </summary>
        private readonly List<BeltItem> items = new();

        /// <summary>Read-only view of the controller's items. Empty list if not controller.</summary>
        public IReadOnlyList<BeltItem> Items => items;

        /// <summary>Y at which items rest (just above the top belt strip).</summary>
        public const float BeltTopY = 11f / 16f;

        private const float ItemCaptureMargin = 0.35f;
        private const float ItemEjectVelocityScale = 0.2f;
        private const float ItemInsertClearance = 0.3f;
        private const float ItemEndStopMargin = 0.05f;

        private BeltAnimationRenderer animationRenderer;
        private long tickListenerId;

        public void SetShaft(bool value, string axis = null)
        {
            if (HasShaft == value && InsertedShaftAxis == axis) return;
            HasShaft = value;
            InsertedShaftAxis = value ? axis : null;
            UpdateKineticState(triggerRebuild: true);
            MarkDirty(true);
        }

        /// <summary>True iff this segment is the controller (the chain's <see cref="EnumBeltPart.Start"/>).</summary>
        public bool IsController => ControllerPos != null && ControllerPos.Equals(Pos);

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            Direction = Block?.Variant?["direction"];
            UpdateKineticState(triggerRebuild: false);
            if (ControllerPos == null) ControllerPos = Pos.Copy();
            if (api.Side == EnumAppSide.Server)
            {
                // Defer assembly one tick so neighbouring BEs are also initialized.
                RegisterDelayedCallback(_ => RebuildChain(), 50);
                tickListenerId = RegisterGameTickListener(OnServerTick, 50);
            }
            else if (api is ICoreClientAPI capi)
            {
                animationRenderer = new BeltAnimationRenderer(capi, this, GetBehavior<BEBehaviorKinetic>());
                capi.Event.RegisterRenderer(animationRenderer, EnumRenderStage.Opaque);
                tickListenerId = RegisterGameTickListener(OnClientTick, 50);
            }
        }

        public override void OnBlockPlaced(ItemStack byItemStack = null)
        {
            base.OnBlockPlaced(byItemStack);
            Direction = Block?.Variant?["direction"];
            UpdateKineticState(triggerRebuild: false);
            if (Api?.Side == EnumAppSide.Server)
            {
                RebuildChain();
                RegisterDelayedCallback(_ =>
                {
                    Api?.ModLoader.GetModSystem<KineticNetworkManager>()?.OnPlaced(Pos);
                }, 50);
            }
        }

        /// <summary>
        /// Positions currently being torn down as part of a span-removal cascade. Re-entrant
        /// <see cref="OnBlockRemoved"/> calls for these positions skip the cascade so each segment
        /// is only processed once.
        /// </summary>
        private static readonly HashSet<BlockPos> SpanRemovalGuard = new HashSet<BlockPos>();

        public override void OnBlockRemoved()
        {
            DisposeAnimationRenderer();
            base.OnBlockRemoved();
            if (Api?.Side != EnumAppSide.Server) return;
            if (Direction == null) return;

            DumpAllItems();

            // Drops are computed in code (blocktype's `drops` is empty) so endpoints can return a
            // shaft instead of a belt item.
            DropOwnLoot();

            if (SpanRemovalGuard.Contains(Pos)) return;

            Vec3i fwd = BlockBelt.HeadOffset(Direction);
            var positions = new List<BlockPos>();
            CollectInDirection(positions, fwd);
            CollectInDirection(positions, new Vec3i(-fwd.X, 0, -fwd.Z));

            foreach (var p in positions) SpanRemovalGuard.Add(p);
            try
            {
                foreach (var p in positions)
                {
                    Api.World.BlockAccessor.BreakBlock(p, null);
                }
            }
            finally
            {
                foreach (var p in positions) SpanRemovalGuard.Remove(p);
            }
        }

        public override void OnBlockUnloaded()
        {
            base.OnBlockUnloaded();
            DisposeAnimationRenderer();
        }

        private void OnServerTick(float dt)
        {
            if (Direction == null) return;
            if (!IsController) return;

            bool changed = CaptureNearbyItems();

            BEBehaviorKinetic kinetic = GetBehavior<BEBehaviorKinetic>();
            float rpm = kinetic?.ActualRPM ?? 0f;
            float velocity = ChainVelocity(rpm);

            if (velocity != 0f && items.Count > 0)
            {
                for (int i = items.Count - 1; i >= 0; i--)
                {
                    items[i].Progress += velocity * dt;

                    if (IsAtBlockedExit(items[i].Progress, velocity, out bool atBlockedHeadEnd))
                    {
                        if (TryHandOffToNeighbor(items[i], atBlockedHeadEnd))
                        {
                            items.RemoveAt(i);
                            changed = true;
                            continue;
                        }

                        // Side-funnels at the head/tail segment should still get a shot at parked
                        // items — otherwise a chest that refuses input would leave the item
                        // unrecoverable even when a perfectly good funnel sits next to the belt end.
                        if (TryTransferToAdjacentFunnel(items[i]))
                        {
                            items.RemoveAt(i);
                            changed = true;
                            continue;
                        }

                        items[i].Progress = atBlockedHeadEnd ? ChainLength - ItemEndStopMargin : ItemEndStopMargin;
                        changed = true;
                        continue;
                    }

                    int stackSizeBeforeTransfer = items[i].Stack?.StackSize ?? 0;
                    if (items[i].Progress >= 0f
                     && items[i].Progress <= ChainLength
                     && TryTransferToAdjacentFunnel(items[i]))
                    {
                        items.RemoveAt(i);
                        changed = true;
                    }
                    else if ((items[i].Stack?.StackSize ?? 0) != stackSizeBeforeTransfer)
                    {
                        changed = true;
                    }
                    else if (items[i].Progress < 0f || items[i].Progress > ChainLength)
                    {
                        bool atHeadEnd = items[i].Progress > ChainLength;
                        if (TryMovePastEnd(items[i], atHeadEnd, velocity))
                        {
                            items.RemoveAt(i);
                        }
                        changed = true;
                    }
                }
            }

            if (changed) MarkDirty(true);
        }

        private void OnClientTick(float dt)
        {
            // Mirror the server-side advance so the renderer interpolates smoothly between
            // dirty syncs. The server is authoritative — its next sync resets progress.
            if (Direction == null) return;
            if (!IsController) return;
            if (items.Count == 0) return;
            BEBehaviorKinetic kinetic = GetBehavior<BEBehaviorKinetic>();
            float rpm = kinetic?.ActualRPM ?? 0f;
            float velocity = ChainVelocity(rpm);
            if (velocity == 0f) return;
            for (int i = 0; i < items.Count; i++)
            {
                items[i].Progress += velocity * dt;
                if (IsAtBlockedExit(items[i].Progress, velocity, out bool atBlockedHeadEnd))
                {
                    items[i].Progress = atBlockedHeadEnd ? ChainLength - ItemEndStopMargin : ItemEndStopMargin;
                }
            }
        }

        /// <summary>Signed progress velocity (units/sec along the chain) for a given signed RPM.</summary>
        public float ChainVelocity(float signedRpm)
        {
            // 60 rpm == 1 block/sec along the belt. Simple game-feel constant; sign carries
            // through from RPM. HeadDirSign maps "positive RPM advances toward head" per variant.
            return signedRpm / 60f * HeadDirSign(Direction);
        }

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
        /// pass skipped because they sat too close to a chain end — they look "stuck" to the
        /// player even though they are real world entities). Routes via the controller for the
        /// chain-item half; the entity sweep runs on the clicked segment's bounding column.
        /// </summary>
        public int ClaimItemsAt(BlockPos clickedSegmentPos, IPlayer player)
        {
            int claimed = 0;

            // 1. Belt-chain items — always handled by the controller.
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
            if (Api.World.BlockAccessor.GetBlockEntity(nextPos) is BEFunnel funnel)
            {
                return funnel.TryAcceptFromBelt(item.Stack);
            }

            BlockEntity nextBe = Api.World.BlockAccessor.GetBlockEntity(nextPos);

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
                // the second segment of THIS chain — and the BEBelt target path would happily
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

        private bool TryTransferToAdjacentFunnel(BeltItem item)
        {
            if (item?.Stack == null || item.Stack.StackSize <= 0) return true;
            BlockPos beltPos = SegmentPosForProgress(item.Progress);

            foreach (BlockFacing face in BlockFacing.ALLFACES)
            {
                BlockPos neighbor = beltPos.AddCopy(face);
                if (Api.World.BlockAccessor.GetBlockEntity(neighbor) is not BEFunnel funnel) continue;
                if (funnel.OutputFacing == face.Opposite) continue;

                int before = item.Stack.StackSize;
                bool fullyAccepted = funnel.TryAcceptFromBelt(item.Stack);
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

        private void DisposeAnimationRenderer()
        {
            if (Api is ICoreClientAPI capi && animationRenderer != null)
            {
                capi.Event.UnregisterRenderer(animationRenderer, EnumRenderStage.Opaque);
                animationRenderer.Dispose();
                animationRenderer = null;
            }
        }

        /// <summary>
        /// Drop logic per part: middle belts drop a belt item; endpoints drop a shaft (since the
        /// endpoint replaced the original shaft when the span was placed); a middle with an
        /// inserted shaft drops both a belt and the inserted shaft.
        /// </summary>
        private void DropOwnLoot()
        {
            Vec3d center = Pos.ToVec3d().Add(0.5, 0.5, 0.5);
            switch (Part)
            {
                case EnumBeltPart.Middle:
                    SpawnBeltItem(center);
                    if (HasShaft) SpawnShaftItem(center, InsertedShaftAxis ?? "y");
                    break;
                default:
                    // Start, End, Solo: this position originally held a shaft on the perpendicular
                    // axis. Restore that exact shaft as a drop.
                    string pulleyAxis = BlockBelt.TravelAxis(Direction) == EnumKineticAxis.X ? "z" : "x";
                    SpawnShaftItem(center, pulleyAxis);
                    break;
            }
        }

        private void SpawnBeltItem(Vec3d at)
        {
            Item beltItem = Api.World.GetItem(new AssetLocation("vintagekinematics", "belt"));
            if (beltItem == null) return;
            Api.World.SpawnItemEntity(new ItemStack(beltItem), at);
        }

        private void SpawnShaftItem(Vec3d at, string axis)
        {
            Block shaftBlock = Api.World.GetBlock(new AssetLocation("vintagekinematics", "shaft-" + axis));
            if (shaftBlock == null) return;
            Api.World.SpawnItemEntity(new ItemStack(shaftBlock), at);
        }

        private void CollectInDirection(List<BlockPos> output, Vec3i step)
        {
            BlockPos cursor = Pos.AddCopy(step.X, step.Y, step.Z);
            int safety = MaxChainLength;
            while (safety-- > 0)
            {
                if (Api.World.BlockAccessor.GetBlockEntity(cursor) is BEBelt other
                    && other.Direction == Direction)
                {
                    output.Add(cursor.Copy());
                    cursor = cursor.AddCopy(step.X, step.Y, step.Z);
                }
                else break;
            }
        }

        /// <summary>Called by <see cref="Blocks.BlockBelt.OnNeighbourBlockChange"/>.</summary>
        public void OnAxisNeighborChanged(BlockPos neighbor)
        {
            if (Api?.Side != EnumAppSide.Server) return;
            if (Direction == null) return;
            Vec3i fwd = BlockBelt.HeadOffset(Direction);
            BlockPos forward = Pos.AddCopy(fwd.X, 0, fwd.Z);
            BlockPos backward = Pos.AddCopy(-fwd.X, 0, -fwd.Z);
            if (neighbor.Equals(forward) || neighbor.Equals(backward))
            {
                RebuildChain();
            }
        }

        private void NotifyBeltAt(BlockPos pos)
        {
            if (Api?.World?.BlockAccessor.GetBlockEntity(pos) is BEBelt belt)
            {
                belt.RebuildChain();
            }
        }

        /// <summary>
        /// Walks backward to find the chain start, then forward to assign indices. Each segment
        /// writes its own state (controllerPos, index, chainLength, part). Side-effect: marks
        /// all visited segments dirty so the data syncs to clients.
        /// </summary>
        private void RebuildChain()
        {
            if (Direction == null) return;
            Vec3i fwd = BlockBelt.HeadOffset(Direction);

            // 1. Walk backward to find the start.
            BlockPos start = Pos.Copy();
            int safetyBack = MaxChainLength;
            while (safetyBack-- > 0)
            {
                BlockPos behind = start.AddCopy(-fwd.X, 0, -fwd.Z);
                if (Api.World.BlockAccessor.GetBlockEntity(behind) is BEBelt prev
                    && prev.Direction == Direction)
                {
                    start = behind;
                }
                else break;
            }

            // If our controller identity is about to change (different start), drop any
            // owned items at their current world position so nothing is silently lost.
            if (IsController && items.Count > 0 && !start.Equals(Pos))
            {
                DumpAllItems();
            }

            // 2. Walk forward from start, capping at MaxChainLength, assigning indices.
            BlockPos cursor = start.Copy();
            int idx = 0;
            BEBelt prevSeg = null;
            while (idx < MaxChainLength)
            {
                if (Api.World.BlockAccessor.GetBlockEntity(cursor) is not BEBelt seg
                    || seg.Direction != Direction) break;

                seg.ControllerPos = start.Copy();
                seg.IndexInChain = idx;
                if (prevSeg != null) prevSeg.MarkDirty(true);
                prevSeg = seg;
                cursor = cursor.AddCopy(fwd.X, 0, fwd.Z);
                idx++;
            }
            int length = idx;

            // 3. Walk through again to write ChainLength + Part now that length is known.
            cursor = start.Copy();
            for (int i = 0; i < length; i++)
            {
                if (Api.World.BlockAccessor.GetBlockEntity(cursor) is not BEBelt seg) break;
                seg.ChainLength = length;
                seg.Part = length == 1 ? EnumBeltPart.Solo
                          : i == 0 ? EnumBeltPart.Start
                          : i == length - 1 ? EnumBeltPart.End
                          : EnumBeltPart.Middle;
                seg.UpdateKineticState(triggerRebuild: true);
                seg.MarkDirty(true);
                cursor = cursor.AddCopy(fwd.X, 0, fwd.Z);
            }
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            if (ControllerPos != null)
            {
                tree.SetInt("ctlX", ControllerPos.X);
                tree.SetInt("ctlY", ControllerPos.Y);
                tree.SetInt("ctlZ", ControllerPos.Z);
            }
            tree.SetInt("idx", IndexInChain);
            tree.SetInt("len", ChainLength);
            tree.SetInt("part", (int)Part);
            tree.SetBool("hasShaft", HasShaft);
            tree.SetString("shaftAxis", InsertedShaftAxis ?? "");

            tree.SetInt("itemCount", items.Count);
            for (int i = 0; i < items.Count; i++)
            {
                tree["it" + i] = items[i].ToTree();
            }
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
        {
            base.FromTreeAttributes(tree, worldForResolving);
            if (tree.HasAttribute("ctlX"))
            {
                ControllerPos = new BlockPos(tree.GetInt("ctlX"), tree.GetInt("ctlY"), tree.GetInt("ctlZ"));
            }
            IndexInChain = tree.GetInt("idx", 0);
            ChainLength = tree.GetInt("len", 1);
            Part = (EnumBeltPart)tree.GetInt("part", 0);
            HasShaft = tree.GetBool("hasShaft", false);
            InsertedShaftAxis = tree.GetString("shaftAxis", "");
            if (string.IsNullOrEmpty(InsertedShaftAxis)) InsertedShaftAxis = null;
            Direction = Block?.Variant?["direction"];
            UpdateKineticState(triggerRebuild: false);

            items.Clear();
            int itemCount = tree.GetInt("itemCount", 0);
            for (int i = 0; i < itemCount; i++)
            {
                if (tree["it" + i] is ITreeAttribute sub)
                {
                    BeltItem bi = BeltItem.FromTree(sub, worldForResolving);
                    if (bi != null) items.Add(bi);
                }
            }
        }

        /// <summary>
        /// Drives the kinetic node's <see cref="BEBehaviorKinetic.Role"/> and
        /// <see cref="BEBehaviorKinetic.Axis"/> from the segment's current Part / HasShaft.
        /// Endpoints (Solo/Start/End) and Middle+HasShaft expose a Shaft-role kinetic port so the
        /// network builder couples them to neighbouring shafts via the default coaxial rule;
        /// Middle without an inserted shaft is Custom (defers to <see cref="BlockBelt.TryConnect"/>)
        /// so an unrelated perpendicular shaft running past it doesn't bleed into the belt's network.
        /// When <paramref name="triggerRebuild"/> is true and the values changed, the network manager
        /// is asked to retopologize this position.
        /// </summary>
        private void UpdateKineticState(bool triggerRebuild)
        {
            if (string.IsNullOrEmpty(Direction)) return;
            var kinetic = GetBehavior<BEBehaviorKinetic>();
            if (kinetic == null) return;

            EnumKineticAxis pulleyAxis = BlockBelt.TravelAxis(Direction) == EnumKineticAxis.X
                ? EnumKineticAxis.Z
                : EnumKineticAxis.X;

            // Axis is always pulleyAxis: the visual shaft (whether the endpoint pulley axle or the
            // mid-chain inserted shaft via belt-shaft.json) is locked to that axis by the shape.
            // Driving kinetic.Axis with anything else would make the renderer spin the visual on
            // the wrong axis (the inserted shaft would rotate like a gear instead of rolling).
            EnumKineticAxis newAxis = pulleyAxis;
            EnumKineticRole newRole = (Part == EnumBeltPart.Middle && !HasShaft)
                ? EnumKineticRole.Custom
                : EnumKineticRole.Shaft;

            bool changed = kinetic.Role != newRole || kinetic.Axis != newAxis;
            kinetic.Role = newRole;
            kinetic.Axis = newAxis;

            if (changed && triggerRebuild && Api?.Side == EnumAppSide.Server)
            {
                var mgr = Api.ModLoader.GetModSystem<KineticNetworkManager>();
                if (mgr != null)
                {
                    mgr.OnRemoved(Pos);
                    mgr.OnPlaced(Pos);
                }
            }
        }

        /// <summary>
        /// Pick a shape per chain part and shaft state, rotated to match the belt's direction.
        /// Solo and End use the end shape with travel-direction-facing pulley wrap; Start uses the
        /// same shape rotated 180° so the wrap appears at the back. Middle uses the open
        /// top-and-bottom shape, or the shaft-through variant when <see cref="HasShaft"/> is set.
        /// </summary>
        public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tessThreadTesselator)
        {
            if (Direction == null) return false;
            ICoreClientAPI capi = Api as ICoreClientAPI;
            if (capi == null) return false;

            string shapePath = Part switch
            {
                EnumBeltPart.Middle when HasShaft => "vintagekinematics:shapes/block/belt-shaft.json",
                EnumBeltPart.Middle              => "vintagekinematics:shapes/block/belt-middle.json",
                _                                => "vintagekinematics:shapes/block/belt-end.json"
            };

            Shape shape = Shape.TryGet(capi, new AssetLocation(shapePath));
            if (shape == null) return false;
            if (Part != EnumBeltPart.Middle || HasShaft)
            {
                shape = KineticMeshSplitter.CloneAndRemoveElements(shape, new[] { "Shaft" });
            }

            int baseRot = Direction switch
            {
                "n" => 0,
                "e" => 270,
                "s" => 180,
                "w" => 90,
                _   => 0
            };
            // Shape is authored with the belt strips extending from the shaft toward +Z — i.e.
            // toward the chain side when this is an End block at the head. Start (at the tail)
            // needs a 180° flip so its strips face -Z toward the chain instead.
            if (Part == EnumBeltPart.Start)
            {
                baseRot = (baseRot + 180) % 360;
            }

            tessThreadTesselator.TesselateShape(Block, shape, out MeshData mesh, new Vec3f(0, baseRot, 0));
            if (mesh != null) mesher.AddMeshData(mesh);
            return true;
        }

        public override void GetBlockInfo(IPlayer forPlayer, System.Text.StringBuilder dsc)
        {
            base.GetBlockInfo(forPlayer, dsc);
            dsc.Append("Belt ").Append(Direction).Append(" — ").Append(Part)
               .Append(" (").Append(IndexInChain + 1).Append('/').Append(ChainLength).Append(')')
               .AppendLine();
        }
    }
}
