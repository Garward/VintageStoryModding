using VRPG.Client.PlayerMenu.Shell;
using Xunit;

namespace VRPG.Tests;

public sealed class PlayerMenuLayoutTests
{
    [Theory]
    [InlineData(1120, 680)][InlineData(800, 520)][InlineData(640, 480)][InlineData(400, 350)]
    public void CriticalControlsStayInsideAndPanesDoNotOverlap(double width, double height)
    {
        var layout = PlayerMenuLayout.Resolve(width, height);
        foreach (var bounds in new[] { layout.Close, layout.Back, layout.PrimaryAction, layout.Browser, layout.Inspector })
        {
            Assert.True(bounds.Width >= 0 && bounds.Height >= 0);
            Assert.True(bounds.X >= 0 && bounds.Y >= 0 && bounds.Right <= width && bounds.Bottom <= height);
        }
        if (!layout.AlternatePanes) Assert.True(layout.Browser.Right <= layout.Inspector.X);
        Assert.True(layout.Close.X >= layout.Back.Right);
        Assert.True(layout.PrimaryAction.Y >= layout.Inspector.Bottom);
    }

    [Theory]
    [InlineData(.8)][InlineData(1)][InlineData(1.25)][InlineData(1.5)]
    public void ScaleConvertsPhysicalViewportWithoutForcingMinimumWindow(double scale)
    {
        var layout = PlayerMenuLayout.ForViewport(1280, 720, scale);
        Assert.True(layout.Width * scale <= 1280);
        Assert.True(layout.Height * scale <= 720);
    }
}
