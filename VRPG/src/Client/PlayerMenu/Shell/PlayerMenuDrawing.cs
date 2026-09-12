using System;
using Cairo;
using VRPG.Client.PlayerMenu.Components;

namespace VRPG.Client.PlayerMenu.Shell;

public readonly record struct MenuTextMetrics(double Width, double Height, double XBearing, double YBearing);

/// <summary>Shared hotbar-family materials and measured typography for every menu page.</summary>
public static class PlayerMenuDrawing
{
    public static (double X, double Y) CenteredOrigin(MenuRect bounds, MenuTextMetrics metrics, double pressOffset)
    {
        double x = bounds.X + (bounds.Width - metrics.Width) / 2d - metrics.XBearing;
        double y = bounds.Y + (bounds.Height - metrics.Height) / 2d - metrics.YBearing + pressOffset;
        return (x, y);
    }

    public static void Color(Context ctx, string hex, double alpha = 1) => ctx.SetSourceRGBA(
        Convert.ToInt32(hex[..2], 16) / 255d, Convert.ToInt32(hex[2..4], 16) / 255d,
        Convert.ToInt32(hex[4..], 16) / 255d, alpha);

    public static void Fill(Context ctx, MenuRect bounds, string color)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        Color(ctx, color); ctx.Rectangle(bounds.X, bounds.Y, bounds.Width, bounds.Height); ctx.Fill();
    }

    public static void Stroke(Context ctx, MenuRect bounds, string color, double line, double alpha = 1)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        Color(ctx, color, alpha); ctx.LineWidth = line;
        ctx.Rectangle(bounds.X, bounds.Y, bounds.Width, bounds.Height); ctx.Stroke();
    }

    public static void DrawChassis(Context ctx, MenuRect bounds)
    {
        Color(ctx, PlayerMenuTheme.Scrim, PlayerMenuTheme.ScrimOpacity);
        ctx.Rectangle(bounds.X, bounds.Y, bounds.Width, bounds.Height); ctx.Fill();
        NotchedPath(ctx, Inset(bounds, 2), 10);
        Color(ctx, PlayerMenuTheme.ChassisOuter); ctx.FillPreserve();
        Color(ctx, PlayerMenuTheme.CopperDull, PlayerMenuTheme.ChassisEdgeOpacity); ctx.LineWidth = 1; ctx.Stroke();
        NotchedPath(ctx, Inset(bounds, 6), 7);
        Color(ctx, PlayerMenuTheme.ChassisInner); ctx.FillPreserve();
        Color(ctx, PlayerMenuTheme.Iron700); ctx.Stroke();
        foreach (double x in new[] { bounds.X + 11, bounds.Right - 11 })
        foreach (double y in new[] { bounds.Y + 11, bounds.Bottom - 11 })
        {
            Color(ctx, PlayerMenuTheme.CopperDull); ctx.Arc(x, y, 1.7, 0, Math.PI * 2); ctx.Fill();
            Fill(ctx, new(x - 1, y - .4, 2, .8), PlayerMenuTheme.InsetShadow);
        }
    }

    public static void DrawPlate(Context ctx, MenuRect bounds)
    {
        NotchedPath(ctx, bounds, 5); Color(ctx, PlayerMenuTheme.Plate); ctx.FillPreserve();
        Color(ctx, PlayerMenuTheme.CopperDull, PlayerMenuTheme.PlateEdgeOpacity); ctx.LineWidth = .8; ctx.Stroke();
        Fill(ctx, new(bounds.X + 6, bounds.Y + 1, Math.Max(0, bounds.Width - 12), 1), PlayerMenuTheme.Iron700);
    }

    public static void DrawWell(Context ctx, MenuRect bounds)
    {
        DrawPlate(ctx, bounds);
        Fill(ctx, Inset(bounds, 3), PlayerMenuTheme.Well);
        Fill(ctx, new(bounds.X + 4, bounds.Y + 3, Math.Max(0, bounds.Width - 8), 2), PlayerMenuTheme.InsetShadow);
    }

    public static void DrawButton(Context ctx, MenuRect bounds, string label, FittedButton button,
        MenuControlState visual, bool focused, bool reducedMotion, bool attachedTab = false)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        double offset = new FittedButton(button.Intent, button.LabelKey, visual).PressOffset(reducedMotion);
        bool selected = button.State == MenuControlState.Selected;
        string surface = visual == MenuControlState.Pressed ? PlayerMenuTheme.Well
            : selected ? PlayerMenuTheme.SelectedPlate : visual == MenuControlState.Hover ? PlayerMenuTheme.HoverPlate : PlayerMenuTheme.Plate;
        Fill(ctx, bounds, PlayerMenuTheme.InsetShadow);
        Fill(ctx, Inset(bounds, 1) with { Y = bounds.Y + 1 + offset, Height = Math.Max(0, bounds.Height - 3 - offset) }, surface);
        string edge = button.State == MenuControlState.Rejected ? PlayerMenuTheme.Danger
            : button.State == MenuControlState.Success ? PlayerMenuTheme.Success
            : selected || button.State == MenuControlState.Pending ? PlayerMenuTheme.Heat
            : visual == MenuControlState.Hover ? PlayerMenuTheme.CopperWorn : PlayerMenuTheme.CopperDull;
        Stroke(ctx, Inset(bounds, .5), edge, selected ? 1.3 : .7, button.CanActivate ? 1 : .45);
        if (selected && attachedTab)
        {
            // Raised top lip and an open lower edge attach the current page to its rail.
            Fill(ctx, new(bounds.X + 3, bounds.Y + 2, bounds.Width - 6, 2), PlayerMenuTheme.CopperWorn);
            Fill(ctx, new(bounds.X + 2, bounds.Bottom - 2, bounds.Width - 4, 3), surface);
        }
        else if (selected || button.State == MenuControlState.Pending)
            Fill(ctx, new(bounds.X + 3, bounds.Y + 4, 2, bounds.Height - 8), PlayerMenuTheme.Heat);
        if (focused || button.State == MenuControlState.Focused)
            Stroke(ctx, Inset(bounds, 4), PlayerMenuTheme.HeatHot, 1);
        if (label.Length == 0) return;
        ctx.Save(); ctx.Rectangle(bounds.X + 6, bounds.Y + 2, Math.Max(0, bounds.Width - 12), Math.Max(0, bounds.Height - 4)); ctx.Clip();
        ctx.SelectFontFace(PlayerMenuTheme.Font, FontSlant.Normal, FontWeight.Normal); ctx.SetFontSize(14);
        Color(ctx, button.CanActivate ? PlayerMenuTheme.Text : PlayerMenuTheme.DisabledText);
        var ink = ctx.TextExtents(label);
        var origin = CenteredOrigin(bounds, new(ink.Width, ink.Height, ink.XBearing, ink.YBearing), offset);
        ctx.MoveTo(origin.X, origin.Y); ctx.ShowText(label); ctx.Restore();
    }

    public static void DrawSectionHeading(Context ctx, string text, MenuRect bounds, double size = 20) =>
        DrawBodyText(ctx, text, bounds, size, PlayerMenuTheme.Text, FontWeight.Bold);

    public static void DrawBodyText(Context ctx, string text, MenuRect bounds, double size, string color = PlayerMenuTheme.Text,
        FontWeight weight = FontWeight.Normal)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        ctx.Save(); ctx.Rectangle(bounds.X, bounds.Y, bounds.Width, bounds.Height); ctx.Clip();
        ctx.SelectFontFace(PlayerMenuTheme.Font, FontSlant.Normal, weight); ctx.SetFontSize(size); Color(ctx, color);
        double y = bounds.Y + size;
        foreach (string line in PlayerMenuTextLayout.Wrap(text, bounds.Width, value => ctx.TextExtents(value).Width))
        { ctx.MoveTo(bounds.X, y); ctx.ShowText(line); y += size + 5; }
        ctx.Restore();
    }

    private static MenuRect Inset(MenuRect b, double inset) => new(b.X + inset, b.Y + inset,
        Math.Max(0, b.Width - inset * 2), Math.Max(0, b.Height - inset * 2));

    private static void NotchedPath(Context ctx, MenuRect b, double notch)
    {
        double n = Math.Max(0, Math.Min(notch, Math.Min(b.Width, b.Height) / 2));
        ctx.NewPath(); ctx.MoveTo(b.X + n, b.Y); ctx.LineTo(b.Right - n, b.Y);
        ctx.LineTo(b.Right, b.Y + n); ctx.LineTo(b.Right, b.Bottom - n); ctx.LineTo(b.Right - n, b.Bottom);
        ctx.LineTo(b.X + n, b.Bottom); ctx.LineTo(b.X, b.Bottom - n); ctx.LineTo(b.X, b.Y + n); ctx.ClosePath();
    }
}
