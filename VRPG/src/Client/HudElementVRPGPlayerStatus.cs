using System;
using System.Collections.Generic;
using Cairo;
using VRPG.Client.UI;
using VRPG.Client.Visuals;
using VRPG.Data;
using VRPG.Data.Definitions;
using Vintagestory.API.Client;

namespace VRPG.Client;

public sealed class HudElementVRPGPlayerStatus : HudElement
{
    private readonly VRPGDataRegistry data;
    private readonly EntityStatusCache cache = new EntityStatusCache();
    private LoadedTexture texture;

    public override string? ToggleKeyCombinationCode => null;
    public override bool Focusable => false;

    public HudElementVRPGPlayerStatus(ICoreClientAPI capi, VRPGDataRegistry data) : base(capi)
    {
        this.data = data;
        texture = new LoadedTexture(capi);
        TryOpen();
    }

    public override bool ShouldReceiveKeyboardEvents() => false;
    public override bool ShouldReceiveRenderEvents() => true;
    public override bool TryClose() => false;

    public override void OnRenderGUI(float deltaTime)
    {
        if (capi.World?.Player?.Entity == null)
        {
            return;
        }

        long nowMs = capi.ElapsedMilliseconds;
        IReadOnlyList<ActiveStatus> statuses = cache.Update(
            capi.World.Player.Entity.EntityId, capi.World.Player.Entity.WatchedAttributes, nowMs);
        if (statuses.Count == 0)
        {
            return;
        }

        const double iconSize = 26.0;
        const double gap = 6.0;
        int frameWidth = Math.Max(1, capi.Render.FrameWidth);
        int frameHeight = Math.Max(1, capi.Render.FrameHeight);
        double totalWidth = statuses.Count * (iconSize + gap) - gap;
        double x = (frameWidth - totalWidth) / 2.0;
        double y = frameHeight - 168.0;

        using var surface = new ImageSurface((Format)0, frameWidth, frameHeight);
        using var ctx = new Context(surface);
        ctx.Operator = Operator.Clear;
        ctx.Paint();
        ctx.Operator = Operator.Over;

        foreach (ActiveStatus status in statuses)
        {
            StatusEffectDefinition? definition = data.StatusEffects.Get(status.Code);
            if (definition == null)
            {
                continue;
            }

            if (!SkillDefinitionValidator.TryParseColor(definition.Visual.Color, out int color))
            {
                color = unchecked((int)0xfff2ede4);
            }

            double r = ((color >> 16) & 0xff) / 255.0;
            double g = ((color >> 8) & 0xff) / 255.0;
            double b = (color & 0xff) / 255.0;

            ctx.SetSourceRGBA(0, 0, 0, 0.62);
            ctx.Rectangle(x - 2, y - 2, iconSize + 4, iconSize + 4);
            ctx.Fill();
            VrpgIconPainter.Draw(ctx, string.IsNullOrWhiteSpace(definition.Visual.Icon) ? definition.Code : definition.Visual.Icon, x, y, iconSize, r, g, b);

            ctx.SelectFontFace("Arial", FontSlant.Normal, FontWeight.Bold);
            ctx.SetFontSize(10.5);
            string seconds = Math.Ceiling(status.RemainingSeconds(nowMs)).ToString("0");
            TextExtents ext = ctx.TextExtents(seconds);
            ctx.SetSourceRGBA(0, 0, 0, 0.9);
            ctx.MoveTo(x + (iconSize - ext.Width) / 2 + 1, y + iconSize + 12);
            ctx.ShowText(seconds);
            ctx.SetSourceRGBA(0.95, 0.93, 0.88, 0.96);
            ctx.MoveTo(x + (iconSize - ext.Width) / 2, y + iconSize + 11);
            ctx.ShowText(seconds);

            if (definition.Visual.ShowStacks && status.Stacks > 1)
            {
                string stacks = status.Stacks.ToString();
                TextExtents stackExt = ctx.TextExtents(stacks);
                ctx.SetSourceRGBA(0, 0, 0, 0.9);
                ctx.MoveTo(x + iconSize - stackExt.Width + 1, y + 11);
                ctx.ShowText(stacks);
                ctx.SetSourceRGBA(1, 1, 1, 0.98);
                ctx.MoveTo(x + iconSize - stackExt.Width, y + 10);
                ctx.ShowText(stacks);
            }

            x += iconSize + gap;
        }

        capi.Gui.LoadOrUpdateCairoTexture(surface, false, ref texture);
        if (texture.TextureId > 0)
        {
            capi.Render.Render2DLoadedTexture(texture, 0, 0, 2250f);
        }
    }

    public override void Dispose()
    {
        texture.Dispose();
        base.Dispose();
    }
}
