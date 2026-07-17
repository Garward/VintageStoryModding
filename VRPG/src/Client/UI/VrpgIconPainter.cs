using System;
using Cairo;

namespace VRPG.Client.UI;

public static class VrpgIconPainter
{
    public static void Draw(Context ctx, string token, double x, double y, double size, double r, double g, double b, double alpha = 1.0)
    {
        string icon = Normalize(token);
        double cx = x + size / 2.0;
        double cy = y + size / 2.0;
        double unit = size / 10.0;
        ctx.Save();
        ctx.SetSourceRGBA(r, g, b, alpha);
        ctx.LineWidth = Math.Max(1.4, unit * 0.62);
        ctx.LineCap = LineCap.Round;
        ctx.LineJoin = LineJoin.Round;

        switch (icon)
        {
            case "anvil":
                ctx.Rectangle(x + unit * 1.6, y + unit * 2.0, unit * 6.8, unit * 1.5);
                ctx.Fill();
                Polygon(ctx, (x + unit * 3.0, y + unit * 3.5), (x + unit * 7.2, y + unit * 3.5), (x + unit * 6.2, y + unit * 6.4), (x + unit * 6.2, y + unit * 7.6), (x + unit * 2.8, y + unit * 7.6), (x + unit * 3.8, y + unit * 6.4));
                ctx.Fill();
                break;
            case "trap":
                ctx.Arc(cx, cy, unit * 3.0, 0, Math.PI * 2.0);
                ctx.Stroke();
                for (int i = 0; i < 8; i++)
                {
                    double angle = i * Math.PI / 4.0;
                    ctx.MoveTo(cx + Math.Cos(angle) * unit * 2.0, cy + Math.Sin(angle) * unit * 2.0);
                    ctx.LineTo(cx + Math.Cos(angle) * unit * 3.8, cy + Math.Sin(angle) * unit * 3.8);
                }
                ctx.Stroke();
                break;
            case "dagger":
                ctx.MoveTo(x + unit * 2.0, y + unit * 8.0);
                ctx.LineTo(x + unit * 6.6, y + unit * 3.4);
                ctx.Stroke();
                Polygon(ctx, (x + unit * 6.0, y + unit * 2.0), (x + unit * 8.1, y + unit * 1.8), (x + unit * 7.8, y + unit * 4.0));
                ctx.Fill();
                ctx.MoveTo(x + unit * 2.4, y + unit * 5.9);
                ctx.LineTo(x + unit * 4.1, y + unit * 7.6);
                ctx.Stroke();
                break;
            case "ward":
                Polygon(ctx, (cx, y + unit * 1.4), (x + unit * 8.0, y + unit * 2.7), (x + unit * 7.5, y + unit * 6.6), (cx, y + unit * 8.6), (x + unit * 2.5, y + unit * 6.6), (x + unit * 2.0, y + unit * 2.7));
                ctx.Stroke();
                ctx.MoveTo(cx, y + unit * 3.0);
                ctx.LineTo(cx, y + unit * 7.1);
                ctx.MoveTo(x + unit * 3.7, y + unit * 4.8);
                ctx.LineTo(x + unit * 6.3, y + unit * 4.8);
                ctx.Stroke();
                break;
            case "rust":
                FillCircle(ctx, x + unit * 3.1, y + unit * 3.2, unit * 1.4);
                FillCircle(ctx, x + unit * 6.4, y + unit * 4.1, unit * 1.8);
                FillCircle(ctx, x + unit * 4.5, y + unit * 7.0, unit * 1.3);
                break;
            case "construct":
                ctx.Rectangle(x + unit * 2.3, y + unit * 2.3, unit * 5.4, unit * 5.4);
                ctx.Stroke();
                ctx.MoveTo(cx, y + unit * 1.0);
                ctx.LineTo(cx, y + unit * 9.0);
                ctx.MoveTo(x + unit, cy);
                ctx.LineTo(x + unit * 9.0, cy);
                ctx.Stroke();
                FillCircle(ctx, cx, cy, unit * 1.1);
                break;
            case "cinder_orb":
                FillCircle(ctx, x + unit * 6.1, y + unit * 4.5, unit * 2.2);
                ctx.MoveTo(x + unit * 1.5, y + unit * 7.4);
                ctx.CurveTo(x + unit * 2.2, y + unit * 5.0, x + unit * 3.6, y + unit * 6.0, x + unit * 4.4, y + unit * 4.7);
                ctx.Stroke();
                break;
            case "fracture_pulse":
                ctx.Arc(cx, cy, unit * 1.5, 0, Math.PI * 2.0);
                ctx.Stroke();
                ctx.Arc(cx, cy, unit * 3.3, -Math.PI * 0.85, Math.PI * 0.15);
                ctx.Stroke();
                ctx.MoveTo(cx, cy);
                ctx.LineTo(x + unit * 7.8, y + unit * 7.8);
                ctx.Stroke();
                break;
            case "rust_lance":
                ctx.MoveTo(x + unit * 1.4, y + unit * 7.6);
                ctx.LineTo(x + unit * 7.0, y + unit * 2.0);
                ctx.Stroke();
                Polygon(ctx, (x + unit * 6.0, y + unit * 1.5), (x + unit * 8.7, y + unit * 1.2), (x + unit * 8.4, y + unit * 3.9));
                ctx.Fill();
                break;
            default:
                DrawFallback(ctx, Initials(icon), cx, y + size * 0.66, size * 0.31, r, g, b, alpha);
                break;
        }

        ctx.Restore();
    }

    private static void FillCircle(Context ctx, double x, double y, double radius)
    {
        ctx.Arc(x, y, radius, 0, Math.PI * 2.0);
        ctx.Fill();
    }

    private static void Polygon(Context ctx, params (double X, double Y)[] points)
    {
        if (points.Length == 0) return;
        ctx.MoveTo(points[0].X, points[0].Y);
        for (int i = 1; i < points.Length; i++) ctx.LineTo(points[i].X, points[i].Y);
        ctx.ClosePath();
    }

    private static void DrawFallback(Context ctx, string text, double x, double baseline, double size, double r, double g, double b, double alpha)
    {
        ctx.SelectFontFace("Arial", FontSlant.Normal, FontWeight.Bold);
        ctx.SetFontSize(size);
        TextExtents extents = ctx.TextExtents(text);
        ctx.SetSourceRGBA(r, g, b, alpha);
        ctx.MoveTo(x - extents.Width / 2.0 - extents.XBearing, baseline);
        ctx.ShowText(text);
    }

    private static string Normalize(string value)
    {
        string safe = (value ?? "").Trim().ToLowerInvariant();
        int colon = safe.IndexOf(':');
        if (colon >= 0) safe = safe.Substring(colon + 1);
        return safe.Replace('-', '_').Replace(' ', '_');
    }

    private static string Initials(string value)
    {
        string[] words = value.Split(new[] { '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return "?";
        if (words.Length == 1) return words[0].Substring(0, Math.Min(2, words[0].Length)).ToUpperInvariant();
        return (words[0][0].ToString() + words[1][0]).ToUpperInvariant();
    }
}
