using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VintageKinematics.Connections
{
    /// <summary>Read-only neighbor scan shared by pipe and storage visual connections.</summary>
    public static class WorldFaceConnectionScanner
    {
        public static string Scan(
            IWorldAccessor world,
            BlockPos origin,
            System.Func<BlockFacing, BlockPos, Block, bool> acceptsNeighbor)
        {
            if (world == null || origin == null || acceptsNeighbor == null) return null;

            List<string> connected = new List<string>(6);
            foreach (BlockFacing face in FaceConnectionMask.Faces)
            {
                BlockPos neighborPos = origin.AddCopy(face);
                Block neighbor = world.BlockAccessor.GetBlock(neighborPos);
                if (acceptsNeighbor(face, neighborPos, neighbor))
                {
                    connected.Add(FaceConnectionMask.Code(face));
                }
            }
            return FaceConnectionMask.Sort(connected);
        }
    }
}
