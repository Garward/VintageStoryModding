using System;
using Cairo;
using VRPG.Config;
using VRPG.Network;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace VRPG.Client.UI;

public sealed class GuiElementVrpgSkillHotbar : GuiElement
{
    private readonly RpgSkillHotbarConfig config;
    private readonly Action<int, int, bool> moved;
    private SkillLoadoutPacket snapshot = new SkillLoadoutPacket();
    private RpgResourcePacket resources = new RpgResourcePacket();
    private long snapshotAtMilliseconds;
    private long nextCooldownRedraw;
    private long nextBindingCheck;
    private string bindingSignature = "";
    private int textureId;
    private bool dragging;
    private double dragMouseX;
    private double dragMouseY;
    private double dragBoundsX;
    private double dragBoundsY;

    public GuiElementVrpgSkillHotbar(ICoreClientAPI api, ElementBounds bounds, RpgSkillHotbarConfig config, Action<int, int, bool> moved)
        : base(api, bounds)
    {
        this.config = config;
        this.moved = moved;
        MouseOverCursor = config.Locked ? null : "move";
    }

    public override bool Focusable => !config.Locked;

    public void SetSnapshot(SkillLoadoutPacket packet)
    {
        snapshot = packet ?? new SkillLoadoutPacket();
        snapshotAtMilliseconds = api.World.ElapsedMilliseconds;
        Redraw();
    }

    public void SetResources(RpgResourcePacket packet)
    {
        resources = packet ?? new RpgResourcePacket();
        Redraw();
    }

    public void SetLocked(bool locked)
    {
        config.Locked = locked;
        dragging = false;
        MouseOverCursor = locked ? null : "move";
        Redraw();
    }

    public override void ComposeElements(Context ctxStatic, ImageSurface surfaceStatic)
    {
        Bounds.CalcWorldBounds();
        Redraw();
    }

    public override void RenderInteractiveElements(float deltaTime)
    {
        if (!config.Enabled || textureId <= 0)
        {
            return;
        }

        long now = api.World.ElapsedMilliseconds;
        if (now >= nextBindingCheck)
        {
            nextBindingCheck = now + 500;
            string currentBindings = CurrentBindingSignature();
            if (!string.Equals(currentBindings, bindingSignature, StringComparison.Ordinal))
            {
                bindingSignature = currentBindings;
                Redraw();
            }
        }
        if (HasActiveCooldown(now) && now >= nextCooldownRedraw)
        {
            nextCooldownRedraw = now + 50;
            Redraw();
        }

        api.Render.Render2DTexturePremultipliedAlpha(textureId, Bounds);
    }

    public override void OnMouseDownOnElement(ICoreClientAPI api, MouseEvent args)
    {
        if (config.Locked || args.Button != EnumMouseButton.Left)
        {
            return;
        }

        dragging = true;
        dragMouseX = args.X;
        dragMouseY = args.Y;
        dragBoundsX = config.X;
        dragBoundsY = config.Y;
        args.Handled = true;
    }

    public override void OnMouseMove(ICoreClientAPI api, MouseEvent args)
    {
        if (!dragging)
        {
            return;
        }

        double maxX = Math.Max(0, api.Render.FrameWidth / RuntimeEnv.GUIScale - Bounds.fixedWidth);
        double maxY = Math.Max(0, api.Render.FrameHeight / RuntimeEnv.GUIScale - Bounds.fixedHeight);
        int nextX = (int)Math.Round(Math.Clamp(dragBoundsX + (args.X - dragMouseX) / RuntimeEnv.GUIScale, 0, maxX));
        int nextY = (int)Math.Round(Math.Clamp(dragBoundsY + (args.Y - dragMouseY) / RuntimeEnv.GUIScale, 0, maxY));
        moved(nextX, nextY, false);
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
        Draw(ctx);
        generateTexture(surface, ref textureId);
    }

