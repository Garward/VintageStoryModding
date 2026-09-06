using System;

namespace VRPG.Client.PlayerMenu.Shell;

public readonly record struct MenuRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public bool Contains(double x, double y) => x >= X && y >= Y && x < Right && y < Bottom;
}

/// <summary>Logical geometry. Narrow screens alternate browse and inspection instead of overflowing.</summary>
public sealed record PlayerMenuLayout(double Width, double Height, bool AlternatePanes,
    MenuRect Close, MenuRect Back, MenuRect Browser, MenuRect Inspector, MenuRect PrimaryAction)
{
    public const double RowHeight = 54;
    public const double BrowserHeaderHeight = 142;
    public int VisibleRows => Math.Max(0, (int)((Browser.Height - BrowserHeaderHeight) / RowHeight));
    public int RenderRowLimit => VisibleRows + 2;
    public static PlayerMenuLayout ForViewport(double width, double height, double scale) =>
        Resolve(Math.Min(1120, Math.Max(0, width / Math.Max(.25, scale) - 24)),
            Math.Min(680, Math.Max(0, height / Math.Max(.25, scale) - 24)));
    public static PlayerMenuLayout Resolve(double width, double height)
    {
        width = Math.Max(0, width); height = Math.Max(0, height);
        double margin = Math.Min(24, width / 20), inner = Math.Max(0, width - margin * 2);
        bool alternate = width < 800 || height < 520;
        double top = Math.Min(alternate ? 142 : 122, height * .45);
        double footer = Math.Min(70, height * .2), bodyHeight = Math.Max(0, height - top - footer);
        double gap = alternate ? 0 : 24, browserWidth = alternate ? inner : (inner - gap) * .55;
        double detailX = alternate ? margin : margin + browserWidth + gap;
        double detailWidth = alternate ? inner : inner - browserWidth - gap;
        return new(width, height, alternate,
            new(width - margin - Math.Min(88, inner / 3), 10, Math.Min(88, inner / 3), Math.Min(34, height)),
            new(margin, 10, Math.Min(88, inner / 3), Math.Min(34, height)),
            new(margin, top, browserWidth, bodyHeight), new(detailX, top, detailWidth, bodyHeight),
            new(detailX, Math.Min(height, top + bodyHeight + 12), detailWidth, Math.Max(0, Math.Min(38, footer - 12))));
    }
}
