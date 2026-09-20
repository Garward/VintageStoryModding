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

        if (DrawMenuIcon(ctx, icon, x, y, size, unit))
        {
            ctx.Restore();
            return;
        }

        switch (icon)
        {
            case "gear":
            case "junk_toss":
                DrawGear(ctx, cx, cy, unit);
                break;
            case "scrap_bomb":
                ctx.Arc(cx, cy + unit * 0.7, unit * 3.0, 0, Math.PI * 2.0);
                ctx.Stroke();
                ctx.MoveTo(cx - unit * 2.6, cy);
                ctx.LineTo(cx + unit * 2.6, cy);
                ctx.MoveTo(cx, cy - unit * 2.2);
                ctx.CurveTo(cx + unit * 0.2, cy - unit * 3.8, cx + unit * 1.4, cy - unit * 3.4, cx + unit * 1.8, cy - unit * 4.1);
                ctx.Stroke();
                FillCircle(ctx, cx + unit * 1.9, cy - unit * 4.2, unit * 0.42);
                break;
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
                DrawFallback(ctx, cx, cy, unit);
                break;
        }

        ctx.Restore();
    }

    private static void FillCircle(Context ctx, double x, double y, double radius)
    {
        ctx.Arc(x, y, radius, 0, Math.PI * 2.0);
        ctx.Fill();
    }

    private static void DrawGear(Context ctx, double cx, double cy, double unit)
    {
        ctx.Arc(cx, cy, unit * 2.9, 0, Math.PI * 2.0);
        ctx.Stroke();
        ctx.Arc(cx, cy, unit * 1.05, 0, Math.PI * 2.0);
        ctx.Stroke();
        for (int i = 0; i < 8; i++)
        {
            double angle = i * Math.PI / 4.0;
            ctx.MoveTo(cx + Math.Cos(angle) * unit * 2.7, cy + Math.Sin(angle) * unit * 2.7);
            ctx.LineTo(cx + Math.Cos(angle) * unit * 3.9, cy + Math.Sin(angle) * unit * 3.9);
        }
        ctx.Stroke();
    }

    private static void Polygon(Context ctx, params (double X, double Y)[] points)
    {
        if (points.Length == 0) return;
        ctx.MoveTo(points[0].X, points[0].Y);
        for (int i = 1; i < points.Length; i++) ctx.LineTo(points[i].X, points[i].Y);
        ctx.ClosePath();
    }

    private static bool DrawMenuIcon(Context ctx, string icon, double x, double y, double size, double u)
    {
        double cx = x + size / 2, cy = y + size / 2;
        switch (icon)
        {
            case "inventory": case "bag": case "empty_bag": case "pouch": case "miscellaneous":
                ctx.MoveTo(x + 2.4 * u, y + 3.7 * u); ctx.CurveTo(x + 2.2 * u, y + 7.8 * u, x + 2.8 * u, y + 8.6 * u, cx, y + 8.6 * u);
                ctx.CurveTo(x + 7.2 * u, y + 8.6 * u, x + 7.8 * u, y + 7.8 * u, x + 7.6 * u, y + 3.7 * u); ctx.ClosePath(); ctx.Stroke();
                ctx.MoveTo(x + 3.2 * u, y + 3.7 * u); ctx.CurveTo(x + 3.8 * u, y + 1.5 * u, x + 6.2 * u, y + 1.5 * u, x + 6.8 * u, y + 3.7 * u); ctx.Stroke();
                if (icon == "empty_bag") { ctx.MoveTo(x + 3.8 * u, cy); ctx.LineTo(x + 6.2 * u, cy); ctx.Stroke(); }
                return true;
            case "stats": case "crest": case "primary_attribute":
                Polygon(ctx, (cx, y + 1.3 * u), (x + 8 * u, y + 2.5 * u), (x + 7.4 * u, y + 6.5 * u), (cx, y + 8.8 * u), (x + 2.6 * u, y + 6.5 * u), (x + 2 * u, y + 2.5 * u)); ctx.Stroke();
                ctx.MoveTo(cx, y + 3 * u); ctx.LineTo(cx, y + 7 * u); ctx.MoveTo(x + 3.7 * u, y + 4.8 * u); ctx.LineTo(x + 6.3 * u, y + 4.8 * u); ctx.Stroke(); return true;
            case "skills": case "skill": case "blades": case "damage":
                ctx.MoveTo(x + 2 * u, y + 8 * u); ctx.LineTo(x + 7.7 * u, y + 2.3 * u); ctx.MoveTo(x + 8 * u, y + 8 * u); ctx.LineTo(x + 2.3 * u, y + 2.3 * u); ctx.Stroke();
                ctx.MoveTo(x + 2.5 * u, y + 6.5 * u); ctx.LineTo(x + 3.5 * u, y + 7.5 * u); ctx.MoveTo(x + 7.5 * u, y + 6.5 * u); ctx.LineTo(x + 6.5 * u, y + 7.5 * u); ctx.Stroke(); return true;
            case "talents": case "talent_nodes":
                ctx.MoveTo(cx, cy); ctx.LineTo(x + 2.4 * u, y + 2.4 * u); ctx.MoveTo(cx, cy); ctx.LineTo(x + 7.6 * u, y + 2.4 * u); ctx.MoveTo(cx, cy); ctx.LineTo(cx, y + 8 * u); ctx.Stroke();
                FillCircle(ctx, cx, cy, .9 * u); FillCircle(ctx, x + 2.4 * u, y + 2.4 * u, .8 * u); FillCircle(ctx, x + 7.6 * u, y + 2.4 * u, .8 * u); FillCircle(ctx, cx, y + 8 * u, .8 * u); return true;
            case "library": case "book": case "all":
                ctx.MoveTo(cx, y + 2.2 * u); ctx.CurveTo(x + 4 * u, y + 1.5 * u, x + 2.3 * u, y + 1.8 * u, x + 1.7 * u, y + 2.4 * u); ctx.LineTo(x + 1.7 * u, y + 8 * u); ctx.CurveTo(x + 3 * u, y + 7.3 * u, x + 4.2 * u, y + 7.6 * u, cx, y + 8.4 * u);
                ctx.CurveTo(x + 5.8 * u, y + 7.6 * u, x + 7 * u, y + 7.3 * u, x + 8.3 * u, y + 8 * u); ctx.LineTo(x + 8.3 * u, y + 2.4 * u); ctx.CurveTo(x + 7.7 * u, y + 1.8 * u, x + 6 * u, y + 1.5 * u, cx, y + 2.2 * u); ctx.ClosePath(); ctx.Stroke(); ctx.MoveTo(cx, y + 2.2 * u); ctx.LineTo(cx, y + 8.4 * u); ctx.Stroke(); return true;
            case "options": DrawGear(ctx, cx, cy, u); return true;
            case "back": case "chevron_back":
                ctx.MoveTo(x + 6.7 * u, y + 1.8 * u); ctx.LineTo(x + 3.2 * u, cy); ctx.LineTo(x + 6.7 * u, y + 8.2 * u); ctx.Stroke(); return true;
            case "close":
                ctx.MoveTo(x + 2.2 * u, y + 2.2 * u); ctx.LineTo(x + 7.8 * u, y + 7.8 * u); ctx.MoveTo(x + 7.8 * u, y + 2.2 * u); ctx.LineTo(x + 2.2 * u, y + 7.8 * u); ctx.Stroke(); return true;
            case "party":
                FillCircle(ctx, cx, y + 3 * u, 1.2 * u); FillCircle(ctx, x + 2.6 * u, y + 4 * u, .9 * u); FillCircle(ctx, x + 7.4 * u, y + 4 * u, .9 * u);
                ctx.Arc(cx, y + 7.8 * u, 2.5 * u, Math.PI, Math.PI * 2); ctx.Stroke(); ctx.Arc(x + 2.3 * u, y + 7.8 * u, 1.6 * u, Math.PI, Math.PI * 1.85); ctx.Stroke(); ctx.Arc(x + 7.7 * u, y + 7.8 * u, 1.6 * u, Math.PI * 1.15, Math.PI * 2); ctx.Stroke(); return true;
            case "search":
                ctx.Arc(x + 4.3 * u, y + 4.3 * u, 2.5 * u, 0, Math.PI * 2); ctx.Stroke(); ctx.MoveTo(x + 6.1 * u, y + 6.1 * u); ctx.LineTo(x + 8.4 * u, y + 8.4 * u); ctx.Stroke(); return true;
            case "armor":
                Polygon(ctx, (x + 2 * u, y + 3 * u), (x + 3.5 * u, y + 1.6 * u), (cx, y + 2.5 * u), (x + 6.5 * u, y + 1.6 * u), (x + 8 * u, y + 3 * u), (x + 7 * u, y + 8.4 * u), (x + 3 * u, y + 8.4 * u)); ctx.Stroke(); return true;
            case "materials": case "ingot":
                Polygon(ctx, (x + 1.5 * u, y + 6.8 * u), (x + 3.1 * u, y + 3.1 * u), (x + 7.2 * u, y + 3.1 * u), (x + 8.5 * u, y + 6.8 * u)); ctx.ClosePath(); ctx.Stroke(); ctx.MoveTo(x + 3.1 * u, y + 3.1 * u); ctx.LineTo(x + 4 * u, y + 6.8 * u); ctx.Stroke(); return true;
            case "consumables": case "flask":
                ctx.MoveTo(x + 4 * u, y + 1.5 * u); ctx.LineTo(x + 6 * u, y + 1.5 * u); ctx.MoveTo(x + 4.5 * u, y + 1.5 * u); ctx.LineTo(x + 4.5 * u, y + 4 * u); ctx.CurveTo(x + 1.6 * u, y + 7.8 * u, x + 3.2 * u, y + 8.6 * u, cx, y + 8.6 * u); ctx.CurveTo(x + 6.8 * u, y + 8.6 * u, x + 8.4 * u, y + 7.8 * u, x + 5.5 * u, y + 4 * u); ctx.LineTo(x + 5.5 * u, y + 1.5 * u); ctx.Stroke(); return true;
            case "quest": case "key":
                ctx.Arc(x + 3.2 * u, y + 3.5 * u, 1.7 * u, 0, Math.PI * 2); ctx.Stroke(); ctx.MoveTo(x + 4.5 * u, y + 4.7 * u); ctx.LineTo(x + 8.5 * u, y + 8.3 * u); ctx.MoveTo(x + 6.6 * u, y + 6.6 * u); ctx.LineTo(x + 7.6 * u, y + 5.6 * u); ctx.Stroke(); return true;
            case "filter":
                Polygon(ctx, (x + 1.5 * u, y + 2 * u), (x + 8.5 * u, y + 2 * u), (x + 6 * u, y + 5.1 * u), (x + 6 * u, y + 8.3 * u), (x + 4 * u, y + 7.2 * u), (x + 4 * u, y + 5.1 * u)); ctx.Stroke(); return true;
            case "sort":
                ctx.MoveTo(x + 3.2 * u, y + 1.7 * u); ctx.LineTo(x + 3.2 * u, y + 8.3 * u); ctx.MoveTo(x + 1.7 * u, y + 3.2 * u); ctx.LineTo(x + 3.2 * u, y + 1.7 * u); ctx.LineTo(x + 4.7 * u, y + 3.2 * u); ctx.MoveTo(x + 6.8 * u, y + 8.3 * u); ctx.LineTo(x + 6.8 * u, y + 1.7 * u); ctx.MoveTo(x + 5.3 * u, y + 6.8 * u); ctx.LineTo(x + 6.8 * u, y + 8.3 * u); ctx.LineTo(x + 8.3 * u, y + 6.8 * u); ctx.Stroke(); return true;
            case "capacity":
                ctx.Rectangle(x + 2 * u, y + 3 * u, 6 * u, 5.5 * u); ctx.Stroke(); ctx.MoveTo(x + 3.2 * u, y + 3 * u); ctx.CurveTo(x + 3.2 * u, y + 1.4 * u, x + 6.8 * u, y + 1.4 * u, x + 6.8 * u, y + 3 * u); ctx.Stroke(); return true;
            case "strength":
                ctx.MoveTo(x + 2 * u, y + 7.8 * u); ctx.LineTo(x + 7.8 * u, y + 2.2 * u); ctx.MoveTo(x + 2.1 * u, y + 5.9 * u); ctx.LineTo(x + 4.1 * u, y + 7.9 * u); ctx.MoveTo(x + 5.9 * u, y + 2.1 * u); ctx.LineTo(x + 7.9 * u, y + 4.1 * u); ctx.Stroke(); return true;
            case "dexterity": case "evasion":
                ctx.MoveTo(x + 1.5 * u, y + 7.5 * u); ctx.CurveTo(x + 3.2 * u, y + 2 * u, x + 6 * u, y + 2 * u, x + 8.5 * u, y + 1.6 * u); ctx.CurveTo(x + 8 * u, y + 4.7 * u, x + 5.5 * u, y + 7.4 * u, x + 1.5 * u, y + 7.5 * u); ctx.Stroke(); ctx.MoveTo(x + 2.5 * u, y + 8.5 * u); ctx.LineTo(x + 6.5 * u, y + 4.3 * u); ctx.Stroke(); return true;
            case "intelligence":
                ctx.Arc(cx, y + 4 * u, 2.7 * u, Math.PI, 0); ctx.LineTo(x + 6.5 * u, y + 6.8 * u); ctx.LineTo(x + 3.5 * u, y + 6.8 * u); ctx.ClosePath(); ctx.Stroke(); ctx.MoveTo(x + 4 * u, y + 8.4 * u); ctx.LineTo(x + 6 * u, y + 8.4 * u); ctx.Stroke(); return true;
            case "available_point":
                ctx.Arc(cx, cy, 3.3 * u, 0, Math.PI * 2); ctx.Stroke(); ctx.MoveTo(cx, y + 3 * u); ctx.LineTo(cx, y + 7 * u); ctx.MoveTo(x + 3 * u, cy); ctx.LineTo(x + 7 * u, cy); ctx.Stroke(); return true;
            case "level":
                Polygon(ctx, (cx, y + 1.5 * u), (x + 6 * u, y + 4 * u), (x + 8.7 * u, y + 4.1 * u), (x + 6.6 * u, y + 6 * u), (x + 7.4 * u, y + 8.7 * u), (cx, y + 7.2 * u), (x + 2.6 * u, y + 8.7 * u), (x + 3.4 * u, y + 6 * u), (x + 1.3 * u, y + 4.1 * u), (x + 4 * u, y + 4 * u)); ctx.ClosePath(); ctx.Stroke(); return true;
            case "lock":
                ctx.Rectangle(x + 2.3 * u, y + 4.3 * u, 5.4 * u, 4.2 * u); ctx.Stroke(); ctx.Arc(cx, y + 4.3 * u, 2 * u, Math.PI, 0); ctx.Stroke(); return true;
            case "cooldown":
                ctx.Arc(cx, cy, 3.5 * u, 0, Math.PI * 2); ctx.Stroke(); ctx.MoveTo(cx, cy); ctx.LineTo(cx, y + 2.6 * u); ctx.MoveTo(cx, cy); ctx.LineTo(x + 6.8 * u, y + 6 * u); ctx.Stroke(); return true;
            case "range": case "radius":
                ctx.Arc(cx, cy, 3.5 * u, 0, Math.PI * 2); ctx.Stroke(); ctx.Arc(cx, cy, 1.5 * u, 0, Math.PI * 2); ctx.Stroke(); return true;
            case "resource_cost":
                ctx.MoveTo(cx, y + 1.3 * u); ctx.CurveTo(x + 8.2 * u, y + 5 * u, x + 7.2 * u, y + 8.5 * u, cx, y + 8.5 * u); ctx.CurveTo(x + 2.8 * u, y + 8.5 * u, x + 1.8 * u, y + 5 * u, cx, y + 1.3 * u); ctx.Stroke(); return true;
            case "delivery":
                ctx.MoveTo(x + 1.5 * u, cy); ctx.LineTo(x + 8.5 * u, cy); ctx.MoveTo(x + 6 * u, y + 2.5 * u); ctx.LineTo(x + 8.5 * u, cy); ctx.LineTo(x + 6 * u, y + 7.5 * u); ctx.Stroke(); return true;
            case "combat_slot":
                ctx.Rectangle(x + 1.6 * u, y + 1.6 * u, 6.8 * u, 6.8 * u); ctx.Stroke(); ctx.Arc(cx, cy, 1.4 * u, 0, Math.PI * 2); ctx.Stroke(); return true;
            case "status":
                ctx.Arc(cx, cy, 3.4 * u, 0, Math.PI * 2); ctx.Stroke(); ctx.MoveTo(x + 3.2 * u, cy); ctx.LineTo(x + 4.5 * u, y + 6.3 * u); ctx.LineTo(x + 7 * u, y + 3.2 * u); ctx.Stroke(); return true;
            case "dungeon":
                ctx.Rectangle(x + 1.5 * u, y + 3 * u, 7 * u, 5.5 * u); ctx.Stroke(); ctx.Rectangle(x + 4 * u, y + 5 * u, 2 * u, 3.5 * u); ctx.Stroke(); ctx.MoveTo(x + 2 * u, y + 3 * u); ctx.LineTo(x + 2 * u, y + 1.7 * u); ctx.LineTo(x + 3.5 * u, y + 1.7 * u); ctx.LineTo(x + 3.5 * u, y + 3 * u); ctx.MoveTo(x + 6.5 * u, y + 3 * u); ctx.LineTo(x + 6.5 * u, y + 1.7 * u); ctx.LineTo(x + 8 * u, y + 1.7 * u); ctx.LineTo(x + 8 * u, y + 3 * u); ctx.Stroke(); return true;
            case "mechanics":
                ctx.Arc(cx, cy, 2.5 * u, 0, Math.PI * 2); ctx.Stroke(); for (int i = 0; i < 6; i++) { double a = i * Math.PI / 3; ctx.MoveTo(cx + Math.Cos(a) * 2.4 * u, cy + Math.Sin(a) * 2.4 * u); ctx.LineTo(cx + Math.Cos(a) * 3.7 * u, cy + Math.Sin(a) * 3.7 * u); } ctx.Stroke(); return true;
            case "document":
                ctx.MoveTo(x + 2.2 * u, y + 1.3 * u); ctx.LineTo(x + 6.3 * u, y + 1.3 * u); ctx.LineTo(x + 7.8 * u, y + 2.8 * u); ctx.LineTo(x + 7.8 * u, y + 8.7 * u); ctx.LineTo(x + 2.2 * u, y + 8.7 * u); ctx.ClosePath(); ctx.Stroke(); ctx.MoveTo(x + 3.5 * u, y + 4.5 * u); ctx.LineTo(x + 6.5 * u, y + 4.5 * u); ctx.MoveTo(x + 3.5 * u, y + 6 * u); ctx.LineTo(x + 6.5 * u, y + 6 * u); ctx.Stroke(); return true;
            default: return false;
        }
    }

    private static void DrawFallback(Context ctx, double cx, double cy, double unit)
    {
        Polygon(ctx, (cx, cy - 3.4 * unit), (cx + 3.4 * unit, cy), (cx, cy + 3.4 * unit), (cx - 3.4 * unit, cy));
        ctx.Stroke();
        FillCircle(ctx, cx, cy, .75 * unit);
    }

    private static string Normalize(string value)
    {
        string safe = (value ?? "").Trim().ToLowerInvariant();
        int colon = safe.IndexOf(':');
        if (colon >= 0) safe = safe.Substring(colon + 1);
        return safe.Replace('-', '_').Replace(' ', '_');
    }

}
