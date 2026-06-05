using System.Reflection;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace VintageKinematics.Api
{
    // Lookups in mod code that should "see through" vanilla multiblock filler cells —
    // e.g. funnel auto-pull, kinetic adjacency, inventory probes — must resolve a filler
    // back to its controller before reading the BE. Single shared helper so behavior is
    // consistent across the codebase.
    public static class MultiblockHelper
    {
        // BlockBehaviorMultiblock keeps its size/cposition fields private; reflect into them
        // once at static-init time so callers can read claim geometry without re-parsing JSON.
        // ControllerPositionRel is public in the shipped DLL while the Size fields are private,
        // so flags must include both Public and NonPublic — otherwise cposition silently reads
        // as null and baseCorner collapses onto controllerPos.
        private const BindingFlags MbFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly FieldInfo MbSizeX = typeof(BlockBehaviorMultiblock).GetField("SizeX", MbFlags);
        private static readonly FieldInfo MbSizeY = typeof(BlockBehaviorMultiblock).GetField("SizeY", MbFlags);
        private static readonly FieldInfo MbSizeZ = typeof(BlockBehaviorMultiblock).GetField("SizeZ", MbFlags);
        private static readonly FieldInfo MbCPos  = typeof(BlockBehaviorMultiblock).GetField("ControllerPositionRel", MbFlags);

        // Returns the BE at pos, redirecting through a vanilla/multiblock filler to its
        // controller cell when applicable. Null if pos has neither a BE nor a multiblock filler.
        public static BlockEntity GetMultiblockAwareBE(IWorldAccessor world, BlockPos pos)
        {
            if (world == null || pos == null) return null;

            Block block = world.BlockAccessor.GetBlock(pos);
            if (block is IMultiblockOffset mb)
            {
                BlockPos ctrl = mb.GetControlBlockPos(pos.Copy());
                return ctrl == null ? null : world.BlockAccessor.GetBlockEntity(ctrl);
            }

            return world.BlockAccessor.GetBlockEntity(pos);
        }

        /// <summary>
        /// Reads the claim geometry of a multiblock from its <see cref="BlockBehaviorMultiblock"/>:
        /// the (0,0,0) corner of the claim in world coords and the per-axis sizes. Returns false if
        /// the block has no Multiblock behavior.
        /// </summary>
        public static bool TryGetClaim(Block block, BlockPos controllerPos, out BlockPos baseCorner, out Vec3i size)
        {
            baseCorner = null;
            size = null;
            BlockBehaviorMultiblock mb = block?.GetBehavior<BlockBehaviorMultiblock>();
            if (mb == null) return false;

            Vec3i cposition = (MbCPos?.GetValue(mb) as Vec3i) ?? new Vec3i(0, 0, 0);
            int sx = (int?)MbSizeX?.GetValue(mb) ?? 1;
            int sy = (int?)MbSizeY?.GetValue(mb) ?? 1;
            int sz = (int?)MbSizeZ?.GetValue(mb) ?? 1;
            baseCorner = new BlockPos(controllerPos.X - cposition.X, controllerPos.Y - cposition.Y, controllerPos.Z - cposition.Z, controllerPos.dimension);
            size = new Vec3i(sx, sy, sz);
            return true;
        }

        public static BlockPos[] CellsOnFace(Block block, BlockPos controllerPos, BlockFacing face)
        {
            if (!TryGetClaim(block, controllerPos, out BlockPos baseCorner, out Vec3i size))
            {
                return new[] { controllerPos };
            }

            var cells = new List<BlockPos>();
            for (int x = 0; x < size.X; x++)
            for (int y = 0; y < size.Y; y++)
            for (int z = 0; z < size.Z; z++)
            {
                if (!IsOnClaimFace(face, x, y, z, size)) continue;
                cells.Add(new BlockPos(baseCorner.X + x, baseCorner.Y + y, baseCorner.Z + z, controllerPos.dimension));
            }
            return cells.ToArray();
        }

        public static BlockFacing PlacementFacingFromVariant(Block block)
        {
            string side = block?.Variant?["side"] ?? "n";
            return side switch
            {
                "e" => BlockFacing.EAST,
                "s" => BlockFacing.SOUTH,
                "w" => BlockFacing.WEST,
                _ => BlockFacing.NORTH
            };
        }

        public static BlockFacing LeftOf(BlockFacing facing)
        {
            if (facing == BlockFacing.NORTH) return BlockFacing.WEST;
            if (facing == BlockFacing.EAST) return BlockFacing.NORTH;
            if (facing == BlockFacing.SOUTH) return BlockFacing.EAST;
            if (facing == BlockFacing.WEST) return BlockFacing.SOUTH;
            return BlockFacing.WEST;
        }

        public static BlockFacing RightOf(BlockFacing facing)
        {
            if (facing == BlockFacing.NORTH) return BlockFacing.EAST;
            if (facing == BlockFacing.EAST) return BlockFacing.SOUTH;
            if (facing == BlockFacing.SOUTH) return BlockFacing.WEST;
            if (facing == BlockFacing.WEST) return BlockFacing.NORTH;
            return BlockFacing.EAST;
        }

        private static bool IsOnClaimFace(BlockFacing face, int x, int y, int z, Vec3i size)
        {
            if (face == BlockFacing.WEST) return x == 0;
            if (face == BlockFacing.EAST) return x == size.X - 1;
            if (face == BlockFacing.DOWN) return y == 0;
            if (face == BlockFacing.UP) return y == size.Y - 1;
            if (face == BlockFacing.NORTH) return z == 0;
            if (face == BlockFacing.SOUTH) return z == size.Z - 1;
            return false;
        }
    }
}
