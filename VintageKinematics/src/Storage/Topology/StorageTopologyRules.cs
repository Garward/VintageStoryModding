using System;
using System.Collections.Generic;

namespace VintageKinematics.Storage.Topology
{
    public static class StorageTopologyRules
    {
        private static readonly int[,] FaceOffsets =
        {
            { 1, 0, 0 },
            { -1, 0, 0 },
            { 0, 1, 0 },
            { 0, -1, 0 },
            { 0, 0, 1 },
            { 0, 0, -1 }
        };

        public static IEnumerable<StorageTopologyPosition> FaceNeighbors(
            StorageTopologyPosition position)
        {
            for (int i = 0; i < FaceOffsets.GetLength(0); i++)
            {
                yield return position.Offset(
                    FaceOffsets[i, 0],
                    FaceOffsets[i, 1],
                    FaceOffsets[i, 2]);
            }
        }

        public static bool AreFaceAdjacent(
            StorageTopologyPosition left,
            StorageTopologyPosition right)
        {
            return left.Dimension == right.Dimension && ManhattanDistance(left, right) == 1;
        }

        public static long ManhattanDistance(
            StorageTopologyPosition left,
            StorageTopologyPosition right)
        {
            if (left.Dimension != right.Dimension) return long.MaxValue;
            return Math.Abs((long)left.X - right.X)
                + Math.Abs((long)left.InternalY - right.InternalY)
                + Math.Abs((long)left.Z - right.Z);
        }
    }
}
