using VRPG.Client;
using VRPG.Config;
using VRPG.Network;
using Vintagestory.API.Client;
using Xunit;

namespace VRPG.Tests;

public sealed class ResourceHudLayoutTests
{
    [Fact]
    public void DefaultHudReservesHealthManaAndExperienceRows()
    {
        var config = new RpgHudConfig { BarHeight = 22, Gap = 7, ShowExperience = true };
        var snapshot = new RpgResourcePacket { MaxHealth = 100, MaxMana = 100 };

        Assert.Equal(3, ResourceHudLayout.VisibleRows(config, snapshot));
        Assert.Equal(80, ResourceHudLayout.Height(config, snapshot));
    }

    [Fact]
    public void OptionalResourcesExpandBoundsWhenVisible()
    {
        var config = new RpgHudConfig { ShowExperience = true };
        var snapshot = new RpgResourcePacket
        {
            MaxMagicShield = 25,
            BloodUnlocked = true
        };

        Assert.Equal(5, ResourceHudLayout.VisibleRows(config, snapshot));
    }

    [Fact]
    public void BottomAnchorUsesNegativeInsetInsteadOfPushingBarsOffscreen()
    {
        ResourceHudOffset bottom = ResourceHudLayout.AlignmentOffset(EnumDialogArea.LeftBottom, 14, 112);
        ResourceHudOffset top = ResourceHudLayout.AlignmentOffset(EnumDialogArea.LeftTop, 14, 112);

        Assert.Equal(14, bottom.X);
        Assert.Equal(-112, bottom.Y);
        Assert.Equal(14, top.X);
        Assert.Equal(112, top.Y);
    }

    [Fact]
    public void BottomAnchorResolvesToDraggableTopLeftPosition()
    {
        ResourceHudPosition position = ResourceHudLayout.ResolveTopLeft(
            EnumDialogArea.LeftBottom,
            screenWidth: 1920,
            screenHeight: 1080,
            widgetWidth: 350,
            widgetHeight: 80,
            insetX: 14,
            insetY: 112);

        Assert.Equal(14, position.X);
        Assert.Equal(888, position.Y);
    }

    [Theory]
    [InlineData(20, ResourceHudLayout.MinimumWidth)]
    [InlineData(420, 420)]
    [InlineData(900, ResourceHudLayout.MaximumWidth)]
    public void WidthIsClampedToUsableRange(int requested, int expected)
    {
        Assert.Equal(expected, ResourceHudLayout.ClampWidth(requested));
    }

    [Fact]
    public void TopLeftPositionCannotLeaveTheScreen()
    {
        ResourceHudPosition position = ResourceHudLayout.ClampTopLeft(1900, -20, 1920, 1080, 350, 80);

        Assert.Equal(1570, position.X);
        Assert.Equal(0, position.Y);
    }
}
