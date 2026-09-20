using System;
using Cairo;
using VRPG.Client.PlayerMenu.Components;
using VRPG.Client.UI;
using Vintagestory.API.Client;

namespace VRPG.Client.PlayerMenu.Shell;

public readonly record struct MenuTextMetrics(double Width, double Height, double XBearing, double YBearing);
public readonly record struct IconLabelLayout(MenuRect Icon, (double X, double Y) Text);
public readonly record struct MenuControlVisual(string Surface, string Edge, double EdgeWidth, double Opacity,
    bool AccentRail, bool FocusGroove, bool CrossHatch, bool PendingMark, int StateMarker);
public readonly record struct PlayerMenuFonts(string Decorative, string Body)
{
    public static PlayerMenuFonts Resolve(string? decorative, string? body) => new(
        string.IsNullOrWhiteSpace(decorative) ? "Lora" : decorative.Trim(),
        string.IsNullOrWhiteSpace(body) ? "sans-serif" : body.Trim());

    public static PlayerMenuFonts From(ICoreClientAPI? api)
    {
#pragma warning disable CS0618 // Renderer remains the live per-client source on supported 1.22 builds.
        try { return Resolve(api?.Render?.DecorativeFontName, api?.Render?.StandardFontName); }
#pragma warning restore CS0618
        catch { return Resolve(null, null); }
    }
}

/// <summary>Shared hotbar-family materials and measured typography for every menu page.</summary>
public static class PlayerMenuDrawing
{
    public static readonly PlayerMenuFonts TestFonts = PlayerMenuFonts.Resolve(null, null);

    public static (double X, double Y) CenteredOrigin(MenuRect bounds, MenuTextMetrics metrics, double pressOffset)
    {
        double x = bounds.X + (bounds.Width - metrics.Width) / 2d - metrics.XBearing;
        double y = bounds.Y + (bounds.Height - metrics.Height) / 2d - metrics.YBearing + pressOffset;
        return (x, y);
    }

    public static IconLabelLayout CenteredIconLabel(MenuRect bounds, double iconSize, double gap,
        MenuTextMetrics metrics, double pressOffset)
    {
        double groupWidth = iconSize + gap + metrics.Width;
        double x = bounds.X + (bounds.Width - groupWidth) / 2d;
        double iconY = bounds.Y + (bounds.Height - iconSize) / 2d + pressOffset;
        double baseline = bounds.Y + (bounds.Height - metrics.Height) / 2d - metrics.YBearing + pressOffset;
        return new(new(x, iconY, iconSize, iconSize), (x + iconSize + gap - metrics.XBearing, baseline));
    }

