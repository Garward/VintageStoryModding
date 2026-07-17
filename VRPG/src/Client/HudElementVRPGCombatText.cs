using System;
using Cairo;
using VRPG.Client.Visuals;
using VRPG.Network;
using Vintagestory.API.Client;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace VRPG.Client;

public sealed class HudElementVRPGCombatText : HudElement
{
    private readonly VisualDirector director;
    private LoadedTexture texture;

    public override string? ToggleKeyCombinationCode => null;
    public override bool Focusable => false;

    public HudElementVRPGCombatText(ICoreClientAPI capi, VisualDirector director) : base(capi)
    {
        this.director = director;
        texture = new LoadedTexture(capi);
        TryOpen();
    }

    public override bool ShouldReceiveKeyboardEvents() => false;
    public override bool ShouldReceiveRenderEvents() => true;
    public override bool TryClose() => false;

    public override void OnRenderGUI(float deltaTime)
    {
        long nowMs = capi.ElapsedMilliseconds;
        director.CombatText.Tick(nowMs);
        if (!director.Config.CombatTextEnabled || director.CombatText.Entries.Count == 0)
        {
            return;
        }

        int frameWidth = Math.Max(1, capi.Render.FrameWidth);
        int frameHeight = Math.Max(1, capi.Render.FrameHeight);
        using var surface = new ImageSurface((Format)0, frameWidth, frameHeight);
        using var ctx = new Context(surface);
        ctx.Operator = Operator.Clear;
        ctx.Paint();
        ctx.Operator = Operator.Over;

        foreach (CombatTextEntry entry in director.CombatText.Entries)
        {
            DrawEntry(ctx, entry, nowMs, frameWidth, frameHeight);
        }

        capi.Gui.LoadOrUpdateCairoTexture(surface, false, ref texture);
        if (texture.TextureId > 0)
        {
            capi.Render.Render2DLoadedTexture(texture, 0, 0, 2300f);
        }
    }

    private void DrawEntry(Context ctx, CombatTextEntry entry, long nowMs, int frameWidth, int frameHeight)
    {
        Vec3d anchor = ResolveAnchor(entry);
        Vec3d screen = MatrixToolsd.Project(anchor,
            capi.Render.PerspectiveProjectionMat, capi.Render.PerspectiveViewMat, frameWidth, frameHeight);
        if (screen.Z < 0)
        {
            return;
        }

        long lifetime = Math.Max(1, entry.ExpiresAtMs - entry.CreatedMs);
        float age = Math.Clamp((nowMs - entry.CreatedMs) / (float)lifetime, 0f, 1f);
        double rise = 34.0 * age;
        double alpha = age < 0.75 ? 0.96 : 0.96 * (1.0 - (age - 0.75) / 0.25);

        string text;
        double fontSize;
        int color;
        if (entry.Kind == CombatTextKind.Word)
        {
            text = entry.Word;
            fontSize = 21.0;
            color = unchecked((int)0xffffd166);
        }
        else
        {
            text = entry.Amount >= 100f ? entry.Amount.ToString("0") : entry.Amount.ToString("0.#");
            bool heal = entry.DamageType == VisualDamageTypes.Heal;
            if (heal)
            {
                text = "+" + text;
            }

            fontSize = entry.Crit ? 20.0 : 14.5;
            fontSize += Math.Min(4.0, (entry.MergeCount - 1) * 0.8);
            color = VisualDamageTypes.ColorRgba(entry.DamageType);
        }

        double x = screen.X;
        double y = frameHeight - screen.Y - rise;
        ctx.SelectFontFace("Arial", FontSlant.Normal, FontWeight.Bold);
        ctx.SetFontSize(fontSize);
        TextExtents ext = ctx.TextExtents(text);
        double tx = x - ext.Width / 2 - ext.XBearing;

        ctx.SetSourceRGBA(0, 0, 0, alpha * 0.85);
        ctx.MoveTo(tx + 1.5, y + 1.5);
        ctx.ShowText(text);

        ctx.SetSourceRGBA(
            ((color >> 16) & 0xff) / 255.0,
            ((color >> 8) & 0xff) / 255.0,
            (color & 0xff) / 255.0,
            alpha);
        ctx.MoveTo(tx, y);
        ctx.ShowText(text);
    }

    private Vec3d ResolveAnchor(CombatTextEntry entry)
    {
        Entity? target = entry.TargetEntityId != 0 ? capi.World.GetEntityById(entry.TargetEntityId) : null;
        if (target != null)
        {
            return new Vec3d(target.Pos.X, target.Pos.Y + Math.Max(1.2f, target.SelectionBox?.Y2 ?? 1.5f) + 0.35, target.Pos.Z);
        }

        return new Vec3d(entry.AnchorX, entry.AnchorY + 1.2, entry.AnchorZ);
    }

    public override void Dispose()
    {
        texture.Dispose();
        base.Dispose();
    }
}
