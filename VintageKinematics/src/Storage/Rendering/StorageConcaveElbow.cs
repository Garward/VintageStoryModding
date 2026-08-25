using System;
using System.Collections.Generic;
using Vintagestory.API.MathTools;
using VintageKinematics.Connections;

namespace VintageKinematics.Storage.Rendering
{
    /// <summary>One perpendicular connection pair and its shared structural edge.</summary>
    public sealed class StorageConcaveElbow
    {
        public static readonly IReadOnlyList<StorageConcaveElbow> All = new[]
        {
            new StorageConcaveElbow("x-down-north", BlockFacing.DOWN, BlockFacing.NORTH),
            new StorageConcaveElbow("x-down-south", BlockFacing.DOWN, BlockFacing.SOUTH),
            new StorageConcaveElbow("x-up-north", BlockFacing.UP, BlockFacing.NORTH),
            new StorageConcaveElbow("x-up-south", BlockFacing.UP, BlockFacing.SOUTH),
            new StorageConcaveElbow("y-west-north", BlockFacing.WEST, BlockFacing.NORTH),
            new StorageConcaveElbow("y-west-south", BlockFacing.WEST, BlockFacing.SOUTH),
            new StorageConcaveElbow("y-east-north", BlockFacing.EAST, BlockFacing.NORTH),
            new StorageConcaveElbow("y-east-south", BlockFacing.EAST, BlockFacing.SOUTH),
            new StorageConcaveElbow("z-down-west", BlockFacing.DOWN, BlockFacing.WEST),
            new StorageConcaveElbow("z-down-east", BlockFacing.DOWN, BlockFacing.EAST),
            new StorageConcaveElbow("z-up-west", BlockFacing.UP, BlockFacing.WEST),
            new StorageConcaveElbow("z-up-east", BlockFacing.UP, BlockFacing.EAST)
        };

        public string Name { get; }
        public BlockFacing First { get; }
        public BlockFacing Second { get; }

        private StorageConcaveElbow(string name, BlockFacing first, BlockFacing second)
        {
            Name = name;
            First = first;
            Second = second;
        }

        public bool HasBothConnections(string faceMask)
        {
            return FaceConnectionMask.Contains(faceMask, FaceConnectionMask.Code(First))
                && FaceConnectionMask.Contains(faceMask, FaceConnectionMask.Code(Second));
        }

        public static IReadOnlyList<string> Select(
            string faceMask,
            Func<StorageConcaveElbow, bool> hasDiagonalCell)
        {
            var selected = new List<string>();
            foreach (StorageConcaveElbow elbow in All)
            {
                if (elbow.HasBothConnections(faceMask) && !hasDiagonalCell(elbow))
                {
                    selected.Add(elbow.Name);
                }
            }
            return selected;
        }
    }
}
