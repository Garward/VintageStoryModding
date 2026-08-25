using System;
using Cairo;
using VRPG.Config;
using VRPG.Network;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace VRPG.Client.UI;

public sealed class GuiElementVrpgResourceBars : GuiElement
{
    private readonly RpgHudConfig config;
    private readonly Action<int, int, bool> moved;
    private readonly Action<double, double> openLayoutMenu;
    private readonly double initialTopLeftX;
    private readonly double initialTopLeftY;
    private RpgResourcePacket snapshot = new RpgResourcePacket
    {
        Health = 100f,
        MaxHealth = 100f,
        Mana = 100f,
        MaxMana = 100f,
        HealthRegenPerSecond = 0f,
        ManaRegenPerSecond = 0f,
        Experience = 0,
        ExperienceToNextLevel = 1000,
        Level = 1,
        HudEnabled = true
    };

    private int textureId;
    private bool hasServerSnapshot;
    private bool dragging;
    private double dragMouseX;
    private double dragMouseY;
    private double dragBoundsX;
    private double dragBoundsY;

    public GuiElementVrpgResourceBars(
        ICoreClientAPI api,
        ElementBounds bounds,
        RpgHudConfig config,
        double initialTopLeftX,
        double initialTopLeftY,
        Action<int, int, bool> moved,
        Action<double, double> openLayoutMenu) : base(api, bounds)
    {
        this.config = config;
        this.initialTopLeftX = initialTopLeftX;
        this.initialTopLeftY = initialTopLeftY;
        this.moved = moved;
        this.openLayoutMenu = openLayoutMenu;
        MouseOverCursor = config.Locked ? null : "move";
    }

    public override bool Focusable => true;

    public void SetLocked(bool locked)
    {
        config.Locked = locked;
        dragging = false;
        MouseOverCursor = locked ? null : "move";
        Redraw();
    }

    public void SetSnapshot(RpgResourcePacket packet)
    {
        if (packet == null)
        {
            return;
        }

        bool changed = !hasServerSnapshot;
        if (!hasServerSnapshot)
        {
            snapshot = ClonePacket(packet);
            hasServerSnapshot = true;
            Redraw();
            return;
        }

        snapshot.Health = ReconcileValue(snapshot.Health, packet.Health, snapshot.MaxHealth, packet.MaxHealth, out bool healthChanged);
        snapshot.Mana = ReconcileValue(snapshot.Mana, packet.Mana, snapshot.MaxMana, packet.MaxMana, out bool manaChanged);
        snapshot.MagicShield = ReconcileValue(snapshot.MagicShield, packet.MagicShield, snapshot.MaxMagicShield, packet.MaxMagicShield, out bool shieldChanged);
        snapshot.Blood = ReconcileValue(snapshot.Blood, packet.Blood, snapshot.MaxBlood, packet.MaxBlood, out bool bloodChanged);
        changed |= healthChanged || manaChanged || shieldChanged || bloodChanged;

        changed |= ApplyServerMetadata(packet);

        if (changed)
        {
            Redraw();
        }
    }

    public override void ComposeElements(Context ctxStatic, ImageSurface surfaceStatic)
    {
        Bounds.CalcWorldBounds();
        Redraw();
    }

    public override void RenderInteractiveElements(float deltaTime)
    {
        if (!snapshot.HudEnabled || textureId <= 0)
        {
            return;
        }

        if (PredictResources(deltaTime))
        {
            Redraw();
        }

        api.Render.Render2DTexturePremultipliedAlpha(textureId, Bounds);
    }

    public override void OnMouseDownOnElement(ICoreClientAPI api, MouseEvent args)
    {
        if (args.Button == EnumMouseButton.Right)
        {
            openLayoutMenu(args.X, args.Y);
            args.Handled = true;
            return;
        }

        if (config.Locked || args.Button != EnumMouseButton.Left)
        {
            return;
        }

        dragging = true;
        dragMouseX = args.X;
        dragMouseY = args.Y;
        bool absolutePosition = string.Equals(config.Anchor, "left-top", StringComparison.OrdinalIgnoreCase)
            || string.Equals(config.Anchor, "top-left", StringComparison.OrdinalIgnoreCase);
        dragBoundsX = absolutePosition ? config.X : initialTopLeftX;
        dragBoundsY = absolutePosition ? config.Y : initialTopLeftY;
        args.Handled = true;
    }

