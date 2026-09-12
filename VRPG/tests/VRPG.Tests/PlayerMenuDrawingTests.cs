using VRPG.Client.PlayerMenu.Shell;
using Xunit;

namespace VRPG.Tests;

public sealed class PlayerMenuDrawingTests
{
    [Theory]
    [InlineData(10, 20, 100, 40, 30, 12, 2, -9, 0, 43, 43)]
    [InlineData(10, 20, 100, 40, 30, 12, 2, -9, 1, 43, 44)]
    [InlineData(0, 0, 80, 34, 35, 15, -3, -11, 0, 25.5, 20.5)]
    public void CenteredOriginUsesMeasuredInkBounds(double x, double y, double width, double height,
        double inkWidth, double inkHeight, double xBearing, double yBearing, double pressOffset,
        double expectedX, double expectedY)
    {
        var origin = PlayerMenuDrawing.CenteredOrigin(new MenuRect(x, y, width, height),
            new MenuTextMetrics(inkWidth, inkHeight, xBearing, yBearing), pressOffset);
        Assert.Equal(expectedX, origin.X, 3);
        Assert.Equal(expectedY, origin.Y, 3);
    }
}
