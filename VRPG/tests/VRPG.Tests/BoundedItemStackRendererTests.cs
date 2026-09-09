using VRPG.Client.UI;
using Xunit;

namespace VRPG.Tests;

public sealed class BoundedItemStackRendererTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(200, 300)]
    public void FortyPixelBrowseModelsKeepTheirClipWithinEachLiveCell(double x, double y)
    {
        var layout = BoundedItemStackRenderer.BoundedItemIconLayout.Resolve(x, y, 40);
        Assert.Equal(x + 2, layout.ClipX); Assert.Equal(y + 2, layout.ClipY);
        Assert.Equal(36, layout.ClipWidth); Assert.Equal(36, layout.ClipHeight);
        Assert.InRange(layout.CenterX, x, x + 40); Assert.InRange(layout.CenterY, y, y + 40);
    }
    [Fact]
    public void ItemModelUsesVanillaItemToSlotRatio()
    {
        BoundedItemStackRenderer.BoundedItemIconLayout layout =
            BoundedItemStackRenderer.BoundedItemIconLayout.Resolve(10, 20, 48);

        Assert.Equal(25.6, layout.ItemSize, 3);
        Assert.Equal(BoundedItemStackRenderer.ItemToSlotRatio, layout.ItemSize / 48, 6);
    }

    [Fact]
    public void OversizedModelsAreClippedInsideTheSlotBorder()
    {
        BoundedItemStackRenderer.BoundedItemIconLayout layout =
            BoundedItemStackRenderer.BoundedItemIconLayout.Resolve(10, 20, 48);

        Assert.Equal(12, layout.ClipX, 3);
        Assert.Equal(22, layout.ClipY, 3);
        Assert.Equal(44, layout.ClipWidth, 3);
        Assert.Equal(44, layout.ClipHeight, 3);
        Assert.True(layout.ClipX + layout.ClipWidth < 10 + 48);
        Assert.True(layout.ClipY + layout.ClipHeight < 20 + 48);
    }
}