    public override void OnMouseMove(ICoreClientAPI api, MouseEvent args)
    {
        if (!dragging)
        {
            return;
        }

        ResourceHudPosition position = ResourceHudLayout.ClampTopLeft(
            dragBoundsX + (args.X - dragMouseX) / RuntimeEnv.GUIScale,
            dragBoundsY + (args.Y - dragMouseY) / RuntimeEnv.GUIScale,
            api.Render.FrameWidth / RuntimeEnv.GUIScale,
            api.Render.FrameHeight / RuntimeEnv.GUIScale,
            Bounds.fixedWidth,
            Bounds.fixedHeight);
        moved((int)Math.Round(position.X), (int)Math.Round(position.Y), false);
        args.Handled = true;
    }

    public override void OnMouseUpOnElement(ICoreClientAPI api, MouseEvent args)
    {
        if (!dragging)
        {
            return;
        }

        dragging = false;
        moved(config.X, config.Y, true);
        args.Handled = true;
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
        ctx.Operator = Operator.Clear;
        ctx.Paint();
        ctx.Operator = Operator.Over;

        double barHeight = Math.Max(14, config.BarHeight);
        double gap = Math.Max(2, config.Gap);
        double y = 0;

        DrawBar(ctx, 0, y, width, barHeight, snapshot.Health, snapshot.MaxHealth, "HP", "#e60035", "#7a0019");
        y += barHeight + gap;

        DrawBar(ctx, 0, y, width, barHeight, snapshot.Mana, snapshot.MaxMana, "MP", "#2d86ff", "#10366f");
        y += barHeight + gap;

        if (snapshot.MaxMagicShield > 0f || config.ShowMagicShieldWhenEmpty)
        {
            DrawBar(ctx, 0, y, width, barHeight, snapshot.MagicShield, Math.Max(1f, snapshot.MaxMagicShield), "Shield", "#78f0ff", "#1a5360");
            y += barHeight + gap;
        }

        if (snapshot.BloodUnlocked || config.ShowBloodWhenUnavailable)
        {
            DrawBar(ctx, 0, y, width, barHeight, snapshot.Blood, snapshot.MaxBlood, "Blood", "#930018", "#3a000b");
            y += barHeight + gap;
        }

        if (config.ShowExperience)
        {
            DrawExperienceBar(ctx, 0, y, width, barHeight);
        }

        if (!config.Locked)
        {
            SetColor(ctx, 1.0, 0.62, 0.06, 0.92);
            ctx.LineWidth = 2;
            ctx.Rectangle(1, 1, width - 2, height - 2);
            ctx.Stroke();
            DrawCenteredText(ctx, "DRAG · RIGHT-CLICK OPTIONS", 0, Math.Max(0, height - 13), width, 12);
        }
    }

    private bool PredictResources(float deltaTime)
    {
        double seconds = GameMath.Clamp(deltaTime, 0f, 0.25f);
        if (seconds <= 0)
        {
            return false;
        }

        bool changed = false;
        snapshot.Health = PredictValue(snapshot.Health, snapshot.MaxHealth, snapshot.HealthRegenPerSecond, seconds, out bool healthChanged);
        snapshot.Mana = PredictValue(snapshot.Mana, snapshot.MaxMana, snapshot.ManaRegenPerSecond, seconds, out bool manaChanged);
        snapshot.MagicShield = PredictValue(snapshot.MagicShield, snapshot.MaxMagicShield, snapshot.MagicShieldRegenPerSecond, seconds, out bool shieldChanged);
        changed |= healthChanged || manaChanged || shieldChanged;

        if (snapshot.BloodUnlocked)
        {
            snapshot.Blood = PredictValue(snapshot.Blood, snapshot.MaxBlood, snapshot.BloodRegenPerSecond, seconds, out bool bloodChanged);
            changed |= bloodChanged;
        }

        return changed;
    }

