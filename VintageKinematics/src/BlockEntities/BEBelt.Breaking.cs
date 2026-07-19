using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using VintageKinematics.Api;
using VintageKinematics.Blocks;

namespace VintageKinematics.BlockEntities
{
    public partial class BEBelt
    {
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
            if (Api.World.BlockAccessor.GetBlock(Pos) is BlockBelt) return;

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
                    if (!AutomationClaimUtil.CanAutomatedBlockAccess(Api.World, Pos, p, EnumBlockAccessFlags.BuildOrBreak)) continue;
                    Api.World.BlockAccessor.BreakBlock(p, null);
                }
            }
            finally
            {
                foreach (var p in positions) SpanRemovalGuard.Remove(p);
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
                    if (HasShaft) SpawnShaftItem(center);
                    break;
                default:
                    // Start, End, Solo: this position originally held a shaft on the perpendicular
                    // axis. Drop the canonical shaft item so rotation variants do not leak.
                    SpawnShaftItem(center);
                    break;
            }
        }

        private void SpawnBeltItem(Vec3d at)
        {
            Item beltItem = Api.World.GetItem(new AssetLocation("vintagekinematics", "belt"));
            if (beltItem == null) return;
            Api.World.SpawnItemEntity(new ItemStack(beltItem), at);
        }

        private void SpawnShaftItem(Vec3d at)
        {
            Block shaftBlock = Api.World.GetBlock(new AssetLocation("vintagekinematics", "shaft-y"));
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
    }
}
