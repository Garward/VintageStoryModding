using System;
using System.Linq;
using VRPG.Worlds.Generation.Onboarding;
using Xunit;

namespace VRPG.Tests;

public sealed class TavernGenerationTests
{
    [Fact]
    public void SeedCorpusHasDeterministicSafeConnectedGeometry()
    {
        for (long seed = -512; seed < 512; seed++)
        {
            TavernLayout layout = TavernGenerator.Generate(seed);
            TavernLayout repeated = TavernGenerator.Generate(seed);
            Assert.Empty(TavernStructuralValidator.Validate(layout));
            Assert.Equal(layout.Surfaces, repeated.Surfaces);
            Assert.Equal(layout.Anchors, repeated.Anchors);
            Assert.Equal(layout.Cells().Select(p => layout.GetBlock(p.X, p.Y, p.Z)),
                repeated.Cells().Select(p => repeated.GetBlock(p.X, p.Y, p.Z)));
        }
    }

    [Theory]
    [InlineData(9, 1, 3, TavernBlock.Air, "walk-surface")]
    [InlineData(9, 0, 3, TavernBlock.Air, "floor-support")]
    [InlineData(9, 2, 3, TavernBlock.Stone, "head-clearance")]
    [InlineData(9, 3, 7, TavernBlock.Timber, "head-clearance")]
    [InlineData(0, 4, 4, TavernBlock.Air, "open-envelope")]
    [InlineData(9, 8, 4, TavernBlock.Air, "open-envelope")]
    [InlineData(9, 1, 16, TavernBlock.Air, "unsupported-slab")]
    [InlineData(9, 2, 16, TavernBlock.Stone, "walk-surface")]
    [InlineData(9, 4, 16, TavernBlock.Timber, "head-clearance")]
    [InlineData(9, 2, 20, TavernBlock.Air, "walk-surface")]
    [InlineData(9, 2, 5, (TavernBlock)99, "unknown-block")]
    public void DamagedWorldCannotPassValidation(int x, int y, int z, TavernBlock replacement, string code)
    {
        TavernLayout layout = TavernGenerator.Generate(17);
        var issues = TavernStructuralValidator.Validate(layout,
            (bx, by, bz) => bx == x && by == y && bz == z ? replacement : layout.GetBlock(bx, by, bz));
        Assert.Contains(issues, issue => issue.Code == code);
    }

    [Fact]
    public void ExitUsesWideSupportedBottomSlabGradeAndDistinctRecoveryLanding()
    {
        TavernLayout layout = TavernGenerator.Generate(long.MinValue);
        for (int x = 1; x < TavernLayout.Width - 1; x++)
        {
            Assert.Equal(4, layout.Surfaces.Single(s => s.X == x && s.Z == 15).HalfHeight);
            Assert.Equal(5, layout.Surfaces.Single(s => s.X == x && s.Z == 16).HalfHeight);
            Assert.Equal(6, layout.Surfaces.Single(s => s.X == x && s.Z == 17).HalfHeight);
            Assert.Equal(TavernBlock.Stone, layout.GetBlock(x, 1, 16));
            Assert.Equal(TavernBlock.BottomSlab, layout.GetBlock(x, 2, 16));
        }
        Assert.NotEqual(layout.GetAnchor("arrival"), layout.GetAnchor("recovery"));
        Assert.Equal(2, layout.GetAnchor("arrival").FeetY);
        Assert.Equal(3, layout.GetAnchor("exit").FeetY);
        Assert.Equal(9.5, layout.GetAnchor("arrival").CenterX);
    }
}
