using System;
using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace VRPG.Client.Interactions;

/// <summary>Shows one selected entity action and its immediate server result.</summary>
public sealed class HudElementVRPGInteractionPrompt : HudElement
{
    private const int LogicalWidth = 360;
    private LoadedTexture texture;
    private InteractionPromptState state = InteractionPromptState.Hidden;
    private double lastScale;
    private int textureWidth;
    private int textureHeight;

    public HudElementVRPGInteractionPrompt(ICoreClientAPI capi) : base(capi)
    {
        texture = new LoadedTexture(capi);
    }

    public override string? ToggleKeyCombinationCode => null;
    public override bool Focusable => false;
    public override bool TryClose() => false;
    public override bool ShouldReceiveKeyboardEvents() => false;
    public override bool ShouldReceiveMouseEvents() => false;

    public void SetState(InteractionPromptState next)
    {
        if (next == state) return;
        state = next;
        Redraw();
    }

    public override void OnRenderGUI(float deltaTime)
    {
        _ = deltaTime;
        if (state.Phase == InteractionPromptPhase.Hidden)
        {
            return;
        }

        double scale = Math.Clamp(RuntimeEnv.GUIScale, 0.75, 2.5);
        if (Math.Abs(scale - lastScale) > 0.001)
        {
            Redraw();
        }
        if (texture.TextureId <= 0) return;

        float x = (capi.Render.FrameWidth - textureWidth) * 0.5f;
        float y = capi.Render.FrameHeight * 0.56f;
        capi.Render.Render2DLoadedTexture(texture, x, y, 2265f);
    }

    private void Redraw()
    {
        if (state.Phase == InteractionPromptPhase.Hidden) return;

        lastScale = Math.Clamp(RuntimeEnv.GUIScale, 0.75, 2.5);
        bool hasMessage = !string.IsNullOrWhiteSpace(state.Message);
        textureWidth = Math.Max(1, (int)Math.Round(LogicalWidth * lastScale));
        textureHeight = Math.Max(1, (int)Math.Round((hasMessage ? 56 : 38) * lastScale));
        using var surface = new ImageSurface(Format.Argb32, textureWidth, textureHeight);
        using var ctx = new Context(surface);
        ctx.Scale(lastScale, lastScale);

        double[] accent = state.Phase switch
        {
            InteractionPromptPhase.Success => new[] { 0.46, 0.78, 0.34 },
            InteractionPromptPhase.Rejected => new[] { 0.82, 0.29, 0.19 },
            InteractionPromptPhase.Pending => new[] { 0.68, 0.45, 0.20 },
            _ => new[] { 1.0, 0.58, 0.04 }
        };
        RoundedRect(ctx, 1, 1, LogicalWidth - 2, hasMessage ? 54 : 36, 5);
        ctx.SetSourceRGBA(0.025, 0.025, 0.022, 0.91);
        ctx.FillPreserve();
        ctx.SetSourceRGBA(accent[0], accent[1], accent[2], 0.92);
        ctx.LineWidth = 1.5;
        ctx.Stroke();

        ctx.SelectFontFace("Arial", FontSlant.Normal, FontWeight.Bold);
        ctx.SetFontSize(13);
        string heading = string.IsNullOrWhiteSpace(state.Target)
            ? state.Action
            : state.Action + " — " + state.Target;
        DrawCenteredEllipsized(ctx, heading, 10, hasMessage ? 23 : 24, LogicalWidth - 20);

        if (hasMessage)
        {
            ctx.SelectFontFace("Arial", FontSlant.Normal, FontWeight.Normal);
            ctx.SetFontSize(11);
            ctx.SetSourceRGBA(accent[0], accent[1], accent[2], 1);
            DrawCenteredEllipsized(ctx, state.Message, 10, 43, LogicalWidth - 20);
        }

        capi.Gui.LoadOrUpdateCairoTexture(surface, false, ref texture);
    }

    private static void DrawCenteredEllipsized(Context ctx, string text, double x, double baseline, double width)
    {
        string fitted = text ?? "";
        while (fitted.Length > 1 && ctx.TextExtents(fitted).Width > width)
        {
            fitted = fitted.Substring(0, fitted.Length - 1).TrimEnd();
        }
        if (!string.Equals(fitted, text, StringComparison.Ordinal) && fitted.Length > 1)
        {
            fitted = fitted.TrimEnd('…', '.', ' ') + "…";
        }
        TextExtents extents = ctx.TextExtents(fitted);
        ctx.SetSourceRGBA(0.96, 0.91, 0.82, 1);
        ctx.MoveTo(x + (width - extents.Width) * 0.5 - extents.XBearing, baseline);
        ctx.ShowText(fitted);
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

    public override void Dispose()
    {
        texture.Dispose();
        base.Dispose();
    }
}
