using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Cairo;
using VRPG.Client.PlayerMenu.Components;
using VRPG.Client.PlayerMenu.Hub;
using VRPG.Client.PlayerMenu.Inventory;
using Vintagestory.API.Client;

namespace VRPG.Client.PlayerMenu.Shell;

public sealed record PlayerMenuTextInput(MenuRect Bounds, string Value, string PlaceholderKey);

public sealed partial class PlayerMenuShell
{
    private bool LibraryOpen => gallery == null && !PartyOpen && session.Page == PlayerMenuPage.Library && hub?.LibraryReady == true && hub.ErrorKey.Length == 0;
    private bool OptionsOpen => gallery == null && !PartyOpen && session.Page == PlayerMenuPage.Options && hub?.HasSnapshot == true && hub.ErrorKey.Length == 0;
    private MenuRect LibrarySearch => new(Layout.Browser.X + 12, Layout.Browser.Y + 42, Layout.Browser.Width - 24, 30);
    private MenuRect LibraryList => new(Layout.Browser.X + 8, Layout.Browser.Y + 80, Layout.Browser.Width - 16, Math.Max(0, Layout.Browser.Height - 84));
    private int LibraryRows => Math.Max(1, (int)((LibraryList.Height + 4) / 52));
    private int optionsLastVisible, optionsRowCount;
    public PlayerMenuTextInput? SelectedPageInput => PartyOpen || Layout.AlternatePanes && session.Current.InspectorOnly ? null
        : LibraryOpen ? new(LibrarySearch, hub!.Library.Query, "library-search")
        : (gallery != null || inventory != null && session.Page == PlayerMenuPage.Inventory)
            ? new(inventory == null ? Layout.BrowserSearch : Layout.InventorySearch, session.Current.Search, inventory == null ? "search" : "item-search") : null;

    public void SetSearchText(string value, double now)
    {
        if (SelectedPageInput == null) return;
        var input = new SearchField(); input.SetText(value ?? "");
        if (LibraryOpen) hub!.Library.SetQuery(input.Text);
        else inventory?.ChangeQuery(inventory.Query with { Search = input.Text, Offset = 0 }, now);
        session.Update(session.Current with { Search = input.Text, ScrollAnchor = 0, InspectorScroll = 0, Focus = "search" });
    }

    private static string LibraryText(string? text) => Regex.Replace(text ?? "", @"\b[a-z][a-z0-9_-]*:[a-zA-Z0-9_./-]+",
        match => PlayerItemDisplayNameResolver.HumanizeAssetPath(match.Value));
    private void DrawLibrary(Context ctx)
    {
        var page = hub!.Library; var b = Layout.Browser;
        if (!Layout.AlternatePanes || !session.Current.InspectorOnly)
        {
            PlayerMenuDrawing.DrawWell(ctx, b);
            Button(ctx, "library-category:previous", "", new(b.X + 8, b.Y + 6, 32, 30), page.Categories.Length > 1 ? MenuControlState.Default : MenuControlState.Disabled, "‹");
            Button(ctx, "library-category:next", "", new(b.Right - 40, b.Y + 6, 32, 30), page.Categories.Length > 1 ? MenuControlState.Default : MenuControlState.Disabled, "›");
            HubBrowseLabel(ctx, page.Category.Length == 0 ? T("library-all") : LibraryText(PlayerItemDisplayNameResolver.HumanizeAssetPath(page.Category)), new(b.X + 48, b.Y + 8, b.Width - 96, 24), 17);
            Button(ctx, "search", "", LibrarySearch);
            page.ScrollBy(0, LibraryRows);
            var clip = LibraryList;
            ctx.Save(); ctx.Rectangle(clip.X, clip.Y, clip.Width, clip.Height); ctx.Clip();
            for (int i = page.Scroll; i < Math.Min(page.VisibleEntries.Length, page.Scroll + LibraryRows); i++)
            {
                var entry = page.VisibleEntries[i]; var row = new MenuRect(clip.X, clip.Y + (i - page.Scroll) * 52, clip.Width, 48);
                if (row.Bottom > clip.Bottom) break;
                Button(ctx, "library-entry:" + i, "", row, page.SelectedEntry?.Code == entry.Code ? MenuControlState.Selected : MenuControlState.Default);
                HubBrowseLabel(ctx, LibraryText(LibraryPage.ReadableName(entry)), new(row.X + 8, row.Y + 2, row.Width - 16, 22), 15);
                HubBrowseLabel(ctx, LibraryText(entry.Summary), new(row.X + 8, row.Y + 25, row.Width - 16, 20), 13);
            }
            if (page.VisibleEntries.Length == 0) Text(ctx, T(page.Query.Trim().Length > 0 ? "library-no-match" : "library-empty"), clip, 16);
            ctx.Restore();
            if (Layout.AlternatePanes && page.SelectedEntry != null) Button(ctx, "library:details", "library-details", Layout.PrimaryAction);
        }
        if (!Layout.AlternatePanes || session.Current.InspectorOnly)
        {
            var lines = new List<(string Text, double Size)>();
            if (page.SelectedEntry is { } entry)
            {
                lines.Add((LibraryText(LibraryPage.ReadableName(entry)), 21)); lines.Add((LibraryText(entry.Summary), 16));
                foreach (var field in entry.Fields) lines.Add((LibraryText(field.Label) + ": " + LibraryText(field.Value), 15));
                if (entry.Tags.Length > 0) lines.Add((T("library-tags", string.Join(" · ", entry.Tags.Select(LibraryText))), 14));
                if (!string.IsNullOrWhiteSpace(entry.Source)) lines.Add((T("library-source", LibraryText(entry.Source)), 14));
            }
            else lines.Add((T(page.Query.Trim().Length > 0 ? "library-no-match" : "library-empty"), 18));
            DrawHubDetails(ctx, lines);
        }
    }

