using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Cairo;
using VRPG.Client.PlayerMenu.Components;
using VRPG.Client.PlayerMenu.Hub;
using VRPG.Client.PlayerMenu.Inventory;
using VRPG.Network;
using Vintagestory.API.Client;

namespace VRPG.Client.PlayerMenu.Shell;

public sealed partial class PlayerMenuShell
{
    private bool StatsSkillsOpen => hub != null && session.Page is PlayerMenuPage.Stats or PlayerMenuPage.Skills;
    private MenuRect SkillsList => new(Layout.Browser.X + 8, Layout.Browser.Y + 48, Layout.Browser.Width - 16, Math.Max(0, Layout.Browser.Height - 56));
    private int SkillRows => Math.Max(1, (int)(SkillsList.Height / 52));
    private static string Number(float value) => value.ToString("0.#", CultureInfo.CurrentCulture);
    private static string HubName(string name, string code, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(name) && !name.Contains(':') && name != code) return name;
        string humanized = PlayerItemDisplayNameResolver.HumanizeAssetPath(code);
        return humanized.Length > 0 ? humanized : T(fallback);
    }
    private void HubBrowseLabel(Context ctx, string text, MenuRect bounds, double size)
    {
        ctx.SelectFontFace(PlayerMenuTheme.Font, FontSlant.Normal, FontWeight.Normal); ctx.SetFontSize(size);
        if (ctx.TextExtents(text).Width > bounds.Width)
        {
            int[] starts = StringInfo.ParseCombiningCharacters(text);
            int length = starts.Length;
            while (length > 0 && ctx.TextExtents(text[..starts[--length]] + "…").Width > bounds.Width) { }
            text = text[..starts[length]] + "…";
        }
        Text(ctx, text, bounds, size);
    }

    private void DrawStats(Context ctx)
    {
        var packet = hub!.Packet!; var page = hub.Stats; var b = Layout.Browser;
        bool details = Layout.AlternatePanes && session.Current.InspectorOnly;
        if (!details)
        {
            PlayerMenuDrawing.DrawWell(ctx, b);
            Text(ctx, packet.Resources is { } resource ? T("stats-points", resource.UnspentStatPoints) : T("stats-empty"),
                new(b.X + 12, b.Y + 6, b.Width - 24, 23), 15);
            if (packet.Resources != null)
            {
                double rowHeight = Math.Min(54, (b.Height - 34) / 3);
                int i = 0;
                foreach (string code in new[] { "strength", "dexterity", "intelligence" })
                {
                    var row = new MenuRect(b.X + 8, b.Y + 32 + i++ * rowHeight, b.Width - 16, rowHeight - 3);
                    PlayerMenuDrawing.DrawPlate(ctx, row);
                    Text(ctx, T("stat-" + code), new(row.X + 8, row.Y + 3, row.Width - 136, row.Height - 4), 15);
                    Text(ctx, StatsPage.Value(packet, code).ToString(CultureInfo.CurrentCulture), new(row.Right - 126, row.Y + 3, 38, row.Height - 4), 16);
                    foreach (int delta in new[] { -1, 1 })
                    {
                        var button = new MenuRect(row.Right - (delta < 0 ? 82 : 40), row.Y + 1, 36, row.Height - 2);
                        Button(ctx, $"stat:{code}:{delta}", "", button, page.Pending && page.FeedbackCode == code ? MenuControlState.Pending
                            : page.CanAdjust(code, delta) ? MenuControlState.Default : MenuControlState.Disabled, delta < 0 ? "−" : "+");
                    }
                }
                if (b.Height > 220)
                {
                    string hint = packet.Resources.CombatLockRemainingSeconds > 0 ? T("stats-combat-locked", Number(packet.Resources.CombatLockRemainingSeconds)) : T("stats-hint");
                    Text(ctx, hint, new(b.X + 12, b.Y + 208, b.Width - 24, b.Height - 220), 15, PlayerMenuTheme.Muted);
                }
            }
        }
        if (!Layout.AlternatePanes || details) DrawStatsSummary(ctx, packet);
        double feedbackWidth = Layout.AlternatePanes ? Layout.Browser.Width - 120 : Layout.Browser.Width;
        string feedback = page.Pending ? T("hub-pending") : page.Feedback.Length > 0 ? page.Feedback
            : packet.Resources?.CombatLockRemainingSeconds > 0 ? T("stats-combat-locked", Number(packet.Resources.CombatLockRemainingSeconds)) : "";
        Text(ctx, feedback, new(Layout.Browser.X, Layout.PrimaryAction.Y, feedbackWidth, Layout.PrimaryAction.Height), 13,
            page.FeedbackError ? PlayerMenuTheme.Danger : PlayerMenuTheme.Muted);
        if (Layout.AlternatePanes)
            Button(ctx, "hub:details", details ? "stats-allocation" : "stats-summary", Layout.PrimaryAction with { X = Layout.PrimaryAction.Right - 112, Width = 112 });
    }

    private void DrawStatsSummary(Context ctx, OpenHubPacket packet)
    {
        var lines = new List<(string Text, double Size)>();
        if (hub!.Stats.Feedback.Length > 0) lines.Add((hub.Stats.Feedback, 15));
        if (packet.Resources is not { } r) lines.Add((T("stats-empty"), 18));
        else
        {
            lines.Add((T("stats-level", r.Level), 21));
            if (packet.Classes.Length > 0) lines.Add((string.Join(" · ", packet.Classes.Select(c => HubName(c.Name, c.Code, "unknown-class"))), 17));
            lines.Add((T("stats-experience", r.Experience, r.ExperienceToNextLevel), 14));
            lines.Add((T("stats-health", Number(r.Health), Number(r.MaxHealth)), 17));
            lines.Add((T("stats-mana", Number(r.Mana), Number(r.MaxMana)), 17));
            if (r.MaxMagicShield > 0) lines.Add((T("stats-shield", Number(r.MagicShield), Number(r.MaxMagicShield)), 17));
            if (r.BloodUnlocked) lines.Add((T("stats-blood", Number(r.Blood), Number(r.MaxBlood)), 17));
            lines.Add((T("stats-recovery"), 18));
            lines.Add((T("stats-health-regen", Number(r.HealthRegenPerSecond)), 15));
            lines.Add((T("stats-mana-regen", Number(r.ManaRegenPerSecond)), 15));
            if (r.MaxMagicShield > 0) lines.Add((T("stats-shield-regen", Number(r.MagicShieldRegenPerSecond)), 15));
            if (r.BloodUnlocked) lines.Add((T("stats-blood-regen", Number(r.BloodRegenPerSecond)), 15));
            lines.Add((T("stats-combat"), 18));
            string primary = r.PrimaryAttribute.Replace("vrpg:", "", StringComparison.Ordinal);
            if (StatsPage.IsSupported(primary)) lines.Add((T("stats-primary", T("stat-" + primary)), 15));
            lines.Add((T(r.EvasiveStepActive ? r.EvasiveStepCooldownRemainingSeconds > .05f ? "stats-evasion-cooldown" : "stats-evasion-ready" : "stats-evasion-locked", Number(r.EvasiveStepCooldownRemainingSeconds)), 15));
        }
        DrawHubDetails(ctx, lines);
    }

    private void DrawSkills(Context ctx)
    {
        var page = hub!.Skills; var b = Layout.Browser;
        bool details = !Layout.AlternatePanes || session.Current.InspectorOnly;
        if (!Layout.AlternatePanes || !details)
        {
            PlayerMenuDrawing.DrawWell(ctx, b);
            if (page.Classes.Length == 0) Text(ctx, T("skills-empty"), new(b.X + 14, b.Y + 14, b.Width - 28, b.Height - 28), 18);
            else
            {
                Button(ctx, "class:previous", "", new(b.X + 8, b.Y + 8, 34, 30), page.Classes.Length > 1 ? MenuControlState.Default : MenuControlState.Disabled, "‹");
                Button(ctx, "class:next", "", new(b.Right - 42, b.Y + 8, 34, 30), page.Classes.Length > 1 ? MenuControlState.Default : MenuControlState.Disabled, "›");
                HubBrowseLabel(ctx, HubName(page.SelectedClass!.Name, page.SelectedClass.Code, "unknown-class"), new(b.X + 50, b.Y + 8, b.Width - 100, 24), 17);
                var clip = SkillsList;
                int start = Math.Clamp(session.Current.ScrollAnchor, 0, Math.Max(0, page.Skills.Length - SkillRows));
                ctx.Save(); ctx.Rectangle(clip.X, clip.Y, clip.Width, clip.Height); ctx.Clip();
                for (int i = start; i < Math.Min(page.Skills.Length, start + SkillRows); i++)
                {
                    var skill = page.Skills[i]; var row = new MenuRect(clip.X, clip.Y + (i - start) * 52, clip.Width, 48);
                    Button(ctx, "skill:" + i, "", row, i == page.SelectedSkillIndex ? MenuControlState.Selected : MenuControlState.Default);
                    HubBrowseLabel(ctx, HubName(skill.Name, skill.Code, "unknown-skill"), new(row.X + 8, row.Y + 2, row.Width - 16, 22), 15);
                    Text(ctx, SkillRank(skill), new(row.X + 8, row.Y + 25, row.Width - 16, 20), 13, PlayerMenuTheme.Muted);
                }
                if (page.Skills.Length == 0) Text(ctx, T("skills-class-empty"), clip, 16);
                ctx.Restore();
                controls.RemoveAll(c => c.Id.StartsWith("skill:", StringComparison.Ordinal) && (c.Bounds.Y < clip.Y || c.Bounds.Bottom > clip.Bottom));
            }
        }
        if (details) DrawSkillDetails(ctx);
        else if (page.SelectedClass != null) Button(ctx, "hub:details", "skills-details", Layout.PrimaryAction);
    }
    private static string SkillRank(HubSkillPacket skill) => skill.LearnedLevel > 0
        ? T("skill-rank", skill.LearnedLevel, Math.Max(1, skill.MaxLevel)) : T("skill-locked", Math.Max(1, skill.RequiredLevel));

    private void DrawSkillDetails(Context ctx)
    {
        var page = hub!.Skills; var skill = page.SelectedSkill;
        var lines = new List<(string Text, double Size)>();
        if (skill == null)
        {
            if (page.SelectedClass is { } selectedClass)
            {
                lines.Add((HubName(selectedClass.Name, selectedClass.Code, "unknown-class"), 21));
                lines.Add((selectedClass.Description, 15));
            }
            lines.Add((T("skills-class-empty"), 18));
        }
        else
        {
            lines.Add((HubName(skill.Name, skill.Code, "unknown-skill"), 21));
            lines.Add((SkillRank(skill), 15));
            if (page.Pending) lines.Add((T("hub-pending"), 15));
            else if (page.Feedback.Length > 0) lines.Add((page.Feedback, 15));
            // Put assignment-changing values before prose so long descriptions cannot bury them.
            lines.Add((T(skill.TimingMode == "instant" ? "skill-damage" : "skill-damage-hit", Number(skill.Damage)), 16));
            if (skill.DamageType.Length > 0) lines.Add((T("skill-damage-type", PlayerItemDisplayNameResolver.HumanizeAssetPath(skill.DamageType)), 15));
            string delivery = skill.Delivery switch
            {
                "melee_arc" => T("skill-melee-arc", Number(skill.MeleeArcDegrees)),
                "melee_line" => T("skill-melee-line", Number(skill.MeleeWidth)),
                "melee_single" => T("skill-melee-single", Number(skill.MeleeWidth)),
                "circle" => T("skill-circle"),
                "raycast_aoe" => T("skill-raycast"),
                "targeted_drop" => T("skill-targeted-drop"),
                "projectile_aoe" => T(skill.ProjectileImpactMode == "ground" ? "skill-projectile-ground" : "skill-projectile-direct"),
                _ => ""
            };
            if (delivery.Length > 0) lines.Add((delivery, 15));
            if (skill.TimingMode == "sequence") lines.Add((T("skill-sequence", skill.HitCount, Number(skill.HitIntervalSeconds)), 15));
            else if (skill.TimingMode == "channel") lines.Add((T("skill-channel", Number(skill.HitIntervalSeconds)), 15));
            lines.Add((T("skill-cooldown", Number(skill.CooldownSeconds)), 15));
            if (skill.ResourceCost > 0)
            {
                string resource = skill.ResourceType is "health" or "mana" or "blood" or "magicshield" ? T("resource-" + skill.ResourceType) : T("skill-resource");
                lines.Add((T(skill.ResourceCostMode == "per_second" ? "skill-cost-second" : "skill-cost", Number(skill.ResourceCost), resource), 15));
            }
            if (skill.Range > 0) lines.Add((T("skill-range", Number(skill.Range)), 15));
            if (skill.Radius > 0) lines.Add((T("skill-radius", Number(skill.Radius)), 15));
            lines.Add((T(skill.LearnedLevel > 0 ? "skills-assign-hint" : "skills-learn-hint"), 14));
            foreach (var equipped in page.Classes.SelectMany(c => c.Skills).Where(s => s.EquippedSlot > 0 && s.EquippedSlot <= page.VisibleSlots))
                lines.Add((T("skill-equipped", equipped.EquippedSlot, HubName(equipped.Name, equipped.Code, "unknown-skill")), 14));
            lines.Add((skill.Description, 15));
            if (page.SelectedClass is { } cls) { lines.Add((HubName(cls.Name, cls.Code, "unknown-class"), 18)); lines.Add((cls.Description, 15)); }
        }
        DrawHubDetails(ctx, lines);
        if (skill == null) return;
        var footer = Layout.PrimaryAction with { Y = Layout.PrimaryAction.Y + 8, Height = Layout.PrimaryAction.Height - 8 };
        Text(ctx, T(page.Pending ? "hub-pending" : page.FeedbackError ? "skills-slot-rejected"
            : skill.LearnedLevel <= 0 ? "skills-slot-locked" : skill.EquippedSlot > 0 ? "skills-slot-reassign" : "skills-slot-assign"),
            new(footer.X, Layout.PrimaryAction.Y - 10, footer.Width, 18), 13, page.FeedbackError ? PlayerMenuTheme.Danger : PlayerMenuTheme.Muted);
        int slots = page.VisibleSlots; double slotWidth = (footer.Width - (slots - 1) * 4) / slots;
        for (int slot = 0; slot < slots; slot++)
            Button(ctx, "skill-slot:" + slot, "", new(footer.X + slot * (slotWidth + 4), footer.Y, slotWidth, footer.Height),
                page.Pending ? MenuControlState.Pending : !page.CanEquip ? MenuControlState.Disabled
                : skill.EquippedSlot == slot + 1 ? MenuControlState.Selected : MenuControlState.Default,
                (slot + 1).ToString(CultureInfo.CurrentCulture));
    }

    private void DrawHubDetails(Context ctx, List<(string Text, double Size)> lines)
    {
        var b = Layout.Inspector; PlayerMenuDrawing.DrawWell(ctx, b);
        double width = b.Width - 28;
        var measured = lines.Select(line => (line.Text, line.Size, Height: MeasureTextHeight(ctx, line.Text, width, line.Size))).ToArray();
        double height = measured.Sum(line => line.Height + 8) + 20;
        inspectorMaximumScroll = Math.Max(0, (int)Math.Ceiling(height - b.Height));
        int scroll = Math.Clamp(session.Current.InspectorScroll, 0, inspectorMaximumScroll);
        ctx.Save(); ctx.Rectangle(b.X + 2, b.Y + 2, b.Width - 4, b.Height - 4); ctx.Clip();
        double y = b.Y + 10 - scroll;
        foreach (var line in measured)
        { Text(ctx, line.Text, new(b.X + 14, y, width, line.Height), line.Size); y += line.Height + 8; }
        ctx.Restore();
    }

    private bool HandleStatsSkillsIntent(string target)
    {
        if (!StatsSkillsOpen) return false;
        if (target == "hub:details") { session.Update(session.Current with { InspectorOnly = !session.Current.InspectorOnly, InspectorScroll = 0 }); return true; }
        if (session.Page == PlayerMenuPage.Stats && target.StartsWith("stat:", StringComparison.Ordinal))
        {
            var parts = target.Split(':');
            if (parts.Length == 3 && int.TryParse(parts[2], out int delta)) hub!.Stats.Adjust(parts[1], delta);
            return true;
        }
        if (session.Page != PlayerMenuPage.Skills) return false;
        var page = hub!.Skills;
        if (target is "class:previous" or "class:next")
        {
            if (page.Classes.Length > 0) page.SelectClass((page.SelectedClassIndex + (target == "class:next" ? 1 : page.Classes.Length - 1)) % page.Classes.Length);
            session.Update(session.Current with { ScrollAnchor = 0, InspectorScroll = 0 }); return true;
        }
        if (target.StartsWith("skill:", StringComparison.Ordinal) && int.TryParse(target[6..], out int index))
        { page.SelectSkill(index); session.Update(session.Current with { InspectorOnly = true, InspectorScroll = 0 }); return true; }
        if (target.StartsWith("skill-slot:", StringComparison.Ordinal) && int.TryParse(target[11..], out int slot))
        { page.EquipSelected(slot); session.Update(session.Current with { InspectorScroll = 0 }); return true; }
        return false;
    }
    private void ScrollStatsSkills(int amount, double x, double y)
    {
        if (Layout.Inspector.Contains(x, y) && (!Layout.AlternatePanes || session.Current.InspectorOnly))
            session.Update(session.Current with { InspectorScroll = Math.Clamp(session.Current.InspectorScroll + amount * 32, 0, inspectorMaximumScroll) });
        else if (session.Page == PlayerMenuPage.Skills && Layout.Browser.Contains(x, y))
            session.Update(session.Current with { ScrollAnchor = Math.Clamp(session.Current.ScrollAnchor + amount, 0, Math.Max(0, hub!.Skills.Skills.Length - SkillRows)) });
    }
    private bool HandleSkillsKey(KeyEvent args)
    {
        if (!StatsSkillsOpen || session.Page != PlayerMenuPage.Skills || session.Current.InspectorOnly
            && (Layout.AlternatePanes || !focus.StartsWith("skill:", StringComparison.Ordinal))) return false;
        var page = hub!.Skills;
        int? selected = args.KeyCode switch
        {
            (int)GlKeys.Up => page.SelectedSkillIndex - 1,
            (int)GlKeys.Down => page.SelectedSkillIndex + 1,
            (int)GlKeys.PageUp => page.SelectedSkillIndex - SkillRows,
            (int)GlKeys.PageDown => page.SelectedSkillIndex + SkillRows,
            (int)GlKeys.Home => 0,
            (int)GlKeys.End => page.Skills.Length - 1,
            _ => null
        };
        if (selected == null || page.Skills.Length == 0) return false;
        page.SelectSkill(selected.Value);
        int start = Math.Clamp(session.Current.ScrollAnchor, Math.Max(0, page.SelectedSkillIndex - SkillRows + 1), page.SelectedSkillIndex);
        focus = "skill:" + page.SelectedSkillIndex;
        session.Update(session.Current with { ScrollAnchor = start, Focus = focus, InspectorScroll = 0 });
        args.Handled = true; Refresh(); return true;
    }
}
