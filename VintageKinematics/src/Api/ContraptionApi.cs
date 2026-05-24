using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using VintageKinematics.Entities;

#pragma warning disable CS0618
namespace VintageKinematics.Api
{
    /// <summary>
    /// Shared helpers for controllers that assemble blocks into EntityVKContraption.
    /// Controllers still own movement rules, placement rules, and their own anchor state.
    /// </summary>
    public static class ContraptionApi
    {
        public static readonly AssetLocation DefaultContraptionEntityCode = new AssetLocation("vintagekinematics", "contraption");

        public static void NormalizeBounds(ref Vec3i min, ref Vec3i max)
        {
            min ??= new Vec3i(0, 0, 0);
            max ??= min.Clone();

            int minX = Math.Min(min.X, max.X);
            int minY = Math.Min(min.Y, max.Y);
            int minZ = Math.Min(min.Z, max.Z);
            int maxX = Math.Max(min.X, max.X);
            int maxY = Math.Max(min.Y, max.Y);
            int maxZ = Math.Max(min.Z, max.Z);

            min.Set(minX, minY, minZ);
            max.Set(maxX, maxY, maxZ);
        }

        public static void IncludeOffsetInBounds(ref Vec3i min, ref Vec3i max, Vec3i offset)
        {
            if (offset == null) return;

            NormalizeBounds(ref min, ref max);
            min.Set(Math.Min(min.X, offset.X), Math.Min(min.Y, offset.Y), Math.Min(min.Z, offset.Z));
            max.Set(Math.Max(max.X, offset.X), Math.Max(max.Y, offset.Y), Math.Max(max.Z, offset.Z));
        }

        public static int CountBlocks(Vec3i min, Vec3i max)
        {
            NormalizeBounds(ref min, ref max);
            return (max.X - min.X + 1) * (max.Y - min.Y + 1) * (max.Z - min.Z + 1);
        }

        public static ContraptionSnapshot CaptureSnapshot(
            IWorldAccessor world,
            BlockPos controllerPos,
            Vec3i localMin,
            Vec3i localMax,
            System.Func<Block, bool> shouldExclude = null)
        {
            NormalizeBounds(ref localMin, ref localMax);

            List<Vec3i> offsets = new List<Vec3i>();
            List<string> blockCodes = new List<string>();
            List<TreeAttribute> blockEntityTrees = new List<TreeAttribute>();

            for (int y = localMin.Y; y <= localMax.Y; y++)
            {
                for (int z = localMin.Z; z <= localMax.Z; z++)
                {
                    for (int x = localMin.X; x <= localMax.X; x++)
                    {
                        BlockPos blockPos = new BlockPos(controllerPos.X + x, controllerPos.InternalY + y, controllerPos.Z + z, controllerPos.dimension);
                        Block block = world.BlockAccessor.GetBlock(blockPos);
                        if (block == null || block.Id == 0 || block.Code == null) continue;
                        if (shouldExclude != null && shouldExclude(block)) continue;

                        offsets.Add(new Vec3i(x, y, z));
                        blockCodes.Add(block.Code.ToString());
                        blockEntityTrees.Add(CaptureBlockEntityTree(world, blockPos));
                    }
                }
            }

            return new ContraptionSnapshot
            {
                LocalMin = localMin.Clone(),
                LocalMax = localMax.Clone(),
                Offsets = offsets.ToArray(),
                BlockCodes = blockCodes.ToArray(),
                BlockEntityTrees = blockEntityTrees.ToArray()
            };
        }

        public static int PruneDisconnected(ContraptionSnapshot snapshot, params Vec3i[] seedOffsets)
        {
            if (snapshot == null)
            {
                return 0;
            }

            Vec3i[] offsets = snapshot.Offsets;
            string[] blockCodes = snapshot.BlockCodes;
            TreeAttribute[] blockEntityTrees = snapshot.BlockEntityTrees;
            int removed = PruneDisconnected(ref offsets, ref blockCodes, ref blockEntityTrees, seedOffsets);

            snapshot.Offsets = offsets;
            snapshot.BlockCodes = blockCodes;
            snapshot.BlockEntityTrees = blockEntityTrees;
            return removed;
        }

