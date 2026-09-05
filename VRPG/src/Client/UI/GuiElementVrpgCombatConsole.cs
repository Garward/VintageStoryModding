using System;
using System.Collections.Generic;
using Cairo;
using VRPG.Config;
using VRPG.Network;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VRPG.Client.UI;

/// <summary>Draws the bounded dynamic layers of the unified combat console.</summary>
public sealed class GuiElementVrpgCombatConsole : GuiElement
{
    private const int HotbarSlotCount = 10;
    private readonly RpgHudConfig hudConfig;
    private readonly RpgSkillHotbarConfig skillConfig;
    private readonly double drawScale;
    private readonly BoundedItemStackRenderer itemRenderer;
    private RpgResourcePacket resources = new RpgResourcePacket
    {
        Health = 100f,
        MaxHealth = 100f,
        Mana = 100f,
        MaxMana = 100f,
        ExperienceToNextLevel = 1000,
        Level = 1,
        HudEnabled = true
    };
    private SkillLoadoutPacket skills = new SkillLoadoutPacket();
    private long skillSnapshotAtMilliseconds;
    private long nextRedrawMilliseconds;
    private long nextStateCheckMilliseconds;
    private int textureId;
    private int lastActiveSlot = -1;
    private string lastBindingSignature = "";
    private string lastHotbarSignature = "";
    private bool lastCombatActionMode;
    private readonly HashSet<int> pressedActionSlots = new HashSet<int>();

    public override bool Focusable => false;

    public GuiElementVrpgCombatConsole(
        ICoreClientAPI api,
        ElementBounds bounds,
        RpgHudConfig hudConfig,
        RpgSkillHotbarConfig skillConfig,
        double drawScale) : base(api, bounds)
    {
        this.hudConfig = hudConfig;
        this.skillConfig = skillConfig;
        this.drawScale = Math.Max(0.25, drawScale);
        itemRenderer = new BoundedItemStackRenderer(api, bounds);
    }

    public void SetResources(RpgResourcePacket packet)
    {
        resources = Clone(packet);
        Redraw();
    }

    public void SetSkills(SkillLoadoutPacket packet)
    {
        skills = packet ?? new SkillLoadoutPacket();
        skillSnapshotAtMilliseconds = api.World?.ElapsedMilliseconds ?? 0;
        Redraw();
    }

    public void SetSkillSlotCount(int slotCount)
    {
        skillConfig.SlotCount = Math.Clamp(slotCount, 4, 8);
        Redraw();
    }