    private static float PredictValue(float value, float max, float perSecond, double seconds, out bool changed)
    {
        if (max <= 0f || perSecond <= 0f || value >= max)
        {
            changed = false;
            return value;
        }

        float next = (float)Math.Min(max, value + perSecond * seconds);
        if (Math.Abs(next - value) <= 0.001f)
        {
            changed = false;
            return value;
        }

        changed = true;
        return next;
    }

    private static float ReconcileValue(float currentDisplay, float serverValue, float oldMax, float newMax, out bool changed)
    {
        float tolerance = Math.Max(0.05f, Math.Max(1f, newMax) * 0.0025f);
        float nextDisplay = Math.Abs(currentDisplay - serverValue) > tolerance ? serverValue : currentDisplay;
        nextDisplay = Clamp(nextDisplay, 0f, newMax);

        changed = Math.Abs(nextDisplay - currentDisplay) > 0.001f || Math.Abs(oldMax - newMax) > 0.001f;
        return nextDisplay;
    }

    private bool ApplyServerMetadata(RpgResourcePacket packet)
    {
        bool changed = false;

        if (IsDifferent(snapshot.MaxHealth, packet.MaxHealth)) { snapshot.MaxHealth = packet.MaxHealth; changed = true; }
        if (IsDifferent(snapshot.MaxMana, packet.MaxMana)) { snapshot.MaxMana = packet.MaxMana; changed = true; }
        if (IsDifferent(snapshot.MaxMagicShield, packet.MaxMagicShield)) { snapshot.MaxMagicShield = packet.MaxMagicShield; changed = true; }
        if (IsDifferent(snapshot.MaxBlood, packet.MaxBlood)) { snapshot.MaxBlood = packet.MaxBlood; changed = true; }

        if (IsDifferent(snapshot.HealthRegenPerSecond, packet.HealthRegenPerSecond)) { snapshot.HealthRegenPerSecond = packet.HealthRegenPerSecond; changed = true; }
        if (IsDifferent(snapshot.ManaRegenPerSecond, packet.ManaRegenPerSecond)) { snapshot.ManaRegenPerSecond = packet.ManaRegenPerSecond; changed = true; }
        if (IsDifferent(snapshot.MagicShieldRegenPerSecond, packet.MagicShieldRegenPerSecond)) { snapshot.MagicShieldRegenPerSecond = packet.MagicShieldRegenPerSecond; changed = true; }
        if (IsDifferent(snapshot.BloodRegenPerSecond, packet.BloodRegenPerSecond)) { snapshot.BloodRegenPerSecond = packet.BloodRegenPerSecond; changed = true; }

        if (snapshot.Experience != packet.Experience) { snapshot.Experience = packet.Experience; changed = true; }
        if (snapshot.ExperienceToNextLevel != packet.ExperienceToNextLevel) { snapshot.ExperienceToNextLevel = packet.ExperienceToNextLevel; changed = true; }
        if (snapshot.Level != packet.Level) { snapshot.Level = packet.Level; changed = true; }
        if (snapshot.HudEnabled != packet.HudEnabled) { snapshot.HudEnabled = packet.HudEnabled; changed = true; }
        if (snapshot.HideVanillaStatbar != packet.HideVanillaStatbar) { snapshot.HideVanillaStatbar = packet.HideVanillaStatbar; changed = true; }
        if (snapshot.BloodUnlocked != packet.BloodUnlocked) { snapshot.BloodUnlocked = packet.BloodUnlocked; changed = true; }

        return changed;
    }

    private static bool IsDifferent(float current, float value)
    {
        return Math.Abs(current - value) > 0.001f;
    }

