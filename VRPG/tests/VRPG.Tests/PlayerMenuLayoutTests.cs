using VRPG.Client.PlayerMenu.Shell;
using Xunit;

namespace VRPG.Tests;

public sealed class PlayerMenuLayoutTests
{
    [Theory]
    [InlineData(1120, 680)][InlineData(800, 520)][InlineData(620, 520)][InlineData(400, 350)]
    public void HeaderTitleNavigationBodyAndFooterHaveDedicatedSpace(double width, double height)
    {
        var layout = PlayerMenuLayout.Resolve(width, height);
        Assert.True(layout.Header.Bottom <= layout.Title.Y);
        Assert.True(layout.Title.Bottom <= layout.Navigation.Y);
        Assert.True(layout.Navigation.Bottom <= layout.Browser.Y);
        Assert.True(layout.Browser.Bottom <= layout.Footer.Y);
        Assert.True(layout.PrimaryAction.Y >= layout.Footer.Y);
        for (int i = 0; i < 6; i++)
        {
            var tab = layout.PageTab(i);
            Assert.True(tab.X >= layout.Navigation.X && tab.Right <= layout.Navigation.Right);
            Assert.True(tab.Y >= layout.Navigation.Y && tab.Bottom <= layout.Navigation.Bottom);
            Assert.True(tab.Width >= 90 && tab.Height >= 30);
            for (int j = 0; j < i; j++)
            {
                var other = layout.PageTab(j);
                Assert.True(tab.X >= other.Right || tab.Y >= other.Bottom);
            }
        }
        Assert.True(layout.InventoryVisibleRows >= 1);
    }

    [Fact]
    public void SmallWindowKeepsASelectableBrowserRowBelowItsChrome()
    {
        var layout = PlayerMenuLayout.Resolve(400, 350);
        Assert.True(layout.VisibleRows >= 1, "The smallest supported window must expose a complete selectable row.");
        var row = layout.BrowserRow(0);
        Assert.True(layout.BrowserSearch.Bottom <= layout.BrowserContent.Y);
        Assert.True(row.Y >= layout.BrowserContent.Y && row.Bottom <= layout.BrowserContent.Bottom);
        Assert.True(row.X >= layout.BrowserContent.X && row.Right <= layout.BrowserContent.Right);
        Assert.True(layout.BrowserContent.Contains(row.X + row.Width / 2, row.Y + row.Height / 2));
    }
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
