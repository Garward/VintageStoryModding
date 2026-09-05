using System;
using System.Collections.Generic;
using System.Reflection;
using VRPG.Worlds.Generation.Onboarding;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace VRPG.Tests;

public sealed class TavernVoxelAdapterTests
{
    [Theory]
    [InlineData(27, 19, 6)]
    [InlineData(-40, -34, 42)]
    public void VoxelRoundTripCrossesChunksAndKeepsDimension(int x, int z, int dimension)
    {
        Dictionary<string, Block> blocks = Blocks();
        var palette = new TavernBlockPalette(code => blocks.GetValueOrDefault(code.ToString()));
        IBlockAccessor accessor = DispatchProxy.Create<IBlockAccessor, AccessorProxy>();
        var proxy = (AccessorProxy)accessor;
        proxy.Registry = blocks;
        var adapter = new TavernVoxelAdapter(accessor, (id, position) =>
        {
            Assert.Equal(dimension, position.dimension);
            Assert.InRange(position.X, x, x + TavernLayout.Width - 1);
            Assert.InRange(position.Z, z, z + TavernLayout.Depth - 1);
            proxy.Voxels[(position.X, position.Y, position.Z, position.dimension)] = id;
        }, palette, new BlockPos(x, 24, z, dimension));
        TavernLayout layout = TavernGenerator.Generate(7);
        foreach (TavernCell cell in layout.Cells()) adapter.Write(cell, layout.GetBlock(cell.X, cell.Y, cell.Z));
        Assert.Empty(TavernStructuralValidator.Validate(layout, adapter.Read));
        Assert.Throws<ArgumentOutOfRangeException>(() => adapter.Write(new(-1, 0, 0), TavernBlock.Stone));
        proxy.HasFluid = true;
        Assert.Equal((TavernBlock)(-1), adapter.Read(9, 2, 3));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("top-slab")]
    [InlineData("full-slab")]
    [InlineData("loot")]
    [InlineData("container")]
    [InlineData("fire")]
    public void InvalidContentFailsBeforeAnyWorldWrite(string problem)
    {
        Dictionary<string, Block> blocks = Blocks();
        Block slab = blocks["vrpg:tavern-slab"];
        if (problem == "missing") blocks.Remove("vrpg:tavern-slab");
        if (problem == "top-slab") slab.CollisionBoxes = new[] { new Cuboidf(0, 0.5f, 0, 1, 1, 1) };
        if (problem == "full-slab") slab.CollisionBoxes = new[] { new Cuboidf(0, 0, 0, 1, 1, 1) };
        if (problem == "loot") slab.Drops = new[] { new BlockDropItemStack() };
        if (problem == "container") slab.EntityClass = "Chest";
        if (problem == "fire") slab.CombustibleProps = new CombustibleProperties();
        Assert.Throws<InvalidOperationException>(() =>
            new TavernBlockPalette(code => blocks.GetValueOrDefault(code.ToString())));
    }

    private static Dictionary<string, Block> Blocks()
    {
        var result = new Dictionary<string, Block>();
        foreach (string material in new[] { "stone", "timber", "slab", "lamp" })
        {
            string code = "vrpg:tavern-" + material;
            result.Add(code, new Block
            {
                BlockId = 101 + result.Count,
                Code = new AssetLocation(code),
                Drops = Array.Empty<BlockDropItemStack>(),
                CollisionBoxes = new[] { new Cuboidf(0, 0, 0, 1, material == "slab" ? 0.5f : 1, 1) }
            });
        }
        return result;
    }

    public class AccessorProxy : DispatchProxy
    {
        public Dictionary<string, Block> Registry { get; set; } = new();
        public Dictionary<(int, int, int, int), int> Voxels { get; } = new();
        public bool HasFluid { get; set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == "GetBlockEntity") return null;
            if (targetMethod?.Name == "GetBlock" && args is [BlockPos position, int layer])
            {
                int id = layer == BlockLayersAccess.Fluid
                    ? (HasFluid ? 999 : 0)
                    : Voxels.GetValueOrDefault((position.X, position.Y, position.Z, position.dimension));
                foreach (Block block in Registry.Values)
                    if (block.Id == id) return block;
                return new Block { BlockId = id };
            }
            throw new NotSupportedException(targetMethod?.Name);
        }
    }
}