    private void Draw(Context ctx)
    {
        ctx.Operator = Operator.Clear;
        ctx.Paint();
        ctx.Operator = Operator.Over;

        double slot = Math.Max(42, config.SlotSize);
        double gap = Math.Max(3, config.Gap);
        int slotCount = Math.Clamp(config.SlotCount, 4, 8);
        for (int i = 0; i < slotCount; i++)
        {
            double x = i * (slot + gap);
            SkillLoadoutSlotPacket? entry = FindSlot(i + 1);
            DrawSlot(ctx, x, 0, slot, entry, i);
        }

        if (!config.Locked)
        {
            ctx.SetSourceRGBA(1.0, 0.62, 0.06, 0.88);
            ctx.LineWidth = 2;
            ctx.Rectangle(1, 1, Bounds.OuterWidth - 2, Bounds.OuterHeight - 2);
            ctx.Stroke();
            DrawText(ctx, "DRAG · LOCK IN HUB OPTIONS", Bounds.OuterWidth / 2.0, Bounds.OuterHeight - 5, 9, true, 1.0, 0.76, 0.26, true);
        }
    }

    private void DrawSlot(Context ctx, double x, double y, double size, SkillLoadoutSlotPacket? entry, int index)
    {
        ctx.SetSourceRGBA(0.035, 0.018, 0.014, 0.88);
        ctx.Rectangle(x, y, size, size);
        ctx.FillPreserve();
        double[] accent = ParseHex(entry?.Color);
        bool insufficient = entry != null && !HasResource(entry);
        ctx.SetSourceRGBA(accent[0], accent[1], accent[2], entry == null ? 0.30 : 0.84);
        ctx.LineWidth = 2;
        ctx.Stroke();

        if (entry?.Empowered == true)
        {
            ctx.SetSourceRGBA(1.0, 0.82, 0.4, 0.95);
            ctx.LineWidth = 3.0;
            ctx.Rectangle(x + 1.5, y + 1.5, size - 3.0, size - 3.0);
            ctx.Stroke();
            ctx.SetSourceRGBA(1.0, 0.82, 0.4, 0.28);
            ctx.Rectangle(x + 3.0, y + 3.0, size - 6.0, size - 6.0);
            ctx.Fill();
        }

        string binding = api.Input.GetHotKeyByCode(VrpgHotkeys.SkillCodes[index])?.CurrentMapping?.ToString() ?? (index + 1).ToString();
        DrawText(ctx, binding, x + size / 2.0, y + 12, 8, true, 0.84, 0.74, 0.62, true);
        if (entry == null || string.IsNullOrWhiteSpace(entry.Code))
        {
            DrawText(ctx, "—", x + size / 2.0, y + size * 0.62, size * 0.28, true, accent[0], accent[1], accent[2], true);
        }
        else
        {
            VrpgIconPainter.Draw(ctx, entry.Icon, x + size * 0.25, y + size * 0.22, size * 0.50, accent[0], accent[1], accent[2]);
        }
        if (entry != null && !string.IsNullOrWhiteSpace(entry.Code))
        {
            long now = api.World.ElapsedMilliseconds;
            int currentCharges = SkillChargeProjection.Current(entry, snapshotAtMilliseconds, now);
            if (entry.ResourceCost > 0f && !string.Equals(entry.ResourceType, "none", StringComparison.OrdinalIgnoreCase))
            {
                string rate = string.Equals(entry.ResourceCostMode, "per_second", StringComparison.OrdinalIgnoreCase) ? "/s" : "";
                DrawText(ctx, entry.ResourceCost.ToString("0.#") + " " + ResourceLabel(entry.ResourceType) + rate, x + 4, y + size - 5, 7, true,
                    insufficient ? 1.0 : 0.78, insufficient ? 0.28 : 0.78, insufficient ? 0.22 : 0.70, false);
            }
            double remaining = SkillChargeProjection.RemainingSeconds(entry, snapshotAtMilliseconds, now);
            bool unavailable = entry.MaximumCharges <= 1 || currentCharges <= 0;
            if (remaining > 0.01 && entry.CooldownSeconds > 0f && unavailable)
            {
                double fraction = Math.Clamp(remaining / entry.CooldownSeconds, 0.0, 1.0);
                ctx.SetSourceRGBA(0.0, 0.0, 0.0, 0.70);
                ctx.Rectangle(x + 2, y + 2, size - 4, (size - 4) * fraction);
                ctx.Fill();
                DrawText(ctx, remaining.ToString("0.0"), x + size / 2.0, y + size * 0.60, 14, true, 1.0, 1.0, 1.0, true);
            }
            else if (remaining > 0.01 && entry.CooldownSeconds > 0f && entry.MaximumCharges > 1)
            {
                double recovered = 1.0 - Math.Clamp(remaining / entry.CooldownSeconds, 0.0, 1.0);
                ctx.SetSourceRGBA(accent[0], accent[1], accent[2], 0.9);
                ctx.Rectangle(x + 2, y + size - 3, (size - 4) * recovered, 2);
                ctx.Fill();
            }
            else if (insufficient)
            {
                ctx.SetSourceRGBA(0.8, 0.06, 0.03, 0.24);
                ctx.Rectangle(x + 2, y + 2, size - 4, size - 4);
                ctx.Fill();
            }

            if (entry.MaximumCharges > 1)
            {
                DrawText(
                    ctx,
                    currentCharges + "/" + entry.MaximumCharges,
                    x + size - 4,
                    y + size - 5,
                    8,
                    true,
                    currentCharges > 0 ? 1.0 : 0.72,
                    currentCharges > 0 ? 0.82 : 0.28,
                    currentCharges > 0 ? 0.42 : 0.22,
                    false,
                    right: true);
            }
        }
    }

