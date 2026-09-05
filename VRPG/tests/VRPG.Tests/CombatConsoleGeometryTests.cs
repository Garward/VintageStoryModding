using VRPG.Client.UI;
using Xunit;

namespace VRPG.Tests;

public sealed class CombatConsoleGeometryTests
{
    [Theory]
    [InlineData(1040, 116)]
    [InlineData(740, 112)]
    [InlineData(320, 112)]
    public void ConsoleKeepsAllPrimaryRegionsInsideItsBounds(double width, double height)
    {
        CombatConsoleGeometry geometry = CombatConsoleGeometry.Resolve(width, height);

        Assert.True(geometry.LeftResourceX >= 0);
        Assert.True(geometry.RightResourceX + geometry.ResourceWidth <= width);
        Assert.True(geometry.HotbarX >= 0);
        Assert.True(geometry.HotbarX + geometry.XpWidth <= width);
        Assert.True(geometry.XpY + geometry.XpHeight <= height);
    }

    [Fact]
    public void NarrowConsoleShrinksSlotsBeforeLeavingTheSafeArea()
    {
        CombatConsoleGeometry normal = CombatConsoleGeometry.Resolve(1040, 116);
        CombatConsoleGeometry narrow = CombatConsoleGeometry.Resolve(600, 112);

        Assert.True(narrow.HotbarSlot < normal.HotbarSlot);
        Assert.True(narrow.ResourceWidth >= 78);
    }

    [Theory]
    [InlineData(1.0, 1040, 116)]
    [InlineData(1.5, 1560, 174)]
    [InlineData(2.0, 2080, 232)]
    public void LocalScaleUniformlyEnlargesTheConsole(double scale, double expectedWidth, double expectedHeight)
    {
        CombatConsoleSize size = CombatConsoleSizing.Resolve(3840, 1, (float)scale);

        Assert.Equal(expectedWidth, size.FixedWidth, 3);
        Assert.Equal(expectedHeight, size.FixedHeight, 3);
        Assert.Equal(1040, size.BaseWidth, 3);
        Assert.Equal(scale, size.EffectiveLocalScale, 3);
    }

    [Fact]
    public void OversizedPreferenceIsReducedToRemainInsideANarrowViewport()
    {
        CombatConsoleSize size = CombatConsoleSizing.Resolve(600, 1, 2f);

        Assert.True(size.FixedWidth <= 580);
        Assert.True(size.BaseWidth >= CombatConsoleSizing.MinimumBaseWidth);
        Assert.True(size.EffectiveLocalScale < 2);
    }
}
