using System;
using Cairo;
using Vintagestory.API.Client;

namespace VRPG.Client;

public sealed class HudElementVRPGWindowPulse : HudElement
{
    private LoadedTexture texture;
    private long startedMs = -1;
    private long endsMs;
    private int colorRgba;

    public override string? ToggleKeyCombinationCode => null;
    public override bool Focusable => false;

    public HudElementVRPGWindowPulse(ICoreClientAPI capi) : base(capi)
    {
        texture = new LoadedTexture(capi);
        TryOpen();
    }

    public override bool ShouldReceiveKeyboardEvents() => false;
    public override bool ShouldReceiveRenderEvents() => true;
    public override bool TryClose() => false;

    public void Trigger(int colorRgba, float durationSeconds)
    {
        this.colorRgba = colorRgba;
        startedMs = capi.ElapsedMilliseconds;
        endsMs = startedMs + (long)(Math.Max(0.4f, durationSeconds) * 1000f);
    }

    public override void OnRenderGUI(float deltaTime)
    {
        long nowMs = capi.ElapsedMilliseconds;
        if (startedMs < 0 || nowMs >= endsMs)
        {
            return;
        }

        int frameWidth = Math.Max(1, capi.Render.FrameWidth);
        int frameHeight = Math.Max(1, capi.Render.FrameHeight);
        double cx = frameWidth / 2.0;
        double cy = frameHeight / 2.0 + 70.0;

        // Repeating 600 ms expanding ring while the window is open.
        double phase = ((nowMs - startedMs) % 600) / 600.0;
        double radius = 10.0 + 22.0 * phase;
        double alpha = 0.85 * (1.0 - phase);

        using var surface = new ImageSurface((Format)0, frameWidth, frameHeight);
        using var ctx = new Context(surface);
        ctx.Operator = Operator.Clear;
        ctx.Paint();
        ctx.Operator = Operator.Over;
        ctx.SetSourceRGBA(
            ((colorRgba >> 16) & 0xff) / 255.0,
            ((colorRgba >> 8) & 0xff) / 255.0,
            (colorRgba & 0xff) / 255.0,
            alpha);
        ctx.LineWidth = 3.0;
        ctx.Arc(cx, cy, radius, 0, Math.PI * 2);
        ctx.Stroke();

        capi.Gui.LoadOrUpdateCairoTexture(surface, false, ref texture);
        if (texture.TextureId > 0)
        {
            capi.Render.Render2DLoadedTexture(texture, 0, 0, 2260f);
        }
    }

    public override void Dispose()
    {
        texture.Dispose();
        base.Dispose();
    }
}