        public static int PruneDisconnected(ref Vec3i[] offsets, ref string[] blockCodes, ref TreeAttribute[] blockEntityTrees, params Vec3i[] seedOffsets)
        {
            if (offsets == null || blockCodes == null)
            {
                offsets = Array.Empty<Vec3i>();
                blockCodes = Array.Empty<string>();
                blockEntityTrees = Array.Empty<TreeAttribute>();
                return 0;
            }

            int count = Math.Min(offsets.Length, blockCodes.Length);
            blockEntityTrees = NormalizeBlockEntityTrees(blockEntityTrees, count);
            if (count <= 1)
            {
                Array.Resize(ref offsets, count);
                Array.Resize(ref blockCodes, count);
                Array.Resize(ref blockEntityTrees, count);
                return 0;
            }

            Dictionary<string, int> indexByOffset = new Dictionary<string, int>();
            for (int i = 0; i < count; i++)
            {
                Vec3i offset = offsets[i];
                if (offset == null) continue;
                indexByOffset[OffsetKey(offset.X, offset.Y, offset.Z)] = i;
            }

            bool[] keep = FindConnectedComponent(offsets, count, indexByOffset, seedOffsets);
            int keptCount = CountKept(keep);
            if (keptCount == count && count == offsets.Length && count == blockCodes.Length) return 0;

            Vec3i[] keptOffsets = new Vec3i[keptCount];
            string[] keptCodes = new string[keptCount];
            TreeAttribute[] keptTrees = new TreeAttribute[keptCount];
            int write = 0;
            for (int i = 0; i < count; i++)
            {
                if (!keep[i]) continue;
                keptOffsets[write] = offsets[i];
                keptCodes[write] = blockCodes[i];
                keptTrees[write] = blockEntityTrees[i] ?? new TreeAttribute();
                write++;
            }

            offsets = keptOffsets;
            blockCodes = keptCodes;
            blockEntityTrees = keptTrees;
            return count - keptCount;
        }

        public static bool TrySpawnContraption(
            ICoreAPI api,
            BlockPos controllerPos,
            ContraptionSnapshot snapshot,
            ContraptionPlacementMode placementMode,
            out EntityVKContraption entity,
            AssetLocation entityCode = null)
        {
            entity = null;
            if (api?.World == null || controllerPos == null || snapshot == null || snapshot.Count == 0) return false;

            EntityProperties entityType = api.World.GetEntityType(entityCode ?? DefaultContraptionEntityCode);
            if (entityType == null) return false;

            if (api.World.ClassRegistry.CreateEntity(entityType) is not EntityVKContraption contraption) return false;

            Vec3i min = snapshot.LocalMin?.Clone();
            Vec3i max = snapshot.LocalMax?.Clone();
            NormalizeBounds(ref min, ref max);
            Vec3d spawnPos = GetEntityOrigin(controllerPos, min, max);

            contraption.Pos.SetPosWithDimension(spawnPos);
            contraption.ServerPos.SetFrom(contraption.Pos);
            contraption.PreviousServerPos.SetFrom(contraption.Pos);
            contraption.PositionBeforeFalling.Set(spawnPos.X, spawnPos.Y, spawnPos.Z);
            contraption.Configure(
                controllerPos,
                min,
                max,
                snapshot.Offsets,
                snapshot.BlockCodes,
                NormalizeBlockEntityTrees(snapshot.BlockEntityTrees, snapshot.BlockCodes?.Length ?? 0),
                snapshot.Count,
                placementMode);

            api.World.SpawnEntity(contraption);
            entity = contraption;
            return true;
        }

        public static void RemoveSnapshotBlocksFromWorld(IWorldAccessor world, BlockPos controllerPos, ContraptionSnapshot snapshot)
        {
            if (world == null || controllerPos == null || snapshot?.Offsets == null) return;

            for (int i = 0; i < snapshot.Offsets.Length; i++)
            {
                BlockPos blockPos = WorldPosFromOffset(controllerPos, snapshot.Offsets[i]);
                world.BlockAccessor.SetBlock(0, blockPos);
                world.BlockAccessor.MarkBlockDirty(blockPos);
            }
        }

        public static BlockPos WorldPosFromOffset(BlockPos controllerPos, Vec3i offset)
        {
            return new BlockPos(
                controllerPos.X + offset.X,
                (controllerPos.InternalY + offset.Y) % BlockPos.DimensionBoundary,
                controllerPos.Z + offset.Z,
                controllerPos.dimension);
        }

        public static Vec3d GetEntityOrigin(BlockPos controllerPos, Vec3i localMin, Vec3i localMax)
        {
            NormalizeBounds(ref localMin, ref localMax);
            double width = localMax.X - localMin.X + 1;
            double depth = localMax.Z - localMin.Z + 1;
            return controllerPos.ToVec3d().Add(localMin.X + width / 2.0, localMin.Y, localMin.Z + depth / 2.0);
        }

