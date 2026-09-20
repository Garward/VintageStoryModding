using System;
using System.Linq;
using System.Runtime.InteropServices;
using Cairo;
using VRPG.Client.PlayerMenu.Components;
using VRPG.Client.PlayerMenu.Shell;
using VRPG.Client.UI;
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

    [Fact]
    public void MenuIconVocabularyDrawsBoundedNonTextInkAndRestoresContext()
    {
        InventoryCompositionTestScope.EnsureCairo();
        string[] tokens =
        {
            "inventory", "stats", "skills", "talents", "library", "options", "back", "close", "party", "search",
            "armor", "materials", "consumables", "quest", "miscellaneous", "filter", "sort", "capacity", "empty-bag",
            "strength", "dexterity", "intelligence", "available-point", "level", "primary-attribute", "evasion",
            "lock", "damage", "cooldown", "range", "resource-cost", "delivery", "combat-slot",
            "book", "skill", "status", "dungeon", "mechanics", "document", "something-unrecognized"
        };

        foreach (string token in tokens)
        {
            using var surface = new ImageSurface((Format)0, 48, 48);
            using var ctx = new Context(surface);
            ctx.LineWidth = 7; ctx.LineCap = LineCap.Square; ctx.Save();
            VrpgIconPainter.Draw(ctx, token, 8, 8, 32, 1, .6, .1);
            ctx.Restore(); surface.Flush();

            Assert.Equal(7, ctx.LineWidth, 3);
            Assert.Equal(LineCap.Square, ctx.LineCap);
            byte[] pixels = new byte[surface.Stride * surface.Height];
            Marshal.Copy(surface.DataPtr, pixels, 0, pixels.Length);
            var ink = Enumerable.Range(0, surface.Height).SelectMany(y => Enumerable.Range(0, surface.Width)
                .Where(x => pixels[y * surface.Stride + x * 4 + 3] != 0).Select(x => (x, y))).ToArray();
            Assert.NotEmpty(ink);
            Assert.All(ink, p => Assert.True(p.x >= 6 && p.x <= 42 && p.y >= 6 && p.y <= 42, $"{token} escaped at {p}"));
        }
    }

    [Fact]
    public void IconAndLabelAreCenteredAsOneGroup()
    {
        var layout = PlayerMenuDrawing.CenteredIconLabel(new MenuRect(10, 20, 180, 40), 18, 6,
            new MenuTextMetrics(72, 14, 0, -11), 0);
        Assert.Equal(52, layout.Icon.X, 3);
        Assert.Equal(31, layout.Icon.Y, 3);
        Assert.Equal(76, layout.Text.X, 3);
        Assert.Equal(44, layout.Text.Y, 3);
    }

    [Fact]
    public void ControlStatesHaveDistinctShapeAndColorSignals()
    {
        var states = Enum.GetValues<MenuControlState>().Select(PlayerMenuDrawing.ResolveControlVisual).ToArray();
        Assert.Equal(states.Length, states.Distinct().Count());
        Assert.True(PlayerMenuDrawing.ResolveControlVisual(MenuControlState.Selected).AccentRail);
        Assert.True(PlayerMenuDrawing.ResolveControlVisual(MenuControlState.Focused).FocusGroove);
        Assert.True(PlayerMenuDrawing.ResolveControlVisual(MenuControlState.Disabled).CrossHatch);
        Assert.True(PlayerMenuDrawing.ResolveControlVisual(MenuControlState.Pending).PendingMark);
    }

    [Fact]
    public void RuntimeFontsUseRendererValuesWithDeterministicFallbacks()
    {
        Assert.Equal(new PlayerMenuFonts("Lora", "sans-serif"), PlayerMenuFonts.Resolve(null, null));
        Assert.Equal(new PlayerMenuFonts("Custom Display", "Custom Body"), PlayerMenuFonts.Resolve(" Custom Display ", " Custom Body "));
    }
}
