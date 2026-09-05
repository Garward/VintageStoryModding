using System;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VRPG.Worlds.Generation.Onboarding;

/// <summary>Dimension-aware voxel IO for a reserved tavern; writes must be supplied by World Mutation.</summary>
public sealed class TavernVoxelAdapter
{
    private readonly IBlockAccessor accessor;
    private readonly Action<int, BlockPos> writeAuthorizedBlock;
    private readonly TavernBlockPalette palette;
    private readonly BlockPos origin;

    public TavernVoxelAdapter(IBlockAccessor accessor, Action<int, BlockPos> writeAuthorizedBlock,
        TavernBlockPalette palette, BlockPos origin)
    {
        this.accessor = accessor ?? throw new ArgumentNullException(nameof(accessor));
        this.writeAuthorizedBlock = writeAuthorizedBlock ?? throw new ArgumentNullException(nameof(writeAuthorizedBlock));
        this.palette = palette ?? throw new ArgumentNullException(nameof(palette));
        this.origin = origin?.Copy() ?? throw new ArgumentNullException(nameof(origin));
        // Fail before the first write for invalid or overflowing placement bounds.
        if (origin.Y < 1) throw new ArgumentOutOfRangeException(nameof(origin));
        _ = Position(TavernLayout.Width - 1, TavernLayout.Height - 1, TavernLayout.Depth - 1);
    }

    public void Write(TavernCell cell, TavernBlock block) =>
        writeAuthorizedBlock(palette.BlockId(block), Position(cell.X, cell.Y, cell.Z));

    public TavernBlock Read(int x, int y, int z)
    {
        BlockPos position = Position(x, y, z);
        if (accessor.GetBlock(position, BlockLayersAccess.Fluid).Id != 0
            || accessor.GetBlockEntity(position) != null) return (TavernBlock)(-1);
        return palette.Classify(accessor.GetBlock(position, BlockLayersAccess.Solid), accessor, position);
    }

    public BlockPos Position(int x, int y, int z)
    {
        if (x < 0 || x >= TavernLayout.Width || y < 0 || y >= TavernLayout.Height
            || z < 0 || z >= TavernLayout.Depth) throw new ArgumentOutOfRangeException(nameof(x));
        return new BlockPos(checked(origin.X + x), checked(origin.Y + y), checked(origin.Z + z), origin.dimension);
    }
}
