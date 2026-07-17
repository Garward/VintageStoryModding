using System;
using System.Collections.Generic;
using Cairo;
using VRPG.Client.UI;
using VRPG.Client.Visuals;
using VRPG.Config;
using VRPG.Data;
using VRPG.Data.Definitions;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace VRPG.Client;

public sealed class HudElementVRPGEntityHealthBars : HudElement
{
    private const string HealthTreeName = "health";
    private readonly RpgEntityHudConfig config;
    private readonly VRPGDataRegistry data;
    private readonly EntityStatusCache statusCache = new EntityStatusCache();
    private LoadedTexture texture;
    private readonly List<EntityHudEntry> entries = new List<EntityHudEntry>();
    private long lastRefreshMs;
    private int lastFrameWidth;
    private int lastFrameHeight;

    public override string? ToggleKeyCombinationCode => null;
    public override bool Focusable => false;

    public HudElementVRPGEntityHealthBars(ICoreClientAPI capi, RpgEntityHudConfig config, VRPGDataRegistry data) : base(capi)
    {
        this.config = config;
        this.data = data;
        texture = new LoadedTexture(capi);
        TryOpen();
    }

    public override bool ShouldReceiveKeyboardEvents()
    {
        return false;
    }

    public override bool ShouldReceiveRenderEvents()
    {
        return true;
    }

    public override bool TryClose()
    {
        return false;
    }

    public override void OnRenderGUI(float deltaTime)
    {
        if (!config.Enabled || IsOffMode() || capi.World?.Player?.Entity == null)
        {
            return;
        }

        long now = capi.ElapsedMilliseconds;
        int refreshMs = Math.Max(16, config.RefreshIntervalMs);
        bool resized = lastFrameWidth != capi.Render.FrameWidth || lastFrameHeight != capi.Render.FrameHeight;
        if (texture.TextureId == 0 || resized || now - lastRefreshMs >= refreshMs)
        {
            CollectEntries();
            lastRefreshMs = now;
        }

        Redraw();

        if (texture.TextureId > 0)
        {
            capi.Render.Render2DLoadedTexture(texture, 0, 0, 2200f);
        }
    }

    public override void Dispose()
    {
        texture.Dispose();
        base.Dispose();
    }

    private void Redraw()
    {
        lastFrameWidth = Math.Max(1, capi.Render.FrameWidth);
        lastFrameHeight = Math.Max(1, capi.Render.FrameHeight);

        using ImageSurface surface = new ImageSurface((Format)0, lastFrameWidth, lastFrameHeight);
        using Context ctx = new Context(surface);
        ctx.Operator = Operator.Clear;
        ctx.Paint();
        ctx.Operator = Operator.Over;

        for (int i = 0; i < entries.Count; i++)
        {
            if (TryReadHealth(entries[i].Entity, out float health, out float maxHealth))
            {
                DrawEntry(ctx, entries[i], health, maxHealth);
            }
        }

        capi.Gui.LoadOrUpdateCairoTexture(surface, false, ref texture);
    }

    private void CollectEntries()
    {
        entries.Clear();

        EntityPlayer playerEntity = capi.World.Player.Entity;
        Vec3d playerPos = playerEntity.Pos.XYZ;
        float radius = Math.Max(1f, config.Radius);
        Entity[] nearby = capi.World.GetEntitiesAround(playerPos, radius, radius, IsCandidate);

        for (int i = 0; i < nearby.Length; i++)
        {
            Entity entity = nearby[i];
            if (!TryReadHealth(entity, out float health, out float maxHealth))
            {
                continue;
            }

            if (config.ShowOnlyDamaged && health >= maxHealth)
            {
                continue;
            }

            double dist = entity.Pos.SquareDistanceTo(playerPos);
            entries.Add(new EntityHudEntry(entity, dist, entity == capi.World.Player.CurrentEntitySelection?.Entity));
        }

        entries.Sort(CompareEntries);
        int maxShown = Math.Max(1, config.MaxShown);
        if (entries.Count > maxShown)
        {
            entries.RemoveRange(maxShown, entries.Count - maxShown);
        }

        statusCache.Prune(capi.ElapsedMilliseconds);
    }

    private bool IsCandidate(Entity entity)
    {
        if (entity == null || entity == capi.World.Player.Entity)
        {
            return false;
        }

        if (!config.ShowPlayers && entity is EntityPlayer)
        {
            return false;
        }

        if (!config.ShowDead && !entity.Alive)
        {
            return false;
        }

        return entity.IsInteractable && entity is EntityAgent && entity.WatchedAttributes.GetTreeAttribute(HealthTreeName) != null;
    }

