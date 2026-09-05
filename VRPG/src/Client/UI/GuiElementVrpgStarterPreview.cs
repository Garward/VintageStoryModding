using System;
using System.Collections.Generic;
using System.Linq;
using Cairo;
using VRPG.Network;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VRPG.Client.UI;

/// <summary>Focused first-run class comparison; item mutation remains server-owned.</summary>
public sealed class GuiElementVrpgStarterPreview : GuiElement
{
    private readonly OnboardingStatePacket packet;
    private readonly Action close;
    private int selectedIndex;
    private int hoverIndex = -1;
    private int textureId;

    public GuiElementVrpgStarterPreview(
        ICoreClientAPI api,
        ElementBounds bounds,
        OnboardingStatePacket packet,
        Action close) : base(api, bounds)
    {
        this.packet = packet;
        this.close = close;
        MouseOverCursor = "pointer";
    }

    public override bool Focusable => true;

    public override void ComposeElements(Context ctxStatic, ImageSurface surfaceStatic)
    {
        Bounds.CalcWorldBounds();
        Redraw();
    }

    public override void RenderInteractiveElements(float deltaTime)
    {
        if (textureId > 0)
        {
            api.Render.Render2DTexturePremultipliedAlpha(textureId, Bounds);
        }
    }

    public override void OnMouseMove(ICoreClientAPI api, MouseEvent args)
    {
        int next = ChoiceAt(args.X, args.Y);
        if (next != hoverIndex)
        {
            hoverIndex = next;
            Redraw();
        }
    }

    public override void OnMouseUpOnElement(ICoreClientAPI api, MouseEvent args)
    {
        if (args.Button != EnumMouseButton.Left)
        {
            return;
        }

        if (CloseAt(args.X, args.Y))
        {
            close();
            args.Handled = true;
            api.Gui.PlaySound("menubutton_press");
            return;
        }

        int choice = ChoiceAt(args.X, args.Y);
        if (choice >= 0)
        {
            selectedIndex = choice;
            Redraw();
            args.Handled = true;
            api.Gui.PlaySound("menubutton_press");
        }
    }

    public override void Dispose()
    {
        base.Dispose();
        if (textureId > 0)
        {
            api.Render.GLDeleteTexture(textureId);
            textureId = 0;
        }
    }

    private void Redraw()
    {
        if (Bounds.OuterWidthInt <= 0 || Bounds.OuterHeightInt <= 0)
        {
            return;
        }

        using ImageSurface surface = new ImageSurface((Format)0, Bounds.OuterWidthInt, Bounds.OuterHeightInt);
        using Context ctx = genContext(surface);
        Draw(ctx, Bounds.OuterWidth, Bounds.OuterHeight);
        generateTexture(surface, ref textureId);
    }

    private void Draw(Context ctx, double width, double height)
    {
        DrawBackdrop(ctx, width, height);
        DrawText(ctx, "CHOOSE A STARTER", scaled(28), scaled(38), scaled(21), true, Gold());
        DrawText(ctx, "Pick the first combat style you want to try. You can change your build later.",
            scaled(28), scaled(65), scaled(13), false, Muted());
        DrawClose(ctx, width - scaled(27), scaled(27));

        double listX = scaled(28);
        double listY = scaled(91);
        double listW = scaled(270);
        double paneH = height - scaled(143);
        DrawPane(ctx, listX, listY, listW, paneH);
        DrawText(ctx, "Combat styles", listX + scaled(14), listY + scaled(25), scaled(14), true, Text());

        for (int i = 0; i < packet.Starters.Length; i++)
        {
            StarterChoicePacket choice = packet.Starters[i];
            double y = listY + scaled(42 + i * 68);
            DrawChoice(ctx, listX + scaled(9), y, listW - scaled(18), scaled(58), choice, i == selectedIndex, i == hoverIndex);
        }

        double detailX = scaled(314);
        double detailW = width - detailX - scaled(28);
        DrawPane(ctx, detailX, listY, detailW, paneH);
        DrawDetails(ctx, detailX, listY, detailW);
        DrawStatus(ctx, width, height);
    }