    private sealed record OptionRow(string Id, string Label, string Hint, string Value, Action Action,
        Action? Decrease = null, bool CanDecrease = true, bool CanIncrease = true);
    private OptionRow[] OptionRows()
    {
        var page = hub!.Options; var hud = hub.Actions.ResourceHud; var v = hub.Actions.CombatVisuals;
        string Toggle(bool value) => T(value ? "option-on" : "option-off");
        OptionRow ToggleRow(string id, bool value, Action action) => new(id, T("option-" + id), T("option-" + id + "-hint"), Toggle(value), action);
        return page.Category switch
        {
            OptionsCategory.Notifications => new[] { ToggleRow("cooldown", page.CooldownNotifications, page.ToggleCooldownNotifications), ToggleRow("resource", page.ResourceNotifications, page.ToggleResourceNotifications) },
            OptionsCategory.ActionBar => new[] { ToggleRow("hotbar-lock", page.HotbarLocked, page.ToggleHotbarLock),
                new OptionRow("slots", T("option-slots"), T("option-slots-hint"), page.VisibleSkillSlots.ToString(CultureInfo.CurrentCulture), () => page.ChangeSkillSlots(1), () => page.ChangeSkillSlots(-1), page.VisibleSkillSlots > 4, page.VisibleSkillSlots < 8),
                ToggleRow("auto-throw", page.AutoThrow, page.ToggleAutoThrow) },
            OptionsCategory.CombatHud => new[] { ToggleRow("hud-lock", hud.Locked, page.ToggleResourceHudLock),
                new OptionRow("hud-width", T("option-hud-width"), T("option-hud-width-hint"), hud.Width.ToString(CultureInfo.CurrentCulture), () => page.ChangeResourceWidth(1), () => page.ChangeResourceWidth(-1), page.CanResizeWidth(-1), page.CanResizeWidth(1)),
                new OptionRow("hud-height", T("option-hud-height"), T("option-hud-height-hint"), hud.BarHeight.ToString(CultureInfo.CurrentCulture), () => page.ChangeResourceHeight(1), () => page.ChangeResourceHeight(-1), page.CanResizeHeight(-1), page.CanResizeHeight(1)),
                new OptionRow("hud-reset", T("option-hud-reset"), T("option-hud-reset-hint"), T("option-reset"), page.ResetResourceHud) },
            _ => new[] { ToggleRow("combat-text", v.CombatTextEnabled, page.ToggleCombatText), ToggleRow("damage-numbers", v.DamageNumbers, page.ToggleDamageNumbers),
                ToggleRow("event-words", v.EventWords, page.ToggleEventWords), ToggleRow("merge", v.MergeNumbers, page.ToggleMergeNumbers),
                ToggleRow("auras", v.StatusAuras, page.ToggleStatusAuras), ToggleRow("own-effects", page.OwnEffectsFirst, page.ToggleDegradationPolicy),
                new OptionRow("opacity", T("option-opacity"), T("option-opacity-hint"), T("option-percent", Math.Round(v.TelegraphOpacity * 100)), page.CycleTelegraphOpacity),
                new OptionRow("intensity", T("option-intensity"), T("option-intensity-hint"), T("option-percent", Math.Round(v.Intensity * 100)), page.CycleIntensity) }
        };
    }
    private static string OptionCategoryLabel(OptionsCategory category) => T("options-category-" + (int)category);
    private void DrawOptions(Context ctx)
    {
        var page = hub!.Options; var b = Layout.Inspector;
        PlayerMenuDrawing.DrawWell(ctx, b);
        if (!Layout.AlternatePanes)
        {
            var rail = Layout.Browser; PlayerMenuDrawing.DrawWell(ctx, rail);
            foreach (var category in Enum.GetValues<OptionsCategory>())
                Button(ctx, "options-category:" + (int)category, "", new(rail.X + 12, rail.Y + 12 + (int)category * 48, rail.Width - 24, 40),
                    page.Category == category ? MenuControlState.Selected : MenuControlState.Default, OptionCategoryLabel(category));
        }
        Button(ctx, "options-category:previous", "", new(b.X + 8, b.Y + 6, 32, 30), MenuControlState.Default, "‹");
        Button(ctx, "options-category:next", "", new(b.Right - 40, b.Y + 6, 32, 30), MenuControlState.Default, "›");
        HubBrowseLabel(ctx, OptionCategoryLabel(page.Category), new(b.X + 48, b.Y + 8, b.Width - 96, 26), 17);
        var rows = OptionRows(); optionsRowCount = rows.Length;
        int start = Math.Clamp(session.Current.ScrollAnchor, 0, rows.Length - 1);
        session.Update(session.Current with { ScrollAnchor = start });
        double y = b.Y + 42; optionsLastVisible = start;
        for (int i = start; i < rows.Length; i++)
        {
            var row = rows[i]; double labelWidth = b.Width - 144;
            double titleHeight = Math.Max(30, MeasureTextHeight(ctx, row.Label, labelWidth, 15));
            double hintHeight = MeasureTextHeight(ctx, row.Hint, b.Width - 40, 13);
            double height = titleHeight + hintHeight + 14;
            if (y + height > b.Bottom - 6) break;
            PlayerMenuDrawing.DrawPlate(ctx, new(b.X + 8, y, b.Width - 16, height));
            Text(ctx, row.Label, new(b.X + 16, y + 4, labelWidth, titleHeight), 15);
            Text(ctx, row.Hint, new(b.X + 16, y + titleHeight + 6, b.Width - 40, hintHeight), 13, PlayerMenuTheme.Muted);
            var control = new MenuRect(b.Right - 112, y + 3, 96, 30);
            if (row.Decrease == null) Button(ctx, "option:" + row.Id, "", control, hub.Loading ? MenuControlState.Disabled : MenuControlState.Default, row.Value);
            else
            {
                Button(ctx, "option:" + row.Id + ":down", "", control with { Width = 28 }, !hub.Loading && row.CanDecrease ? MenuControlState.Default : MenuControlState.Disabled, "−");
                Text(ctx, row.Value, control with { X = control.X + 33, Width = 34 }, 14);
                Button(ctx, "option:" + row.Id, "", control with { X = control.Right - 28, Width = 28 }, !hub.Loading && row.CanIncrease ? MenuControlState.Default : MenuControlState.Disabled, "+");
            }
            optionsLastVisible = i + 1; y += height + 6;
        }
        var footer = Layout.PrimaryAction;
        Button(ctx, "options:previous", "", footer with { Width = 34 }, start > 0 ? MenuControlState.Default : MenuControlState.Disabled, "‹");
        Button(ctx, "options:next", "", footer with { X = footer.Right - 34, Width = 34 }, optionsLastVisible < rows.Length ? MenuControlState.Default : MenuControlState.Disabled, "›");
        Text(ctx, page.Error.Length > 0 ? page.Error : T("options-range", start + 1, optionsLastVisible, rows.Length),
            footer with { X = footer.X + 42, Width = footer.Width - 84 }, 13, page.Error.Length > 0 ? PlayerMenuTheme.Danger : PlayerMenuTheme.Muted);
    }