    private Vec3d Project(Entity entity)
    {
        double y = entity.Pos.Y + Math.Max(entity.SelectionBox?.Y2 ?? 1.5f, entity.LocalEyePos.Y) + config.VerticalOffset;
        Vec3d aboveHead = new Vec3d(entity.Pos.X, y, entity.Pos.Z);
        return MatrixToolsd.Project(aboveHead, capi.Render.PerspectiveProjectionMat, capi.Render.PerspectiveViewMat, capi.Render.FrameWidth, capi.Render.FrameHeight);
    }

    private void DrawEntry(Context ctx, EntityHudEntry entry, float health, float maxHealth)
    {
        Entity entity = entry.Entity;
        Vec3d screen = Project(entity);
        if (screen.Z < 0 || screen.X < -200 || screen.X > capi.Render.FrameWidth + 200 || screen.Y < -200 || screen.Y > capi.Render.FrameHeight + 200)
        {
            return;
        }

        string mode = (config.Mode ?? "compact").Trim().ToLowerInvariant();
        bool readable = mode == "readable" || mode == "detailed";
        bool detailed = mode == "detailed";
        double scale = GameMath.Clamp(config.Scale, 0.55f, 2f);
        double width = (readable ? 210 : 150) * scale;
        double barHeight = (readable ? 14 : 9) * scale;
        double labelHeight = (config.ShowNames || config.ShowLevels ? (readable ? 20 : 15) * scale : 0);
        double x = screen.X - width / 2;
        double y = capi.Render.FrameHeight - screen.Y - labelHeight - barHeight - 4 * scale;

        if (entry.Targeted)
        {
            width *= 1.08;
            x = screen.X - width / 2;
        }

        if (config.ShowNames || config.ShowLevels)
        {
            DrawLabel(ctx, entity, entry, x, y, width, labelHeight, readable || detailed);
            y += labelHeight;
        }

        DrawHealthBar(ctx, x, y, width, barHeight, health, maxHealth, readable || detailed || entry.Targeted);

        DrawStatusOverlay(ctx, entity, x, y + barHeight + 3 * scale, width, scale);
    }

    private void DrawStatusOverlay(Context ctx, Entity entity, double x, double y, double width, double scale)
    {
        IReadOnlyList<Visuals.ActiveStatus> statuses = statusCache.Update(entity.EntityId, entity.WatchedAttributes, capi.ElapsedMilliseconds);
        DrawShieldOverlay(ctx, entity, x, y - 3 * scale, width, scale);

        double iconSize = 15 * scale;
        double iconX = x;
        foreach (Visuals.ActiveStatus status in statuses)
        {
            StatusEffectDefinition? definition = data.StatusEffects.Get(status.Code);
            if (definition == null)
            {
                continue;
            }

            if (definition.Visual.Buildup?.ShowBar == true)
            {
                DrawBuildupBar(ctx, definition, status, x, y, width, scale);
                y += 7 * scale;
                continue;
            }

            if (iconX + iconSize > x + width)
            {
                continue;
            }

            DrawStatusIcon(ctx, definition, status, iconX, y, iconSize, scale);
            iconX += iconSize + 3 * scale;
        }
    }

    private void DrawStatusIcon(Context ctx, StatusEffectDefinition definition, Visuals.ActiveStatus status, double x, double y, double size, double scale)
    {
        if (!Data.SkillDefinitionValidator.TryParseColor(definition.Visual.Color, out int color))
        {
            color = unchecked((int)0xfff2ede4);
        }

        double r = ((color >> 16) & 0xff) / 255.0;
        double g = ((color >> 8) & 0xff) / 255.0;
        double b = (color & 0xff) / 255.0;

        // Duration wipe: darken the spent fraction of a backing plate.
        double remaining = status.DurationMs <= 0 ? 1.0 : Math.Clamp(status.RemainingSeconds(capi.ElapsedMilliseconds) * 1000.0 / status.DurationMs, 0, 1);
        SetColor(ctx, 0, 0, 0, 0.6);
        ctx.Rectangle(x, y, size, size);
        ctx.Fill();
        SetColor(ctx, r * 0.35, g * 0.35, b * 0.35, 0.85);
        ctx.Rectangle(x, y + size * (1 - remaining), size, size * remaining);
        ctx.Fill();

        VrpgIconPainter.Draw(ctx, string.IsNullOrWhiteSpace(definition.Visual.Icon) ? definition.Code : definition.Visual.Icon, x, y, size, r, g, b);

        if (definition.Visual.ShowStacks && status.Stacks > 1)
        {
            ctx.SelectFontFace("Arial", FontSlant.Normal, FontWeight.Bold);
            ctx.SetFontSize(Math.Max(8.0, size * 0.52));
            string stacks = status.Stacks.ToString();
            TextExtents ext = ctx.TextExtents(stacks);
            SetColor(ctx, 0, 0, 0, 0.9);
            ctx.MoveTo(x + size - ext.Width + 0.5, y + size - 0.5);
            ctx.ShowText(stacks);
            SetColor(ctx, 1, 1, 1, 0.98);
            ctx.MoveTo(x + size - ext.Width - 0.5, y + size - 1.5);
            ctx.ShowText(stacks);
        }
    }