        public static TreeAttribute CaptureBlockEntityTree(IWorldAccessor world, BlockPos blockPos)
        {
            BlockEntity be = world.BlockAccessor.GetBlockEntity(blockPos);
            if (be == null) return new TreeAttribute();

            TreeAttribute tree = new TreeAttribute();
            be.ToTreeAttributes(tree);
            return tree;
        }

        public static TreeAttribute[] NormalizeBlockEntityTrees(TreeAttribute[] trees, int count)
        {
            if (count <= 0) return Array.Empty<TreeAttribute>();

            TreeAttribute[] normalized = new TreeAttribute[count];
            for (int i = 0; i < count; i++)
            {
                normalized[i] = i < (trees?.Length ?? 0) && trees[i] != null
                    ? trees[i]
                    : new TreeAttribute();
            }
            return normalized;
        }

        public static TreeAttribute[] CloneBlockEntityTrees(TreeAttribute[] trees, int count)
        {
            TreeAttribute[] normalized = NormalizeBlockEntityTrees(trees, count);
            TreeAttribute[] cloned = new TreeAttribute[normalized.Length];
            for (int i = 0; i < normalized.Length; i++)
            {
                cloned[i] = normalized[i].Clone() as TreeAttribute ?? new TreeAttribute();
            }
            return cloned;
        }

        private static bool[] FindConnectedComponent(Vec3i[] offsets, int count, Dictionary<string, int> indexByOffset, Vec3i[] seedOffsets)
        {
            bool[] keep = new bool[count];
            Queue<int> open = new Queue<int>();

            if (seedOffsets == null || seedOffsets.Length == 0)
            {
                seedOffsets = new[] { new Vec3i(0, 0, 0) };
            }

            for (int i = 0; i < count; i++)
            {
                if (!IsSeedOrAdjacentToSeed(offsets[i], seedOffsets)) continue;
                keep[i] = true;
                open.Enqueue(i);
            }

            while (open.Count > 0)
            {
                VisitNeighbors(offsets, open.Dequeue(), indexByOffset, keep, open);
            }

            return keep;
        }

        private static bool IsSeedOrAdjacentToSeed(Vec3i offset, Vec3i[] seedOffsets)
        {
            if (offset == null) return false;

            for (int i = 0; i < seedOffsets.Length; i++)
            {
                Vec3i seed = seedOffsets[i];
                if (seed == null) continue;
                int dx = offset.X - seed.X;
                int dy = offset.Y - seed.Y;
                int dz = offset.Z - seed.Z;
                if (dx == 0 && dy == 0 && dz == 0) return true;
                if (Math.Abs(dx) + Math.Abs(dy) + Math.Abs(dz) == 1) return true;
            }

            return false;
        }

        private static void VisitNeighbors(Vec3i[] offsets, int index, Dictionary<string, int> indexByOffset, bool[] keep, Queue<int> open)
        {
            Vec3i offset = offsets[index];
            if (offset == null) return;

            TryVisit(offset.X + 1, offset.Y, offset.Z, indexByOffset, keep, open);
            TryVisit(offset.X - 1, offset.Y, offset.Z, indexByOffset, keep, open);
            TryVisit(offset.X, offset.Y + 1, offset.Z, indexByOffset, keep, open);
            TryVisit(offset.X, offset.Y - 1, offset.Z, indexByOffset, keep, open);
            TryVisit(offset.X, offset.Y, offset.Z + 1, indexByOffset, keep, open);
            TryVisit(offset.X, offset.Y, offset.Z - 1, indexByOffset, keep, open);
        }

        private static void TryVisit(int x, int y, int z, Dictionary<string, int> indexByOffset, bool[] keep, Queue<int> open)
        {
            if (!indexByOffset.TryGetValue(OffsetKey(x, y, z), out int index)) return;
            if (keep[index]) return;

            keep[index] = true;
            open.Enqueue(index);
        }

        private static int CountKept(bool[] keep)
        {
            int count = 0;
            for (int i = 0; i < keep.Length; i++)
            {
                if (keep[i]) count++;
            }
            return count;
        }

        private static string OffsetKey(int x, int y, int z)
        {
            return x + "," + y + "," + z;
        }
    }
}
#pragma warning restore CS0618