    public static MenuControlVisual ResolveControlVisual(MenuControlState state) => state switch
    {
        MenuControlState.Hover => new(PlayerMenuTheme.HoverPlate, PlayerMenuTheme.CopperWorn, 1, 1, false, false, false, false, 1),
        MenuControlState.Focused => new(PlayerMenuTheme.Plate, PlayerMenuTheme.HeatHot, 1, 1, false, true, false, false, 2),
        MenuControlState.Pressed => new(PlayerMenuTheme.Well, PlayerMenuTheme.CopperWorn, 1.2, 1, false, false, false, false, 3),
        MenuControlState.Selected => new(PlayerMenuTheme.SelectedPlate, PlayerMenuTheme.Heat, 1.4, 1, true, false, false, false, 4),
        MenuControlState.Disabled => new(PlayerMenuTheme.Iron900, PlayerMenuTheme.Iron700, .7, .48, false, false, true, false, 5),
        MenuControlState.Locked => new(PlayerMenuTheme.Iron900, PlayerMenuTheme.CopperDull, .7, .58, false, false, true, false, 6),
        MenuControlState.Pending => new(PlayerMenuTheme.Plate, PlayerMenuTheme.Heat, 1.2, .78, true, false, false, true, 7),
        MenuControlState.Success => new(PlayerMenuTheme.Plate, PlayerMenuTheme.Success, 1.1, 1, false, false, false, false, 8),
        MenuControlState.Rejected => new(PlayerMenuTheme.Plate, PlayerMenuTheme.Danger, 1.1, 1, false, false, false, false, 9),
        _ => new(PlayerMenuTheme.Plate, PlayerMenuTheme.CopperDull, .7, 1, false, false, false, false, 0)
    };

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
        MenuControlState visual, bool focused, bool reducedMotion, bool attachedTab = false,
        string icon = "", PlayerMenuFonts? fonts = null)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        double offset = new FittedButton(button.Intent, button.LabelKey, visual).PressOffset(reducedMotion);
        bool selected = button.State == MenuControlState.Selected;
        var style = ResolveControlVisual(button.State is MenuControlState.Disabled or MenuControlState.Locked or MenuControlState.Pending
            ? button.State : visual);
        Fill(ctx, bounds, PlayerMenuTheme.InsetShadow);
        Fill(ctx, Inset(bounds, 1) with { Y = bounds.Y + 1 + offset, Height = Math.Max(0, bounds.Height - 3 - offset) }, style.Surface);
        Stroke(ctx, Inset(bounds, .5), style.Edge, style.EdgeWidth, style.Opacity);
        if (selected && attachedTab)
        {
            // Raised top lip and an open lower edge attach the current page to its rail.
            Fill(ctx, new(bounds.X + 3, bounds.Y + 2, bounds.Width - 6, 2), PlayerMenuTheme.CopperWorn);
            Fill(ctx, new(bounds.X + 2, bounds.Bottom - 2, bounds.Width - 4, 3), style.Surface);
        }
        else if (style.AccentRail)
            Fill(ctx, new(bounds.X + 3, bounds.Y + 4, 2, bounds.Height - 8), PlayerMenuTheme.Heat);
        if (focused || style.FocusGroove)
            Stroke(ctx, Inset(bounds, 4), PlayerMenuTheme.HeatHot, 1);
        if (style.CrossHatch)
        {
            ctx.Save(); ctx.Rectangle(bounds.X + 2, bounds.Y + 2, Math.Max(0, bounds.Width - 4), Math.Max(0, bounds.Height - 4)); ctx.Clip();
            Color(ctx, PlayerMenuTheme.DisabledText, .18); ctx.LineWidth = 1;
            for (double x = bounds.X - bounds.Height; x < bounds.Right; x += 9)
            { ctx.MoveTo(x, bounds.Bottom); ctx.LineTo(x + bounds.Height, bounds.Y); }
            ctx.Stroke(); ctx.Restore();
        }
        if (style.PendingMark)
        {
            Color(ctx, PlayerMenuTheme.HeatHot, .9); ctx.LineWidth = 2;
            ctx.Arc(bounds.Right - 12, bounds.Y + 12, 5, -.5 * Math.PI, 1.1 * Math.PI); ctx.Stroke();
        }
        if (label.Length == 0) return;
        ctx.Save(); ctx.Rectangle(bounds.X + 6, bounds.Y + 2, Math.Max(0, bounds.Width - 12), Math.Max(0, bounds.Height - 4)); ctx.Clip();
        ctx.SelectFontFace((fonts ?? TestFonts).Body, FontSlant.Normal, FontWeight.Normal); ctx.SetFontSize(14);
        Color(ctx, button.CanActivate ? PlayerMenuTheme.Text : PlayerMenuTheme.DisabledText);
        var ink = ctx.TextExtents(label);
        if (icon.Length > 0 && bounds.Width >= 70)
        {
            double iconSize = Math.Min(18, bounds.Height - 12);
            var group = CenteredIconLabel(bounds, iconSize, 6, new(ink.Width, ink.Height, ink.XBearing, ink.YBearing), offset);
            Color(ctx, button.CanActivate ? PlayerMenuTheme.Text : PlayerMenuTheme.DisabledText);
            VrpgIconPainter.Draw(ctx, icon, group.Icon.X, group.Icon.Y, group.Icon.Width, 234 / 255d, 223 / 255d, 206 / 255d, style.Opacity);
            ctx.MoveTo(group.Text.X, group.Text.Y); ctx.ShowText(label);
        }
        else
        {
            var origin = CenteredOrigin(bounds, new(ink.Width, ink.Height, ink.XBearing, ink.YBearing), offset);
            ctx.MoveTo(origin.X, origin.Y); ctx.ShowText(label);
        }
        ctx.Restore();
    }

    public static void DrawSectionHeading(Context ctx, string text, MenuRect bounds, double size = 20, PlayerMenuFonts? fonts = null) =>
        DrawBodyText(ctx, text, bounds, size, PlayerMenuTheme.Text, FontWeight.Bold, (fonts ?? TestFonts).Decorative);

    public static void DrawBodyText(Context ctx, string text, MenuRect bounds, double size, string color = PlayerMenuTheme.Text,
        FontWeight weight = FontWeight.Normal, string? font = null)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        ctx.Save(); ctx.Rectangle(bounds.X, bounds.Y, bounds.Width, bounds.Height); ctx.Clip();
        ctx.SelectFontFace(font ?? TestFonts.Body, FontSlant.Normal, weight); ctx.SetFontSize(size); Color(ctx, color);
        double y = bounds.Y + size;
        foreach (string line in PlayerMenuTextLayout.Wrap(text, bounds.Width, value => ctx.TextExtents(value).Width))
        { ctx.MoveTo(bounds.X, y); ctx.ShowText(line); y += size + 5; }
        ctx.Restore();
    }

    public static void DrawSectionHeader(Context ctx, MenuRect bounds, string icon, string title, PlayerMenuFonts? fonts = null)
    {
        Fill(ctx, new(bounds.X, bounds.Bottom - 1, bounds.Width, 1), PlayerMenuTheme.CopperDull);
        VrpgIconPainter.Draw(ctx, icon, bounds.X, bounds.Y + 1, Math.Min(20, bounds.Height - 2), .92, .72, .42);
        DrawBodyText(ctx, title, new(bounds.X + 27, bounds.Y, bounds.Width - 27, bounds.Height), 16,
            PlayerMenuTheme.Text, FontWeight.Bold, (fonts ?? TestFonts).Decorative);
    }

    public static void DrawListRow(Context ctx, MenuRect bounds, bool selected, bool focused = false)
    {
        var state = selected ? MenuControlState.Selected : focused ? MenuControlState.Focused : MenuControlState.Default;
        var style = ResolveControlVisual(state); Fill(ctx, bounds, style.Surface); Stroke(ctx, bounds, style.Edge, style.EdgeWidth, style.Opacity);
        if (style.AccentRail) Fill(ctx, new(bounds.X, bounds.Y + 3, 3, Math.Max(0, bounds.Height - 6)), PlayerMenuTheme.Heat);
        if (style.FocusGroove) Stroke(ctx, Inset(bounds, 4), PlayerMenuTheme.HeatHot, 1);
    }

    public static void DrawKeyValueRow(Context ctx, MenuRect bounds, string key, string value, PlayerMenuFonts? fonts = null)
    {
        Fill(ctx, new(bounds.X, bounds.Bottom - 1, bounds.Width, 1), PlayerMenuTheme.Iron700);
        DrawBodyText(ctx, key, new(bounds.X, bounds.Y, bounds.Width * .58, bounds.Height), 14, PlayerMenuTheme.Muted, FontWeight.Normal, (fonts ?? TestFonts).Body);
        ctx.Save(); ctx.SelectFontFace((fonts ?? TestFonts).Body, FontSlant.Normal, FontWeight.Bold); ctx.SetFontSize(14); Color(ctx, PlayerMenuTheme.Text);
        var ext = ctx.TextExtents(value); ctx.MoveTo(bounds.Right - ext.Width - 2 - ext.XBearing, bounds.Y + (bounds.Height - ext.Height) / 2 - ext.YBearing); ctx.ShowText(value); ctx.Restore();
    }

    public static void DrawStatChip(Context ctx, MenuRect bounds, string icon, string label, string value, PlayerMenuFonts? fonts = null)
    {
        DrawPlate(ctx, bounds); VrpgIconPainter.Draw(ctx, icon, bounds.X + 7, bounds.Y + 7, Math.Min(20, bounds.Height - 14), .92, .72, .42);
        DrawBodyText(ctx, label, new(bounds.X + 34, bounds.Y + 2, bounds.Width - 42, bounds.Height / 2), 11, PlayerMenuTheme.Muted, FontWeight.Normal, (fonts ?? TestFonts).Body);
        DrawBodyText(ctx, value, new(bounds.X + 34, bounds.Y + bounds.Height / 2 - 3, bounds.Width - 42, bounds.Height / 2), 15, PlayerMenuTheme.Text, FontWeight.Bold, (fonts ?? TestFonts).Body);
    }

    public static void DrawEmptyState(Context ctx, MenuRect bounds, string icon, string title, string nextStep, PlayerMenuFonts? fonts = null)
    {
        double iconSize = Math.Min(44, Math.Min(bounds.Width, bounds.Height) * .18); double center = bounds.X + bounds.Width / 2;
        VrpgIconPainter.Draw(ctx, icon, center - iconSize / 2, bounds.Y + Math.Max(12, bounds.Height * .2), iconSize, .72, .51, .31, .8);
        double y = bounds.Y + Math.Max(62, bounds.Height * .2 + iconSize + 12);
        DrawCenteredText(ctx, title, new(bounds.X + 16, y, bounds.Width - 32, 28), 18, PlayerMenuTheme.Text, (fonts ?? TestFonts).Decorative, FontWeight.Bold);
        DrawCenteredText(ctx, nextStep, new(bounds.X + 24, y + 34, bounds.Width - 48, 52), 14, PlayerMenuTheme.Muted, (fonts ?? TestFonts).Body, FontWeight.Normal);
    }

    public static void DrawSearchWell(Context ctx, MenuRect bounds, string icon = "search")
    {
        DrawWell(ctx, bounds); VrpgIconPainter.Draw(ctx, icon, bounds.X + 8, bounds.Y + (bounds.Height - 18) / 2, 18, .67, .61, .54);
    }

    private static void DrawCenteredText(Context ctx, string text, MenuRect bounds, double size, string color, string font, FontWeight weight)
    {
        ctx.Save(); ctx.Rectangle(bounds.X, bounds.Y, bounds.Width, bounds.Height); ctx.Clip(); ctx.SelectFontFace(font, FontSlant.Normal, weight); ctx.SetFontSize(size); Color(ctx, color);
        var lines = PlayerMenuTextLayout.Wrap(text, bounds.Width, value => ctx.TextExtents(value).Width); double y = bounds.Y + size;
        foreach (string line in lines) { var ext = ctx.TextExtents(line); ctx.MoveTo(bounds.X + (bounds.Width - ext.Width) / 2 - ext.XBearing, y); ctx.ShowText(line); y += size + 4; }
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