    private void DrawBuildupBar(Context ctx, StatusEffectDefinition definition, Visuals.ActiveStatus status, double x, double y, double width, double scale)
    {
        StatusBuildupVisualDefinition buildup = definition.Visual.Buildup!;
        double barHeight = 5 * scale;
        double fill = Math.Clamp(status.Magnitude / Math.Max(1f, buildup.Threshold), 0, 1);
        bool atThreshold = fill >= 1.0;

        if (!Data.SkillDefinitionValidator.TryParseColor(definition.Visual.Color, out int color))
        {
            color = unchecked((int)0xffffd166);
        }

        SetColor(ctx, 0, 0, 0, 0.72);
        ctx.Rectangle(x - 1, y - 1, width + 2, barHeight + 2);
        ctx.Fill();
        SetColor(ctx, 0.15, 0.12, 0.08, 0.9);
        ctx.Rectangle(x, y, width, barHeight);
        ctx.Fill();

        double flash = atThreshold && buildup.FlashAtThreshold
            ? 0.65 + 0.35 * Math.Sin(capi.ElapsedMilliseconds / 80.0)
            : 0.95;
        SetColor(ctx, ((color >> 16) & 0xff) / 255.0 * flash, ((color >> 8) & 0xff) / 255.0 * flash, (color & 0xff) / 255.0 * flash, 0.95);
        ctx.Rectangle(x, y, width * fill, barHeight);
        ctx.Fill();

        // Threshold notch.
        SetColor(ctx, 1, 1, 1, 0.85);
        ctx.Rectangle(x + width - 1.5, y - 1, 1.5, barHeight + 2);
        ctx.Fill();
    }

    private void DrawShieldOverlay(Context ctx, Entity entity, double barX, double barBottomY, double width, double scale)
    {
        float shield = entity.WatchedAttributes.GetFloat("vrpgShieldCurrent");
        float shieldMax = entity.WatchedAttributes.GetFloat("vrpgShieldMax");
        if (shield <= 0f || shieldMax <= 0f || !TryReadHealth(entity, out _, out float maxHealth) || maxHealth <= 0f)
        {
            return;
        }

        double fraction = Math.Min(1.0, shield / maxHealth);
        double overlayHeight = 3 * scale;
        SetColor(ctx, 0.47, 0.94, 1.0, 0.9);
        ctx.Rectangle(barX, barBottomY - overlayHeight, width * fraction, overlayHeight);
        ctx.Fill();
    }

