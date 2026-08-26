using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using VintageKinematics.Api;

namespace VintageKinematics.BlockEntities
{
    /// <summary>
    /// Finds one empty cell on the pump's current horizontal output layer.
    /// It deliberately never searches upward or downward.
    /// </summary>
    internal static class PumpWorldOutput
    {
        private const int MaxSourceCells = 4096;

        public static bool IsPotentialTarget(
            IWorldAccessor world,
            BlockPos target,
            WorldLiquidOutputDefinition liquid)
        {
            return IsSupportedPlacementCandidate(world, target, liquid.LiquidCode)
                || IsMatchingSource(world, target, liquid.LiquidCode);
        }

        public static bool TryFindPlacement(
            IWorldAccessor world,
            BlockPos target,
            WorldLiquidOutputDefinition liquid,
            out BlockPos placement)
        {
            if (IsSupportedPlacementCandidate(world, target, liquid.LiquidCode))
            {
                placement = target.Copy();
                return true;
            }

            if (!IsMatchingSource(world, target, liquid.LiquidCode))
            {
                placement = null;
                return false;
            }

            Queue<BlockPos> queue = new();
            HashSet<string> visited = new();
            queue.Enqueue(target.Copy());
            visited.Add(Key(target));

            while (queue.Count > 0 && visited.Count <= MaxSourceCells)
            {
                BlockPos sourcePos = queue.Dequeue();
                foreach (BlockFacing face in BlockFacing.HORIZONTALS)
                {
                    BlockPos neighbor = sourcePos.AddCopy(face);
                    if (IsSupportedPlacementCandidate(world, neighbor, liquid.LiquidCode))
                    {
                        placement = neighbor;
                        return true;
                    }

                    if (!IsMatchingSource(world, neighbor, liquid.LiquidCode)) continue;
                    if (visited.Add(Key(neighbor))) queue.Enqueue(neighbor);
                }
            }

            placement = null;
            return false;
        }

        public static bool IsSupportedPlacementCandidate(
            IWorldAccessor world,
            BlockPos pos,
            string liquidCode)
        {
            if (world.BlockAccessor.GetBlock(pos, BlockLayersAccess.Solid).BlockId != 0) return false;

            Block fluid = world.BlockAccessor.GetBlock(pos, BlockLayersAccess.Fluid);
            bool empty = fluid.BlockId == 0;
            bool matchingPartial = fluid.Code?.Domain == "game"
                && fluid.LiquidCode == liquidCode
                && fluid.LiquidLevel > 0
                && fluid.LiquidLevel < WorldLiquidPumpPolicy.FullLiquidLevel;
            if (!empty && !matchingPartial) return false;

            BlockPos below = pos.DownCopy();
            Block solidBelow = world.BlockAccessor.GetBlock(below, BlockLayersAccess.Solid);
            if (solidBelow.SideSolid[BlockFacing.UP.Index]) return true;
            return IsMatchingSource(world, below, liquidCode);
        }

        private static bool IsMatchingSource(IWorldAccessor world, BlockPos pos, string expectedLiquidCode)
        {
            Block block = world.BlockAccessor.GetBlock(pos, BlockLayersAccess.FluidOrSolid);
            return WorldLiquidOutputPolicy.IsMatchingFullSource(
                block?.Code?.Domain,
                block?.LiquidCode,
                block?.LiquidLevel ?? 0,
                expectedLiquidCode);
        }

        private static string Key(BlockPos pos)
        {
            return $"{pos.dimension}:{pos.X}:{pos.InternalY}:{pos.Z}";
        }
    }
}
