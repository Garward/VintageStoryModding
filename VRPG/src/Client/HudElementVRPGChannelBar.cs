using System;
using Cairo;
using VRPG.Network;
using Vintagestory.API.Client;

namespace VRPG.Client;

/// <summary>Displays the server-authoritative lifetime of the player's active channel.</summary>
public sealed class HudElementVRPGChannelBar : HudElement
{
    private const int Width = 430;
    private const int Height = 54;

    private LoadedTexture texture;
    private bool active;
    private string skillName = "";
    private string color = "#ff9f0d";
    private long startedMilliseconds;
    private long lastRedrawMilliseconds;
    private float maxDurationSeconds;

    public HudElementVRPGChannelBar(ICoreClientAPI capi) : base(capi)
    {
        texture = new LoadedTexture(capi);
        TryOpen();
    }

    public override string? ToggleKeyCombinationCode => null;
    public override bool Focusable => false;
    public override bool ShouldReceiveKeyboardEvents() => false;
    public override bool ShouldReceiveRenderEvents() => true;
    public override bool TryClose() => false;

    public void SetState(SkillChannelStatePacket packet)
    {
        active = packet.Active;
        if (!active)
        {
            return;
        }

        skillName = packet.SkillName;
        color = packet.Color;
        maxDurationSeconds = Math.Max(0.1f, packet.MaxDurationSeconds);
        startedMilliseconds = capi.ElapsedMilliseconds;
        lastRedrawMilliseconds = 0;
    }

    public override void OnRenderGUI(float deltaTime)
    {
        _ = deltaTime;
        if (!active)
        {
            return;
        }

        long now = capi.ElapsedMilliseconds;
        double elapsedSeconds = (now - startedMilliseconds) / 1000.0;
        if (elapsedSeconds >= maxDurationSeconds + 0.25f)
        {
            active = false;
            return;
        }

        if (now - lastRedrawMilliseconds >= 33)
        {
            Redraw(Math.Clamp(elapsedSeconds / maxDurationSeconds, 0.0, 1.0));
            lastRedrawMilliseconds = now;
        }

        if (texture.TextureId <= 0)
        {
            return;
        }

        float x = (capi.Render.FrameWidth - Width) * 0.5f;
        float y = capi.Render.FrameHeight - 300f;
        capi.Render.Render2DLoadedTexture(texture, x, y, 2260f);
    }

    private void Redraw(double progress)
    {
        double[] accent = ParseColor(color);
        using var surface = new ImageSurface((Format)0, Width, Height);
        using var ctx = new Context(surface);
        ctx.Operator = Operator.Clear;
        ctx.Paint();
        ctx.Operator = Operator.Over;

        RoundedRect(ctx, 1, 1, Width - 2, Height - 2, 6);
        ctx.SetSourceRGBA(0.025, 0.014, 0.012, 0.92);
        ctx.FillPreserve();
        ctx.SetSourceRGBA(accent[0], accent[1], accent[2], 0.88);
        ctx.LineWidth = 2;
        ctx.Stroke();

        ctx.SelectFontFace("Arial", FontSlant.Normal, FontWeight.Bold);
        ctx.SetFontSize(14);
        string label = "CHANNELING  •  " + skillName.ToUpperInvariant();
        TextExtents ext = ctx.TextExtents(label);
        ctx.SetSourceRGBA(0.98, 0.92, 0.82, 1);
        ctx.MoveTo((Width - ext.Width) * 0.5 - ext.XBearing, 21);
        ctx.ShowText(label);

        const double barX = 15;
        const double barY = 32;
        const double barWidth = Width - 30;
        const double barHeight = 10;
        RoundedRect(ctx, barX, barY, barWidth, barHeight, 3);
        ctx.SetSourceRGBA(0.14, 0.08, 0.055, 1);
        ctx.Fill();
        if (progress > 0)
        {
            RoundedRect(ctx, barX, barY, barWidth * progress, barHeight, 3);
            ctx.SetSourceRGBA(accent[0], accent[1], accent[2], 0.96);
            ctx.Fill();
        }

        capi.Gui.LoadOrUpdateCairoTexture(surface, false, ref texture);
    }

    private static void RoundedRect(Context ctx, double x, double y, double width, double height, double radius)
    {
        double right = x + width;
        double bottom = y + height;
        ctx.NewSubPath();
        ctx.Arc(right - radius, y + radius, radius, -Math.PI / 2, 0);
        ctx.Arc(right - radius, bottom - radius, radius, 0, Math.PI / 2);
        ctx.Arc(x + radius, bottom - radius, radius, Math.PI / 2, Math.PI);
        ctx.Arc(x + radius, y + radius, radius, Math.PI, Math.PI * 1.5);
        ctx.ClosePath();
    }

    private static double[] ParseColor(string value)
    {
        string hex = (value ?? "").Trim().TrimStart('#');
        if (hex.Length >= 6
            && int.TryParse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber, null, out int r)
            && int.TryParse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber, null, out int g)
            && int.TryParse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber, null, out int b))
        {
            return new[] { r / 255.0, g / 255.0, b / 255.0 };
        }

        return new[] { 1.0, 0.62, 0.06 };
    }

    public override void Dispose()
    {
        texture.Dispose();
        base.Dispose();
    }
}
