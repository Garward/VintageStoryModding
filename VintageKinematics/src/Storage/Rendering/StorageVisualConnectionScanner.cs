using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using VintageKinematics.Connections;
using System.Collections.Generic;

namespace VintageKinematics.Storage.Rendering
{
    /// <summary>Visual-only storage neighbor scan; never establishes ownership.</summary>
    public static class StorageVisualConnectionScanner
    {
        public static string Scan(IWorldAccessor world, BlockPos pos)
        {
            return WorldFaceConnectionScanner.Scan(
                world,
                pos,
                (_, _, neighbor) => neighbor?.Attributes?[KineticStorageRemovalService.StorageMemberAttribute]
                    .AsBool(false) == true);
        }

        public static IReadOnlyList<string> ScanConcaveElbows(
            IWorldAccessor world,
            BlockPos pos,
            string faceMask)
        {
            if (world == null || pos == null) return System.Array.Empty<string>();
            return StorageConcaveElbow.Select(faceMask, elbow =>
            {
                BlockPos diagonal = pos.AddCopy(elbow.First).Add(elbow.Second);
                return IsStorageMember(world.BlockAccessor.GetBlock(diagonal));
            });
        }

        private static bool IsStorageMember(Block block)
        {
            return block?.Attributes?[KineticStorageRemovalService.StorageMemberAttribute]
                .AsBool(false) == true;
        }
    }
}
