using System.Collections.Generic;

namespace VintageKinematics.Storage.Topology
{
    internal static class StorageTopologyOrdering
    {
        public static readonly IComparer<StorageTopologyPosition> Positions =
            Comparer<StorageTopologyPosition>.Create(ComparePositions);
        public static readonly IComparer<StorageTopologyChunk> Chunks =
            Comparer<StorageTopologyChunk>.Create(CompareChunks);

        private static int ComparePositions(StorageTopologyPosition left, StorageTopologyPosition right)
        {
            int result = left.Dimension.CompareTo(right.Dimension);
            if (result != 0) return result;
            result = left.X.CompareTo(right.X);
            if (result != 0) return result;
            result = left.InternalY.CompareTo(right.InternalY);
            return result != 0 ? result : left.Z.CompareTo(right.Z);
        }

        private static int CompareChunks(StorageTopologyChunk left, StorageTopologyChunk right)
        {
            int result = left.Dimension.CompareTo(right.Dimension);
            if (result != 0) return result;
            result = left.X.CompareTo(right.X);
            if (result != 0) return result;
            result = left.Y.CompareTo(right.Y);
            return result != 0 ? result : left.Z.CompareTo(right.Z);
        }
    }
}
