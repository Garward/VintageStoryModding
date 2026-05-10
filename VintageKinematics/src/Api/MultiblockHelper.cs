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
        // Returns the BE at pos, redirecting through a vanilla BlockMultiblock filler to its
        // controller cell when applicable. Null if pos has neither a BE nor a multiblock filler.
        public static BlockEntity GetMultiblockAwareBE(IWorldAccessor world, BlockPos pos)
        {
            if (world == null || pos == null) return null;
            BlockEntity be = world.BlockAccessor.GetBlockEntity(pos);
            if (be != null) return be;

            if (world.BlockAccessor.GetBlock(pos) is BlockMultiblock mb)
            {
                BlockPos ctrl = new BlockPos(pos.X + mb.OffsetInv.X, pos.Y + mb.OffsetInv.Y, pos.Z + mb.OffsetInv.Z, pos.dimension);
                return world.BlockAccessor.GetBlockEntity(ctrl);
            }
            return null;
        }
    }
}