    private void DrawBar(Context ctx, double x, double y, double width, double height, double value, double max, string label, string fillHex, string backHex, string? displayText = null)
    {
        double fill = max <= 0 ? 0 : GameMath.Clamp(value / max, 0, 1);
        double border = 3;

        SetColor(ctx, 0, 0, 0, 0.68);
        ctx.Rectangle(x, y, width, height);
        ctx.Fill();

        SetColor(ctx, 0.93, 0.68, 0.28, 0.92);
        ctx.Rectangle(x, y, width, height);
        ctx.LineWidth = 2;
        ctx.Stroke();

        SetHex(ctx, backHex, 0.95);
        ctx.Rectangle(x + border, y + border, width - border * 2, height - border * 2);
        ctx.Fill();

        SetHex(ctx, fillHex, 0.98);
        ctx.Rectangle(x + border, y + border, (width - border * 2) * fill, height - border * 2);
        ctx.Fill();

        SetColor(ctx, 1, 1, 1, 0.12);
        ctx.Rectangle(x + border, y + border, (width - border * 2) * fill, Math.Max(1, (height - border * 2) * 0.34));
        ctx.Fill();

        string text = displayText ?? label + "  " + Format(value) + "/" + Format(max);
        DrawCenteredText(ctx, text, x, y, width, height);
    }

    private void DrawExperienceBar(Context ctx, double x, double y, double width, double height)
    {
        double max = Math.Max(1, snapshot.ExperienceToNextLevel);
        double percent = GameMath.Clamp(snapshot.Experience / max, 0, 1);
        string text = "XP Lv. " + snapshot.Level + "  " + Format(snapshot.Experience) + "/" + Format(max) + "  " + Math.Round(percent * 100) + "%";
        DrawBar(ctx, x, y, width, height, snapshot.Experience, max, "XP", "#e6d94c", "#5a5017", text);
    }

    private static void DrawCenteredText(Context ctx, string text, double x, double y, double width, double height)
    {
        ctx.SelectFontFace("Arial", FontSlant.Normal, FontWeight.Bold);
        ctx.SetFontSize(Math.Max(10, height * 0.62));
        TextExtents ext = ctx.TextExtents(text);
        double textX = x + (width - ext.Width) / 2 - ext.XBearing;
        double textY = y + (height - ext.Height) / 2 - ext.YBearing;

        SetColor(ctx, 0, 0, 0, 0.78);
        ctx.MoveTo(textX + 1, textY + 1);
        ctx.ShowText(text);

        SetColor(ctx, 1, 1, 1, 0.95);
        ctx.MoveTo(textX, textY);
        ctx.ShowText(text);
    }

    private static string Format(double value)
    {
        return value.ToString(value >= 100 ? "0" : "0.#");
    }

    private static RpgResourcePacket ClonePacket(RpgResourcePacket source)
    {
        return new RpgResourcePacket
        {
            Health = source.Health,
            MaxHealth = source.MaxHealth,
            Mana = source.Mana,
            MaxMana = source.MaxMana,
            MagicShield = source.MagicShield,
            MaxMagicShield = source.MaxMagicShield,
            Blood = source.Blood,
            MaxBlood = source.MaxBlood,
            BloodUnlocked = source.BloodUnlocked,
            Experience = source.Experience,
            ExperienceToNextLevel = source.ExperienceToNextLevel,
            Level = source.Level,
            HudEnabled = source.HudEnabled,
            HideVanillaStatbar = source.HideVanillaStatbar,
            HealthRegenPerSecond = source.HealthRegenPerSecond,
            ManaRegenPerSecond = source.ManaRegenPerSecond,
            MagicShieldRegenPerSecond = source.MagicShieldRegenPerSecond,
            BloodRegenPerSecond = source.BloodRegenPerSecond
        };
    }

    private static float Clamp(float value, float min, float max)
    {
        if (max < min)
        {
            max = min;
        }

        return Math.Max(min, Math.Min(max, value));
    }

    private static void SetHex(Context ctx, string hex, double alpha)
    {
        string value = (hex ?? "").TrimStart('#');
        if (value.Length != 6)
        {
            SetColor(ctx, 1, 1, 1, alpha);
            return;
        }

        int r = Convert.ToInt32(value.Substring(0, 2), 16);
        int g = Convert.ToInt32(value.Substring(2, 2), 16);
        int b = Convert.ToInt32(value.Substring(4, 2), 16);
        SetColor(ctx, r / 255.0, g / 255.0, b / 255.0, alpha);
    }

    private static void SetColor(Context ctx, double r, double g, double b, double a)
    {
        ctx.SetSourceRGBA(r, g, b, a);
    }
}