    private bool HandleLibraryOptionsIntent(string target)
    {
        if (LibraryOpen)
        {
            var page = hub!.Library;
            if (target.StartsWith("library-category:", StringComparison.Ordinal))
            {
                int index = Array.IndexOf(page.Categories, page.Category);
                page.SelectCategory(page.Categories[(index + (target.EndsWith("next", StringComparison.Ordinal) ? 1 : page.Categories.Length - 1)) % page.Categories.Length]);
                session.Update(session.Current with { InspectorScroll = 0 }); return true;
            }
            if (target.StartsWith("library-entry:", StringComparison.Ordinal) && int.TryParse(target[14..], out int entry))
            {
                if (entry >= 0 && entry < page.VisibleEntries.Length) page.SelectEntry(page.VisibleEntries[entry].Code);
                session.Update(session.Current with { InspectorOnly = true, InspectorScroll = 0 });
                // The dialog must remove the native search control when the narrow view becomes details.
                intent("library:compose"); return true;
            }
            if (target == "library:details") { session.Update(session.Current with { InspectorOnly = true }); intent("library:compose"); return true; }
        }
        if (!OptionsOpen) return false;
        if (target.StartsWith("options-category:", StringComparison.Ordinal))
        {
            string value = target[17..]; int next = int.TryParse(value, out int category) ? category : ((int)hub!.Options.Category + (value == "next" ? 1 : 3)) % 4;
            hub!.Options.SelectCategory((OptionsCategory)next); session.Update(session.Current with { ScrollAnchor = 0 }); return true;
        }
        if (target is "options:previous" or "options:next")
        { ScrollOptions(target == "options:next" ? Math.Max(1, optionsLastVisible - session.Current.ScrollAnchor) : -1); return true; }
        if (target.StartsWith("option:", StringComparison.Ordinal))
        {
            bool down = target.EndsWith(":down", StringComparison.Ordinal); string id = down ? target[7..^5] : target[7..];
            var row = OptionRows().FirstOrDefault(r => r.Id == id);
            if (row != null) { if (down) row.Decrease?.Invoke(); else row.Action(); }
            return true;
        }
        return false;
    }
    private void ScrollOptions(int amount) => session.Update(session.Current with { ScrollAnchor = Math.Clamp(session.Current.ScrollAnchor + amount, 0, Math.Max(0, optionsRowCount - 1)) });
    private bool HandleLibraryOptionsKey(KeyEvent args)
    {
        if (OptionsOpen && args.KeyCode is (int)GlKeys.PageDown or (int)GlKeys.PageUp)
        { ScrollOptions(args.KeyCode == (int)GlKeys.PageDown ? Math.Max(1, optionsLastVisible - session.Current.ScrollAnchor) : -1); args.Handled = true; Refresh(); return true; }
        if (!LibraryOpen || session.Current.InspectorOnly && (Layout.AlternatePanes || !focus.StartsWith("library-entry:", StringComparison.Ordinal))) return false;
        var page = hub!.Library; int current = Array.FindIndex(page.VisibleEntries, e => e.Code == page.SelectedEntry?.Code);
        int? next = args.KeyCode switch { (int)GlKeys.Up => current - 1, (int)GlKeys.Down => current + 1, (int)GlKeys.PageDown => current + LibraryRows,
            (int)GlKeys.PageUp => current - LibraryRows, (int)GlKeys.Home => 0, (int)GlKeys.End => page.VisibleEntries.Length - 1, _ => null };
        if (next == null || page.VisibleEntries.Length == 0) return false;
        int index = Math.Clamp(next.Value, 0, page.VisibleEntries.Length - 1); page.SelectEntry(page.VisibleEntries[index].Code);
        page.ScrollBy(index < page.Scroll ? index - page.Scroll : index >= page.Scroll + LibraryRows ? index - page.Scroll - LibraryRows + 1 : 0, LibraryRows);
        focus = "library-entry:" + index; session.Update(session.Current with { Focus = focus, InspectorScroll = 0 }); args.Handled = true; Refresh(); return true;
    }
}