    private bool HasResource(SkillLoadoutSlotPacket entry)
    {
        if (entry.ResourceCost <= 0f) return true;
        float required = string.Equals(entry.ResourceCostMode, "per_second", StringComparison.OrdinalIgnoreCase)
            ? entry.ResourceCost * Math.Max(0.05f, entry.HitIntervalSeconds)
            : entry.ResourceCost;
        string type = (entry.ResourceType ?? "").Trim().ToLowerInvariant();
        return type switch
        {
            "mana" or "mp" => resources.Mana + 0.001f >= required,
            "blood" => resources.BloodUnlocked && resources.Blood + 0.001f >= required,
            _ => true
        };
    }

    private static string ResourceLabel(string type)
    {
        return string.Equals(type, "blood", StringComparison.OrdinalIgnoreCase) ? "BL" : "MP";
    }

    private string CurrentBindingSignature()
    {
        var labels = new string[VrpgHotkeys.SkillCodes.Length];
        for (int i = 0; i < labels.Length; i++)
        {
            labels[i] = api.Input.GetHotKeyByCode(VrpgHotkeys.SkillCodes[i])?.CurrentMapping?.ToString() ?? "";
        }
        return string.Join("|", labels);
    }

    private SkillLoadoutSlotPacket? FindSlot(int slot)
    {
        for (int i = 0; i < snapshot.Slots.Length; i++)
        {
            if (snapshot.Slots[i].Slot == slot && !string.IsNullOrWhiteSpace(snapshot.Slots[i].Code)) return snapshot.Slots[i];
        }
        return null;
    }

    private bool HasActiveCooldown(long now)
    {
        for (int i = 0; i < snapshot.Slots.Length; i++)
        {
            if (SkillChargeProjection.RemainingSeconds(snapshot.Slots[i], snapshotAtMilliseconds, now) > 0.01) return true;
        }
        return false;
    }

    private static void DrawText(Context ctx, string text, double x, double y, double size, bool bold, double r, double g, double b, bool center, bool right = false)
    {
        ctx.SelectFontFace("Arial", FontSlant.Normal, bold ? FontWeight.Bold : FontWeight.Normal);
        ctx.SetFontSize(size);
        TextExtents ext = ctx.TextExtents(text);
        double drawX = center ? x - ext.Width / 2.0 - ext.XBearing : right ? x - ext.Width : x;
        ctx.SetSourceRGBA(0, 0, 0, 0.75);
        ctx.MoveTo(drawX + 1, y + 1);
        ctx.ShowText(text);
        ctx.SetSourceRGBA(r, g, b, 1);
        ctx.MoveTo(drawX, y);
        ctx.ShowText(text);
    }

    private static double[] ParseHex(string? value)
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
}
