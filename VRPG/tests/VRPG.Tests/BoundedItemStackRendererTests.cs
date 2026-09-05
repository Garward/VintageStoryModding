using VRPG.Client.UI;
using Xunit;

namespace VRPG.Tests;

public sealed class BoundedItemStackRendererTests
{
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