    public void SetActionPressed(int slot, bool pressed)
    {
        if (pressed ? pressedActionSlots.Add(slot) : pressedActionSlots.Remove(slot))
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
        if (!resources.HudEnabled)
        {
            return;
        }

        long now = api.World?.ElapsedMilliseconds ?? 0;
        bool periodic = now >= nextStateCheckMilliseconds;
        if (periodic)
        {
            nextStateCheckMilliseconds = now + 100;
            int activeSlot = api.World?.Player?.InventoryManager?.ActiveHotbarSlotNumber ?? -1;
            bool combatActionMode = IsCombatActionMode();
            string bindings = BindingSignature();
            string hotbar = HotbarSignature();
            if (activeSlot != lastActiveSlot
                || combatActionMode != lastCombatActionMode
                || !string.Equals(bindings, lastBindingSignature, StringComparison.Ordinal)
                || !string.Equals(hotbar, lastHotbarSignature, StringComparison.Ordinal))
            {
                lastActiveSlot = activeSlot;
                lastCombatActionMode = combatActionMode;
                lastBindingSignature = bindings;
                lastHotbarSignature = hotbar;
                Redraw();
            }
        }

        bool animated = PredictResources(deltaTime) || HasActiveCooldown(now);
        if (animated && now >= nextRedrawMilliseconds)
        {
            nextRedrawMilliseconds = now + 50;
            Redraw();
        }

        if (textureId > 0)
        {
            api.Render.Render2DTexturePremultipliedAlpha(textureId, Bounds);
            RenderHotbarItems(deltaTime);
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
        ctx.Scale(drawScale, drawScale);
        Draw(ctx, Bounds.OuterWidth / drawScale, Bounds.OuterHeight / drawScale);
        generateTexture(surface, ref textureId);
    }

    private void Draw(Context ctx, double width, double height)
    {
        ctx.Operator = Operator.Clear;
        ctx.Paint();
        ctx.Operator = Operator.Over;

        CombatConsoleGeometry geometry = CombatConsoleGeometry.Resolve(width, height);
        DrawChassis(ctx, geometry);
        DrawResourcePlate(ctx, geometry.LeftResourceX, geometry.ResourceY, geometry.ResourceWidth,
            geometry.ResourceHeight, "HEALTH", resources.Health, resources.MaxHealth, ResourceColor.Health, false);
        DrawResourcePlate(ctx, geometry.RightResourceX, geometry.ResourceY, geometry.ResourceWidth,
            geometry.ResourceHeight, "MANA", resources.Mana, resources.MaxMana, ResourceColor.Mana, true);
        if (IsCombatActionMode())
        {
            DrawCombatActionSlots(ctx, geometry);
            DrawCombatProfilePlate(ctx, geometry);
        }
        else
        {
            DrawHotbarSlots(ctx, geometry);
            DrawSkillSlots(ctx, geometry);
        }
        DrawExperience(ctx, geometry);
    }

    private static void DrawChassis(Context ctx, CombatConsoleGeometry g)
    {
        RoundedRect(ctx, 1, g.ChassisY, g.Width - 2, g.ChassisHeight, 7);
        SetColor(ctx, 0.045, 0.052, 0.050, 0.96);
        ctx.FillPreserve();
        SetColor(ctx, 0.38, 0.24, 0.14, 0.96);
        ctx.LineWidth = 2;
        ctx.Stroke();

        RoundedRect(ctx, 5, g.ChassisY + 4, g.Width - 10, g.ChassisHeight - 8, 5);
        SetColor(ctx, 0.075, 0.084, 0.080, 0.94);
        ctx.FillPreserve();
        SetColor(ctx, 0.18, 0.12, 0.075, 0.92);
        ctx.LineWidth = 1;
        ctx.Stroke();

        DrawChassisWear(ctx, g);

        DrawRivet(ctx, 11, g.ChassisY + 11);
        DrawRivet(ctx, g.Width - 11, g.ChassisY + 11);
        DrawRivet(ctx, 11, g.ChassisY + g.ChassisHeight - 11);
        DrawRivet(ctx, g.Width - 11, g.ChassisY + g.ChassisHeight - 11);
    }

    private void DrawResourcePlate(
        Context ctx,
        double x,
        double y,
        double width,
        double height,
        string label,
        double value,
        double max,
        ResourceColor color,
        bool fillFromRight)
    {
        RoundedRect(ctx, x, y, width, height, 4);
        SetColor(ctx, 0.025, 0.030, 0.029, 1);
        ctx.FillPreserve();
        SetColor(ctx, 0.34, 0.22, 0.13, 1);
        ctx.LineWidth = 1.5;
        ctx.Stroke();

        const double inset = 4;
        double barY = y + height * 0.42;
        double barHeight = Math.Max(10, height * 0.36);
        double innerWidth = Math.Max(1, width - inset * 2);
        double fraction = max <= 0 ? 0 : GameMath.Clamp(value / max, 0, 1);

        ResourceChannelPath(ctx, x + inset, barY, innerWidth, barHeight, fillFromRight);
        SetColor(ctx, color.BackR, color.BackG, color.BackB, 1);
        ctx.FillPreserve();
        SetColor(ctx, 0.28, 0.18, 0.11, 0.9);
        ctx.LineWidth = 0.8;
        ctx.Stroke();

        double fillWidth = innerWidth * fraction;
        if (fillWidth > 0)
        {
            double fillX = fillFromRight ? x + inset + innerWidth - fillWidth : x + inset;
            ctx.Save();
            ResourceChannelPath(ctx, x + inset, barY, innerWidth, barHeight, fillFromRight);
            ctx.Clip();

            double outerX = fillFromRight ? fillX + fillWidth : fillX;
            double innerX = fillFromRight ? fillX : fillX + fillWidth;
            using (LinearGradient gradient = new LinearGradient(outerX, barY, innerX, barY))
            {
                gradient.AddColorStop(0, new Color(color.FillR * 0.58, color.FillG * 0.58, color.FillB * 0.58, 0.98));
                gradient.AddColorStop(0.58, new Color(color.FillR, color.FillG, color.FillB, 0.99));
                gradient.AddColorStop(1, new Color(
                    Math.Min(1, color.FillR * 1.20 + 0.04),
                    Math.Min(1, color.FillG * 1.20 + 0.04),
                    Math.Min(1, color.FillB * 1.20 + 0.04),
                    1));
                ctx.SetSource(gradient);
                ctx.Rectangle(fillX, barY, fillWidth, barHeight);
                ctx.Fill();
            }

            SetColor(ctx, 1, 1, 1, 0.12);
            ctx.Rectangle(fillX, barY, fillWidth, Math.Max(1, barHeight * 0.28));
            ctx.Fill();
            ctx.Restore();

            ResourceChannelPath(ctx, x + inset, barY, innerWidth, barHeight, fillFromRight);
            SetColor(ctx, 0.28, 0.18, 0.11, 0.9);
            ctx.LineWidth = 0.8;
            ctx.Stroke();
        }

        string amount = Format(value) + " / " + Format(max);
        DrawText(ctx, label, fillFromRight ? x + width - 7 : x + 7, y + 15, 10, true,
            0.70, 0.61, 0.49, center: false, right: fillFromRight);
        DrawText(ctx, amount, fillFromRight ? x + 7 : x + width - 7, y + 15, 11, true,
            0.93, 0.89, 0.82, center: false, right: !fillFromRight);

        if (!fillFromRight && resources.MaxMagicShield > 0)
        {
            double shield = GameMath.Clamp(resources.MagicShield / Math.Max(1, resources.MaxMagicShield), 0, 1);
            SetColor(ctx, 0.48, 0.86, 0.91, 0.95);
            ctx.Rectangle(x + inset, y + height - 3, innerWidth * shield, 2);
            ctx.Fill();
        }
        else if (fillFromRight && resources.BloodUnlocked && resources.MaxBlood > 0)
        {
            double blood = GameMath.Clamp(resources.Blood / Math.Max(1, resources.MaxBlood), 0, 1);
            double bloodWidth = innerWidth * blood;
            SetColor(ctx, 0.62, 0.06, 0.10, 0.95);
            ctx.Rectangle(x + inset + innerWidth - bloodWidth, y + height - 3, bloodWidth, 2);
            ctx.Fill();
        }
    }

    private void DrawHotbarSlots(Context ctx, CombatConsoleGeometry g)
    {
        int active = api.World?.Player?.InventoryManager?.ActiveHotbarSlotNumber ?? -1;
        for (int i = 0; i < HotbarSlotCount; i++)
        {
            double x = g.HotbarX + i * (g.HotbarSlot + g.HotbarGap);
            bool selected = active == i;
            RoundedRect(ctx, x, g.HotbarY - (selected ? 2 : 0), g.HotbarSlot, g.HotbarSlot + (selected ? 2 : 0), 3);
            SetColor(ctx, selected ? 0.12 : 0.035, selected ? 0.075 : 0.042, selected ? 0.035 : 0.040, 0.98);
            ctx.FillPreserve();
            SetColor(ctx, selected ? 1.0 : 0.42, selected ? 0.55 : 0.29, selected ? 0.08 : 0.17, selected ? 1.0 : 0.92);
            ctx.LineWidth = selected ? 2.3 : 1.2;
            ctx.Stroke();

            string binding = api.Input.GetHotKeyByCode("hotbarslot" + (i + 1))?.CurrentMapping?.ToString() ?? (i + 1).ToString();
            DrawText(ctx, binding, x + g.HotbarSlot - 4, g.HotbarY + 10, 7.5, true,
                selected ? 1.0 : 0.66, selected ? 0.72 : 0.58, selected ? 0.22 : 0.47, false, true);
        }
    }

    private void DrawCombatActionSlots(Context ctx, CombatConsoleGeometry g)
    {
        long now = api.World?.ElapsedMilliseconds ?? 0;
        for (int i = 0; i < HotbarSlotCount; i++)
        {
            double x = g.HotbarX + i * (g.HotbarSlot + g.HotbarGap);
            bool weaponSocket = i == 8;
            bool hasWeapon = weaponSocket && CombatHeldItemController.IsWeapon(
                api.World?.Player?.InventoryManager?.ActiveHotbarSlot?.Itemstack);
            SkillLoadoutSlotPacket? skill = i < VrpgHotkeys.SkillCodes.Length ? FindSkill(i + 1) : null;
            bool pressed = pressedActionSlots.Contains(i);
            double[] accent = ParseHex(skill?.Color);

            RoundedRect(ctx, x, g.HotbarY - (pressed ? 2 : 0), g.HotbarSlot, g.HotbarSlot + (pressed ? 2 : 0), 3);
            SetColor(ctx, pressed ? 0.14 : 0.03, pressed ? 0.075 : 0.034, pressed ? 0.025 : 0.032, 0.99);
            ctx.FillPreserve();
            SetColor(ctx, accent[0], accent[1], accent[2], skill == null ? 0.24 : pressed ? 1 : 0.70);
            ctx.LineWidth = pressed || skill?.Empowered == true ? 2.3 : 1.2;
            ctx.Stroke();

            if (weaponSocket)
            {
                DrawText(ctx, "WEAPON", x + g.HotbarSlot / 2, g.HotbarY + 9, 6.5, true,
                    hasWeapon ? 0.86 : 0.48, hasWeapon ? 0.62 : 0.42, hasWeapon ? 0.28 : 0.35, true);
                if (!hasWeapon)
                {
                    DrawText(ctx, "—", x + g.HotbarSlot / 2, g.HotbarY + g.HotbarSlot * 0.68, 11, false,
                        0.31, 0.28, 0.24, true);
                }
                continue;
            }

            string binding = api.Input.GetHotKeyByCode("hotbarslot" + (i + 1))?.CurrentMapping?.ToString() ?? "";
            DrawText(ctx, binding, x + g.HotbarSlot - 4, g.HotbarY + 10, 7.5, true,
                skill == null ? 0.48 : 0.76, skill == null ? 0.42 : 0.66, skill == null ? 0.35 : 0.48, false, true);

            if (skill == null || string.IsNullOrWhiteSpace(skill.Code))
            {
                DrawText(ctx, "—", x + g.HotbarSlot / 2, g.HotbarY + g.HotbarSlot * 0.68, 11, false,
                    0.31, 0.28, 0.24, true);
                continue;
            }

            VrpgIconPainter.Draw(ctx, skill.Icon, x + g.HotbarSlot * 0.22, g.HotbarY + g.HotbarSlot * 0.20,
                g.HotbarSlot * 0.56, accent[0], accent[1], accent[2]);
            int charges = SkillChargeProjection.Current(skill, skillSnapshotAtMilliseconds, now);
            double remaining = SkillChargeProjection.RemainingSeconds(skill, skillSnapshotAtMilliseconds, now);
            if (remaining > 0.01 && skill.CooldownSeconds > 0 && (skill.MaximumCharges <= 1 || charges <= 0))
            {
                double fraction = Math.Clamp(remaining / skill.CooldownSeconds, 0, 1);
                SetColor(ctx, 0, 0, 0, 0.72);
                ctx.Rectangle(x + 2, g.HotbarY + 2, g.HotbarSlot - 4, (g.HotbarSlot - 4) * fraction);
                ctx.Fill();
                DrawText(ctx, remaining.ToString(remaining >= 10 ? "0" : "0.0"), x + g.HotbarSlot / 2,
                    g.HotbarY + g.HotbarSlot * 0.67, 10, true, 0.95, 0.92, 0.86, true);
            }
            if (skill.MaximumCharges > 1)
            {
                DrawText(ctx, charges + "/" + skill.MaximumCharges, x + g.HotbarSlot - 3,
                    g.HotbarY + g.HotbarSlot - 4, 7, true, 0.96, 0.76, 0.28, false, true);
            }
        }
    }

    private static void DrawCombatProfilePlate(Context ctx, CombatConsoleGeometry g)
    {
        const double width = 92;
        double x = (g.Width - width) / 2;
        RoundedRect(ctx, x, 5, width, g.ChassisY + 7, 4);
        SetColor(ctx, 0.050, 0.058, 0.055, 0.99);
        ctx.FillPreserve();
        SetColor(ctx, 0.38, 0.23, 0.12, 1);
        ctx.LineWidth = 1.3;
        ctx.Stroke();
        DrawSkillRailBrace(ctx, x + 8, g.ChassisY, 7, 12);
        DrawSkillRailBrace(ctx, x + width - 15, g.ChassisY, 7, 12);
        DrawText(ctx, "COMBAT", g.Width / 2, 23, 9, true, 0.88, 0.57, 0.19, true);
    }

    private void DrawSkillSlots(Context ctx, CombatConsoleGeometry g)
    {
        int count = Math.Clamp(skillConfig.SlotCount, 4, 8);
        double totalWidth = count * g.SkillSlot + (count - 1) * g.SkillGap;
        double startX = (g.Width - totalWidth) / 2;
        long now = api.World?.ElapsedMilliseconds ?? 0;

        // A raised fitted plate joins the skill row to the main chassis. This keeps
        // the skills visually part of one instrument instead of floating above it.
        RoundedRect(ctx, startX - 7, 0, totalWidth + 14, g.ChassisY + 10, 5);
        SetColor(ctx, 0.050, 0.058, 0.055, 0.98);
        ctx.FillPreserve();
        SetColor(ctx, 0.35, 0.22, 0.13, 0.98);
        ctx.LineWidth = 1.4;
        ctx.Stroke();
        SetColor(ctx, 0.12, 0.075, 0.045, 0.9);
        ctx.Rectangle(startX + 5, g.ChassisY + 3, totalWidth - 10, 4);
        ctx.Fill();
        DrawSkillRailBrace(ctx, startX + 5, g.ChassisY + 1, 7, 12);
        DrawSkillRailBrace(ctx, startX + totalWidth - 12, g.ChassisY + 1, 7, 12);
        DrawRivet(ctx, startX - 1, g.ChassisY + 1);
        DrawRivet(ctx, startX + totalWidth + 1, g.ChassisY + 1);

        for (int i = 0; i < count; i++)
        {
            double x = startX + i * (g.SkillSlot + g.SkillGap);
            SkillLoadoutSlotPacket? skill = FindSkill(i + 1);
            double[] accent = ParseHex(skill?.Color);
            RoundedRect(ctx, x, g.SkillY, g.SkillSlot, g.SkillSlot, 3);
            SetColor(ctx, 0.03, 0.034, 0.032, 0.98);
            ctx.FillPreserve();
            SetColor(ctx, accent[0], accent[1], accent[2], skill == null ? 0.24 : 0.72);
            ctx.LineWidth = skill?.Empowered == true ? 2.4 : 1.2;
            ctx.Stroke();

            string binding = api.Input.GetHotKeyByCode(VrpgHotkeys.SkillCodes[i])?.CurrentMapping?.ToString() ?? "";
            DrawText(ctx, binding, x + g.SkillSlot - 3, g.SkillY + 9, 7, true,
                0.67, 0.58, 0.47, false, true);

            if (skill == null || string.IsNullOrWhiteSpace(skill.Code))
            {
                DrawText(ctx, "—", x + g.SkillSlot / 2, g.SkillY + g.SkillSlot * 0.68, 11, false,
                    0.31, 0.28, 0.24, true);
                continue;
            }

            VrpgIconPainter.Draw(ctx, skill.Icon, x + g.SkillSlot * 0.23, g.SkillY + g.SkillSlot * 0.20,
                g.SkillSlot * 0.54, accent[0], accent[1], accent[2]);
            int charges = SkillChargeProjection.Current(skill, skillSnapshotAtMilliseconds, now);
            double remaining = SkillChargeProjection.RemainingSeconds(skill, skillSnapshotAtMilliseconds, now);
            if (remaining > 0.01 && skill.CooldownSeconds > 0 && (skill.MaximumCharges <= 1 || charges <= 0))
            {
                double fraction = Math.Clamp(remaining / skill.CooldownSeconds, 0, 1);
                SetColor(ctx, 0, 0, 0, 0.72);
                ctx.Rectangle(x + 2, g.SkillY + 2, g.SkillSlot - 4, (g.SkillSlot - 4) * fraction);
                ctx.Fill();
                DrawText(ctx, remaining.ToString(remaining >= 10 ? "0" : "0.0"), x + g.SkillSlot / 2,
                    g.SkillY + g.SkillSlot * 0.67, 10, true, 0.95, 0.92, 0.86, true);
            }
            if (skill.MaximumCharges > 1)
            {
                DrawText(ctx, charges + "/" + skill.MaximumCharges, x + g.SkillSlot - 3,
                    g.SkillY + g.SkillSlot - 4, 7, true, 0.96, 0.76, 0.28, false, true);
            }
        }
    }

    private void DrawExperience(Context ctx, CombatConsoleGeometry g)
    {
        double max = Math.Max(1, resources.ExperienceToNextLevel);
        double fraction = GameMath.Clamp(resources.Experience / max, 0, 1);
        RoundedRect(ctx, g.XpX, g.XpY, g.XpWidth, g.XpHeight, 2);
        SetColor(ctx, 0.13, 0.10, 0.045, 1);
        ctx.Fill();
        if (fraction > 0)
        {
            RoundedRect(ctx, g.XpX, g.XpY, g.XpWidth * fraction, g.XpHeight, 2);
            using LinearGradient gradient = new LinearGradient(g.XpX, g.XpY, g.XpX + g.XpWidth, g.XpY);
            gradient.AddColorStop(0, new Color(0.55, 0.25, 0.025, 0.96));
            gradient.AddColorStop(0.62, new Color(0.93, 0.49, 0.045, 0.98));
            gradient.AddColorStop(1, new Color(1.0, 0.70, 0.12, 1));
            ctx.SetSource(gradient);
            ctx.Fill();
        }

        double badgeWidth = 42;
        double badgeHeight = 14;
        double badgeX = (g.Width - badgeWidth) / 2;
        double badgeY = g.XpY - 4;
        RoundedRect(ctx, badgeX, badgeY, badgeWidth, badgeHeight, 2);
        SetColor(ctx, 0.055, 0.060, 0.056, 1);
        ctx.FillPreserve();
        SetColor(ctx, 0.43, 0.27, 0.12, 1);
        ctx.LineWidth = 1;
        ctx.Stroke();
        DrawText(ctx, "LV " + resources.Level, g.Width / 2, badgeY + 10, 8, true,
            0.95, 0.82, 0.56, true);
    }

    private void RenderHotbarItems(float deltaTime)
    {
        if (IsCombatActionMode())
        {
            RenderCombatWeapon(deltaTime);
            return;
        }

        IInventory? hotbar = api.World?.Player?.InventoryManager?.GetHotbarInventory();
        if (hotbar == null)
        {
            return;
        }

        CombatConsoleGeometry g = CombatConsoleGeometry.Resolve(
            Bounds.OuterWidth / drawScale,
            Bounds.OuterHeight / drawScale);
        for (int i = 0; i < HotbarSlotCount && i < hotbar.Count; i++)
        {
            ItemSlot? slot = hotbar[i];
            if (slot?.Itemstack == null)
            {
                continue;
            }

            double slotX = g.HotbarX + i * (g.HotbarSlot + g.HotbarGap);
            itemRenderer.Render(
                slot,
                slotX,
                g.HotbarY,
                g.HotbarSlot,
                drawScale,
                2255,
                deltaTime,
                showStackSize: true);
        }
    }

    private void RenderCombatWeapon(float deltaTime)
    {
        ItemSlot? weapon = api.World?.Player?.InventoryManager?.ActiveHotbarSlot;
        if (weapon == null || !CombatHeldItemController.IsWeapon(weapon.Itemstack))
        {
            return;
        }

        CombatConsoleGeometry g = CombatConsoleGeometry.Resolve(
            Bounds.OuterWidth / drawScale,
            Bounds.OuterHeight / drawScale);
        const int weaponSocket = 8;
        double slotX = g.HotbarX + weaponSocket * (g.HotbarSlot + g.HotbarGap);
        itemRenderer.Render(
            weapon,
            slotX,
            g.HotbarY,
            g.HotbarSlot,
            drawScale,
            2255,
            deltaTime,
            showStackSize: false);
    }

    private bool PredictResources(float deltaTime)
    {
        double seconds = GameMath.Clamp(deltaTime, 0, 0.25f);
        bool changed = false;
        resources.Health = Predict(resources.Health, resources.MaxHealth, resources.HealthRegenPerSecond, seconds, out bool healthChanged);
        resources.Mana = Predict(resources.Mana, resources.MaxMana, resources.ManaRegenPerSecond, seconds, out bool manaChanged);
        resources.MagicShield = Predict(resources.MagicShield, resources.MaxMagicShield, resources.MagicShieldRegenPerSecond, seconds, out bool shieldChanged);
        changed |= healthChanged || manaChanged || shieldChanged;
        if (resources.BloodUnlocked)
        {
            resources.Blood = Predict(resources.Blood, resources.MaxBlood, resources.BloodRegenPerSecond, seconds, out bool bloodChanged);
            changed |= bloodChanged;
        }
        return changed;
    }

    private static float Predict(float value, float max, float rate, double seconds, out bool changed)
    {
        if (rate <= 0 || max <= 0 || value >= max)
        {
            changed = false;
            return value;
        }
        float next = (float)Math.Min(max, value + rate * seconds);
        if (Math.Abs(next - value) < 0.001f)
        {
            changed = false;
            return value;
        }
        changed = true;
        return next;
    }

    private bool HasActiveCooldown(long now)
    {
        for (int i = 0; i < skills.Slots.Length; i++)
        {
            if (SkillChargeProjection.RemainingSeconds(skills.Slots[i], skillSnapshotAtMilliseconds, now) > 0.01)
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsCombatActionMode() => VrpgClientHudRuntime.CombatActionBarActive;

    private SkillLoadoutSlotPacket? FindSkill(int slot)
    {
        for (int i = 0; i < skills.Slots.Length; i++)
        {
            if (skills.Slots[i].Slot == slot && !string.IsNullOrWhiteSpace(skills.Slots[i].Code))
            {
                return skills.Slots[i];
            }
        }
        return null;
    }

    private string BindingSignature()
    {
        string[] values = new string[HotbarSlotCount + VrpgHotkeys.SkillCodes.Length];
        for (int i = 0; i < HotbarSlotCount; i++)
        {
            values[i] = api.Input.GetHotKeyByCode("hotbarslot" + (i + 1))?.CurrentMapping?.ToString() ?? "";
        }
        for (int i = 0; i < VrpgHotkeys.SkillCodes.Length; i++)
        {
            values[HotbarSlotCount + i] = api.Input.GetHotKeyByCode(VrpgHotkeys.SkillCodes[i])?.CurrentMapping?.ToString() ?? "";
        }
        return string.Join("|", values);
    }

    private string HotbarSignature()
    {
        IInventory? hotbar = api.World?.Player?.InventoryManager?.GetHotbarInventory();
        if (hotbar == null)
        {
            return "";
        }
        string[] values = new string[HotbarSlotCount];
        for (int i = 0; i < HotbarSlotCount && i < hotbar.Count; i++)
        {
            ItemStack? stack = hotbar[i]?.Itemstack;
            values[i] = stack == null ? "" : stack.Class + ":" + stack.Id + ":" + stack.StackSize;
        }
        return string.Join("|", values);
    }

    private static RpgResourcePacket Clone(RpgResourcePacket packet)
    {
        return new RpgResourcePacket
        {
            Health = packet.Health,
            MaxHealth = packet.MaxHealth,
            Mana = packet.Mana,
            MaxMana = packet.MaxMana,
            MagicShield = packet.MagicShield,
            MaxMagicShield = packet.MaxMagicShield,
            Blood = packet.Blood,
            MaxBlood = packet.MaxBlood,
            BloodUnlocked = packet.BloodUnlocked,
            Experience = packet.Experience,
            ExperienceToNextLevel = packet.ExperienceToNextLevel,
            Level = packet.Level,
            HudEnabled = packet.HudEnabled,
            HealthRegenPerSecond = packet.HealthRegenPerSecond,
            ManaRegenPerSecond = packet.ManaRegenPerSecond,
            MagicShieldRegenPerSecond = packet.MagicShieldRegenPerSecond,
            BloodRegenPerSecond = packet.BloodRegenPerSecond
        };
    }

    private static void DrawRivet(Context ctx, double x, double y)
    {
        ctx.Arc(x, y, 2.3, 0, Math.PI * 2);
        SetColor(ctx, 0.27, 0.19, 0.12, 1);
        ctx.FillPreserve();
        SetColor(ctx, 0.55, 0.38, 0.20, 0.75);
        ctx.LineWidth = 0.8;
        ctx.Stroke();
    }

    private static void DrawChassisWear(Context ctx, CombatConsoleGeometry g)
    {
        // Fixed marks add material character without flickering when the dynamic
        // HUD texture is regenerated for resource and cooldown changes.
        SetColor(ctx, 0.32, 0.14, 0.055, 0.34);
        ctx.LineWidth = 0.8;
        DrawLine(ctx, 24, g.ChassisY + 6, 72, g.ChassisY + 6);
        DrawLine(ctx, g.Width - 91, g.ChassisY + g.ChassisHeight - 6,
            g.Width - 35, g.ChassisY + g.ChassisHeight - 6);
        DrawLine(ctx, g.HotbarX - 8, g.ChassisY + 12,
            g.HotbarX - 8, g.ChassisY + g.ChassisHeight - 13);
        DrawLine(ctx, g.HotbarX + g.XpWidth + 8, g.ChassisY + 12,
            g.HotbarX + g.XpWidth + 8, g.ChassisY + g.ChassisHeight - 13);

        SetColor(ctx, 0.50, 0.22, 0.07, 0.22);
        DrawWearSpeck(ctx, 20, g.ChassisY + 8, 1.2);
        DrawWearSpeck(ctx, 79, g.ChassisY + 6, 0.8);
        DrawWearSpeck(ctx, g.Width - 28, g.ChassisY + g.ChassisHeight - 7, 1.1);
        DrawWearSpeck(ctx, g.Width - 99, g.ChassisY + g.ChassisHeight - 6, 0.7);
    }

    private static void DrawSkillRailBrace(Context ctx, double x, double y, double width, double height)
    {
        RoundedRect(ctx, x, y, width, height, 1.5);
        SetColor(ctx, 0.065, 0.070, 0.066, 1);
        ctx.FillPreserve();
        SetColor(ctx, 0.33, 0.20, 0.105, 0.95);
        ctx.LineWidth = 0.9;
        ctx.Stroke();
    }

    private static void ResourceChannelPath(
        Context ctx,
        double x,
        double y,
        double width,
        double height,
        bool innerEndOnLeft)
    {
        double taper = Math.Min(6, width * 0.08);
        ctx.NewSubPath();
        if (innerEndOnLeft)
        {
            ctx.MoveTo(x + taper, y);
            ctx.LineTo(x + width, y);
            ctx.LineTo(x + width, y + height);
            ctx.LineTo(x + taper, y + height);
            ctx.LineTo(x, y + height / 2);
        }
        else
        {
            ctx.MoveTo(x, y);
            ctx.LineTo(x + width - taper, y);
            ctx.LineTo(x + width, y + height / 2);
            ctx.LineTo(x + width - taper, y + height);
            ctx.LineTo(x, y + height);
        }
        ctx.ClosePath();
    }

    private static void DrawLine(Context ctx, double x1, double y1, double x2, double y2)
    {
        ctx.MoveTo(x1, y1);
        ctx.LineTo(x2, y2);
        ctx.Stroke();
    }

    private static void DrawWearSpeck(Context ctx, double x, double y, double radius)
    {
        ctx.Arc(x, y, radius, 0, Math.PI * 2);
        ctx.Fill();
    }

    private static void RoundedRect(Context ctx, double x, double y, double width, double height, double radius)
    {
        if (width <= 0 || height <= 0)
        {
            return;
        }
        double r = Math.Min(radius, Math.Min(width, height) / 2);
        ctx.NewSubPath();
        ctx.Arc(x + width - r, y + r, r, -Math.PI / 2, 0);
        ctx.Arc(x + width - r, y + height - r, r, 0, Math.PI / 2);
        ctx.Arc(x + r, y + height - r, r, Math.PI / 2, Math.PI);
        ctx.Arc(x + r, y + r, r, Math.PI, Math.PI * 1.5);
        ctx.ClosePath();
    }

    private static void DrawText(
        Context ctx,
        string text,
        double x,
        double y,
        double size,
        bool bold,
        double r,
        double g,
        double b,
        bool center,
        bool right = false)
    {
        ctx.SelectFontFace("Arial", FontSlant.Normal, bold ? FontWeight.Bold : FontWeight.Normal);
        ctx.SetFontSize(size);
        TextExtents ext = ctx.TextExtents(text);
        double drawX = center ? x - ext.Width / 2 - ext.XBearing : right ? x - ext.Width : x;
        SetColor(ctx, 0, 0, 0, 0.82);
        ctx.MoveTo(drawX + 1, y + 1);
        ctx.ShowText(text);
        SetColor(ctx, r, g, b, 1);
        ctx.MoveTo(drawX, y);
        ctx.ShowText(text);
    }

    private static string Format(double value) => value >= 100 ? value.ToString("0") : value.ToString("0.#");

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
        return new[] { 1.0, 0.60, 0.09 };
    }

    private static void SetColor(Context ctx, double r, double g, double b, double a) => ctx.SetSourceRGBA(r, g, b, a);

    private readonly record struct ResourceColor(double FillR, double FillG, double FillB, double BackR, double BackG, double BackB)
    {
        public static ResourceColor Health => new ResourceColor(0.72, 0.035, 0.055, 0.20, 0.015, 0.020);
        public static ResourceColor Mana => new ResourceColor(0.10, 0.48, 0.62, 0.025, 0.12, 0.16);
    }
}

public readonly record struct CombatConsoleGeometry(
    double Width,
    double ChassisY,
    double ChassisHeight,
    double LeftResourceX,
    double RightResourceX,
    double ResourceY,
    double ResourceWidth,
    double ResourceHeight,
    double HotbarX,
    double HotbarY,
    double HotbarSlot,
    double HotbarGap,
    double SkillY,
    double SkillSlot,
    double SkillGap,
    double XpX,
    double XpY,
    double XpWidth,
    double XpHeight)
{
    public static CombatConsoleGeometry Resolve(double width, double height)
    {
        double margin = 12;
        double gap = width < 760 ? 4 : 7;
        double slotGap = width < 760 ? 2 : 3;
        double availableForHotbar = width * (width < 760 ? 0.56 : 0.50);
        double slot = Math.Clamp((availableForHotbar - slotGap * 9) / 10, 29, 46);
        double hotbarWidth = slot * 10 + slotGap * 9;
        double hotbarX = (width - hotbarWidth) / 2;
        double resourceWidth = Math.Max(78, hotbarX - margin - gap);
        double chassisY = 31;
        double chassisHeight = height - chassisY - 2;
        double hotbarY = chassisY + 19;
        double resourceY = hotbarY + 1;
        double resourceHeight = Math.Max(42, slot - 2);
        double skillSlot = width < 760 ? 27 : 31;
        double skillGap = 4;
        double xpX = hotbarX;
        double xpY = Math.Min(height - 11, hotbarY + slot + 7);
        return new CombatConsoleGeometry(
            width,
            chassisY,
            chassisHeight,
            margin,
            width - margin - resourceWidth,
            resourceY,
            resourceWidth,
            resourceHeight,
            hotbarX,
            hotbarY,
            slot,
            slotGap,
            2,
            skillSlot,
            skillGap,
            xpX,
            xpY,
            hotbarWidth,
            6);
    }
}