    private void DrawLabel(Context ctx, Entity entity, EntityHudEntry entry, double x, double y, double width, double height, bool readable)
    {
        string text = "";
        if (config.ShowLevels)
        {
            int level = Math.Max(1, entity.WatchedAttributes.GetInt("vrpgLevel", 1));
            string rarity = entity.WatchedAttributes.GetString("vrpgRarityName", "");
            if (!string.IsNullOrWhiteSpace(rarity) && !string.Equals(rarity, "Ordinary", StringComparison.OrdinalIgnoreCase))
            {
                text += rarity + " ";
            }

            text += "Lv. " + level + " ";
        }

        if (config.ShowNames)
        {
            text += entity.GetName();
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        double fontSize = readable ? 13.5 * config.Scale : 11.5 * config.Scale;
        ctx.SelectFontFace("Arial", FontSlant.Normal, FontWeight.Bold);
        ctx.SetFontSize(fontSize);
        TextExtents ext = ctx.TextExtents(text);
        double tx = x + (width - ext.Width) / 2 - ext.XBearing;
        double ty = y + Math.Max(height - 2 * config.Scale, ext.Height + 1 * config.Scale);

        SetColor(ctx, 0, 0, 0, 0.74);
        ctx.MoveTo(tx + 1, ty + 1);
        ctx.ShowText(text);

        SetColor(ctx, entry.Targeted ? 1 : 0.92, entry.Targeted ? 0.78 : 0.9, entry.Targeted ? 0.34 : 0.82, 0.98);
        ctx.MoveTo(tx, ty);
        ctx.ShowText(text);
    }

    private void DrawHealthBar(Context ctx, double x, double y, double width, double height, float health, float maxHealth, bool readable)
    {
        double fill = maxHealth <= 0 ? 0 : GameMath.Clamp(health / maxHealth, 0, 1);
        double border = Math.Max(1, Math.Round(config.Scale));

        SetColor(ctx, 0, 0, 0, 0.72);
        ctx.Rectangle(x - border, y - border, width + border * 2, height + border * 2);
        ctx.Fill();

        SetColor(ctx, 0.17, 0.13, 0.1, 0.92);
        ctx.Rectangle(x, y, width, height);
        ctx.Fill();

        SetColor(ctx, 0.33, 0.74, 0.06, 0.94);
        ctx.Rectangle(x, y, width * fill, height);
        ctx.Fill();

        SetColor(ctx, 0.86, 0.6, 0.22, 0.78);
        ctx.Rectangle(x - border, y - border, width + border * 2, height + border * 2);
        ctx.LineWidth = border;
        ctx.Stroke();

        if (!readable && !config.ShowPercent && !config.ShowNumbers)
        {
            return;
        }

        string text = BuildHealthText(health, maxHealth);
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        ctx.SelectFontFace("Arial", FontSlant.Normal, FontWeight.Bold);
        ctx.SetFontSize(Math.Max(9, height + 3 * config.Scale));
        TextExtents ext = ctx.TextExtents(text);
        double tx = x + (width - ext.Width) / 2 - ext.XBearing;
        double ty = y + height / 2 - ext.Height / 2 - ext.YBearing;

        SetColor(ctx, 0, 0, 0, 0.82);
        ctx.MoveTo(tx + 1, ty + 1);
        ctx.ShowText(text);

        SetColor(ctx, 1, 1, 1, 0.96);
        ctx.MoveTo(tx, ty);
        ctx.ShowText(text);
    }

    private string BuildHealthText(float health, float maxHealth)
    {
        if (config.ShowNumbers)
        {
            return Format(health) + "/" + Format(maxHealth);
        }

        if (config.ShowPercent)
        {
            return Math.Round(maxHealth <= 0 ? 0 : health / maxHealth * 100f) + "%";
        }

        return "";
    }

    private bool TryReadHealth(Entity entity, out float health, out float maxHealth)
    {
        health = 0;
        maxHealth = 0;
        ITreeAttribute? healthTree = entity.WatchedAttributes.GetTreeAttribute(HealthTreeName);
        if (healthTree == null)
        {
            return false;
        }

        float? current = healthTree.TryGetFloat("currenthealth");
        float? max = healthTree.TryGetFloat("maxhealth");
        if (!current.HasValue || !max.HasValue || max.Value <= 0)
        {
            return false;
        }

        health = current.Value;
        maxHealth = max.Value;
        return true;
    }

    private bool IsOffMode()
    {
        return string.Equals(config.Mode, "off", StringComparison.OrdinalIgnoreCase);
    }

    private static int CompareEntries(EntityHudEntry left, EntityHudEntry right)
    {
        if (left.Targeted != right.Targeted)
        {
            return left.Targeted ? -1 : 1;
        }

        return left.DistanceSquared.CompareTo(right.DistanceSquared);
    }

    private static string Format(float value)
    {
        return value.ToString(value >= 100 ? "0" : "0.#");
    }

    private static void SetColor(Context ctx, double r, double g, double b, double a)
    {
        ctx.SetSourceRGBA(r, g, b, a);
    }

    private sealed class EntityHudEntry
    {
        public EntityHudEntry(Entity entity, double distanceSquared, bool targeted)
        {
            Entity = entity;
            DistanceSquared = distanceSquared;
            Targeted = targeted;
        }

        public Entity Entity { get; }
        public double DistanceSquared { get; }
        public bool Targeted { get; }
    }
}