    private void DrawDetails(Context ctx, double x, double y, double width)
    {
        if (packet.Starters.Length == 0)
        {
            DrawText(ctx, "No starter choices are available.", x + scaled(18), y + scaled(34), scaled(16), true, Text());
            return;
        }

        selectedIndex = Math.Clamp(selectedIndex, 0, packet.Starters.Length - 1);
        StarterChoicePacket choice = packet.Starters[selectedIndex];
        double cursor = y + scaled(34);
        DrawText(ctx, choice.Name, x + scaled(18), cursor, scaled(22), true, Gold());
        cursor += scaled(29);
        cursor = DrawWrapped(ctx, choice.Description, x + scaled(18), cursor, width - scaled(36), scaled(13), Text(), 3);

        cursor += scaled(16);
        cursor = DrawSection(ctx, "HOW IT PLAYS", choice.CombatPattern, x, cursor, width);
        cursor += scaled(10);
        cursor = DrawSection(ctx, "HOW IT SURVIVES", choice.DefensiveApproach, x, cursor, width);
        cursor += scaled(13);

        DrawText(ctx, "STARTS WITH", x + scaled(18), cursor, scaled(12), true, Muted());
        cursor += scaled(23);
        cursor = DrawGrant(ctx, "Weapon", choice.WeaponName, x, cursor, width);
        cursor = DrawGrant(ctx, "Armor", string.Join(", ", choice.ArmorNames), x, cursor, width);
        cursor = DrawGrant(ctx, "Skill", choice.SkillName, x, cursor, width);
        cursor += scaled(8);
        DrawWrapped(ctx, choice.SkillDescription, x + scaled(105), cursor, width - scaled(123), scaled(12), Muted(), 3);

        DrawText(ctx, "Suggested attribute", x + scaled(18), y + scaled(389), scaled(12), true, Muted());
        DrawText(ctx, Humanize(choice.PrimaryAttribute), x + scaled(144), y + scaled(389), scaled(12), true, Text());
    }

    private double DrawSection(Context ctx, string heading, string body, double x, double y, double width)
    {
        DrawText(ctx, heading, x + scaled(18), y, scaled(12), true, Muted());
        return DrawWrapped(ctx, body, x + scaled(18), y + scaled(20), width - scaled(36), scaled(13), Text(), 2);
    }

    private double DrawGrant(Context ctx, string label, string value, double x, double y, double width)
    {
        DrawText(ctx, label, x + scaled(18), y, scaled(12), true, Muted());
        DrawText(ctx, value, x + scaled(105), y, scaled(12), false, Text(), width - scaled(123));
        return y + scaled(22);
    }

    private void DrawStatus(Context ctx, double width, double height)
    {
        double y = height - scaled(38);
        ctx.SetSourceRGBA(1, 0.62, 0.05, 0.28);
        ctx.MoveTo(scaled(28), y - scaled(15));
        ctx.LineTo(width - scaled(28), y - scaled(15));
        ctx.Stroke();
        DrawText(ctx, "PREVIEW", scaled(28), y + scaled(4), scaled(12), true, Gold());
        DrawText(ctx, packet.StatusMessage, scaled(120), y + scaled(4), scaled(12), false, Muted(), width - scaled(148));
    }

    private void DrawChoice(Context ctx, double x, double y, double width, double height, StarterChoicePacket choice, bool selected, bool hover)
    {
        RoundedRectangle(ctx, x, y, width, height, scaled(3));
        ctx.SetSourceRGBA(selected ? 0.30 : 0.09, selected ? 0.10 : 0.04, 0.025, selected ? 0.96 : hover ? 0.82 : 0.58);
        ctx.FillPreserve();
        ctx.LineWidth = scaled(selected ? 2 : 1);
        ctx.SetSourceRGBA(1, 0.62, 0.05, selected ? 0.92 : hover ? 0.58 : 0.28);
        ctx.Stroke();
        ctx.SetSourceRGBA(1, 0.62, 0.05, selected ? 0.94 : 0.44);
        ctx.Rectangle(x, y, scaled(5), height);
        ctx.Fill();
        DrawText(ctx, choice.Name, x + scaled(16), y + scaled(23), scaled(15), true, selected ? Gold() : Text(), width - scaled(28));
        DrawText(ctx, Humanize(choice.PrimaryAttribute), x + scaled(16), y + scaled(44), scaled(11), false, Muted(), width - scaled(28));
    }

    private void DrawBackdrop(Context ctx, double width, double height)
    {
        RoundedRectangle(ctx, scaled(2), scaled(2), width - scaled(4), height - scaled(4), scaled(3));
        using LinearGradient gradient = new LinearGradient(0, 0, width, height);
        gradient.AddColorStop(0, new Color(0.075, 0.030, 0.022, 0.99));
        gradient.AddColorStop(0.55, new Color(0.17, 0.055, 0.028, 0.99));
        gradient.AddColorStop(1, new Color(0.045, 0.025, 0.021, 0.99));
        ctx.SetSource(gradient);
        ctx.FillPreserve();
        ctx.LineWidth = scaled(2);
        ctx.SetSourceRGBA(VrpgGuiTheme.BorderR, VrpgGuiTheme.BorderG, VrpgGuiTheme.BorderB, 0.95);
        ctx.Stroke();
    }

    private void DrawPane(Context ctx, double x, double y, double width, double height)
    {
        RoundedRectangle(ctx, x, y, width, height, scaled(3));
        ctx.SetSourceRGBA(0.025, 0.018, 0.016, 0.63);
        ctx.FillPreserve();
        ctx.SetSourceRGBA(0.95, 0.58, 0.12, 0.42);
        ctx.Stroke();
    }

    private void DrawClose(Context ctx, double x, double y)
    {
        ctx.LineWidth = scaled(2);
        ctx.SetSourceRGBA(0.98, 0.88, 0.70, 0.92);
        ctx.MoveTo(x - scaled(7), y - scaled(7));
        ctx.LineTo(x + scaled(7), y + scaled(7));
        ctx.MoveTo(x + scaled(7), y - scaled(7));
        ctx.LineTo(x - scaled(7), y + scaled(7));
        ctx.Stroke();
    }

    private int ChoiceAt(double mouseX, double mouseY)
    {
        double x = mouseX - Bounds.absX;
        double y = mouseY - Bounds.absY;
        if (x < scaled(37) || x > scaled(289) || y < scaled(133)) return -1;
        int index = (int)((y - scaled(133)) / scaled(68));
        double rowY = scaled(133 + index * 68);
        return index >= 0 && index < packet.Starters.Length && y <= rowY + scaled(58) ? index : -1;
    }

    private bool CloseAt(double mouseX, double mouseY)
    {
        double x = mouseX - Bounds.absX;
        double y = mouseY - Bounds.absY;
        return Math.Abs(x - (Bounds.OuterWidth - scaled(27))) <= scaled(18) && Math.Abs(y - scaled(27)) <= scaled(18);
    }

    private double DrawWrapped(Context ctx, string value, double x, double y, double width, double size, double[] color, int maxLines)
    {
        foreach (string line in Wrap(ctx, value, width, size).Take(maxLines))
        {
            DrawText(ctx, line, x, y, size, false, color);
            y += size * 1.32;
        }
        return y;
    }

    private static IEnumerable<string> Wrap(Context ctx, string value, double width, double size)
    {
        SelectFont(ctx, size, false);
        string current = "";
        foreach (string word in (value ?? "").Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string next = current.Length == 0 ? word : current + " " + word;
            if (ctx.TextExtents(next).Width > width && current.Length > 0)
            {
                yield return current;
                current = word;
            }
            else current = next;
        }
        if (current.Length > 0) yield return current;
    }

    private static void DrawText(Context ctx, string value, double x, double y, double size, bool bold, double[] color, double maxWidth = 0)
    {
        if (string.IsNullOrEmpty(value)) return;
        SelectFont(ctx, size, bold);
        string text = value;
        if (maxWidth > 0 && ctx.TextExtents(text).Width > maxWidth)
        {
            while (text.Length > 1 && ctx.TextExtents(text + "...").Width > maxWidth) text = text[..^1];
            text = text.TrimEnd() + "...";
        }
        ctx.SetSourceRGBA(0, 0, 0, 0.45);
        ctx.MoveTo(x + 1, y + 1);
        ctx.ShowText(text);
        ctx.SetSourceRGBA(color[0], color[1], color[2], color[3]);
        ctx.MoveTo(x, y);
        ctx.ShowText(text);
    }

    private static void SelectFont(Context ctx, double size, bool bold)
    {
        ctx.SelectFontFace("Lora", FontSlant.Normal, bold ? FontWeight.Bold : FontWeight.Normal);
        ctx.SetFontSize(size);
    }

    private static string Humanize(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "Unspecified";
        string text = value.Trim().Replace('_', ' ');
        return char.ToUpperInvariant(text[0]) + text[1..];
    }

    private static void RoundedRectangle(Context ctx, double x, double y, double width, double height, double radius)
    {
        radius = Math.Min(radius, Math.Min(width, height) / 2);
        ctx.NewSubPath();
        ctx.Arc(x + width - radius, y + radius, radius, -Math.PI / 2, 0);
        ctx.Arc(x + width - radius, y + height - radius, radius, 0, Math.PI / 2);
        ctx.Arc(x + radius, y + height - radius, radius, Math.PI / 2, Math.PI);
        ctx.Arc(x + radius, y + radius, radius, Math.PI, Math.PI * 1.5);
        ctx.ClosePath();
    }

    private static double[] Gold() => new[] { VrpgGuiTheme.GoldR, VrpgGuiTheme.GoldG, VrpgGuiTheme.GoldB, 1.0 };
    private static double[] Text() => new[] { VrpgGuiTheme.TextR, VrpgGuiTheme.TextG, VrpgGuiTheme.TextB, 1.0 };
    private static double[] Muted() => new[] { VrpgGuiTheme.MutedR, VrpgGuiTheme.MutedG, VrpgGuiTheme.MutedB, 1.0 };
}
