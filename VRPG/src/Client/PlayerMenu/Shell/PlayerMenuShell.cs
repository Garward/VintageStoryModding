using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Cairo;
using VRPG.Client.PlayerMenu.Components;
using VRPG.Client.UI;
using VRPG.Inventory;
using VRPG.Client.PlayerMenu.Inventory;
using VRPG.Network;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace VRPG.Client.PlayerMenu.Shell;

/// <summary>Native cached composition host. Pages issue named intents; rendering owns no gameplay state.</summary>
public sealed class PlayerMenuShell : GuiElement
{
    private readonly PlayerMenuSessionState session;
    private readonly PlayerMenuGallery? gallery;
    private readonly InventoryPage? inventory;
    private readonly Action<string> intent;
    private readonly BoundedItemStackRenderer itemRenderer;
    private readonly Dictionary<string, ItemSlot> icons = new();
    private readonly List<(string Id, MenuRect Bounds, FittedButton Button)> controls = new();
    private readonly List<(ItemSlot Slot, MenuRect Bounds)> visibleIcons = new();
    private int textureId;
    private string hover = "", focus = "", pressed = "";
    private int inspectorMaximumScroll;
    public PlayerMenuLayout Layout { get; }
    public override bool Focusable => true;
    public PlayerMenuShell(ICoreClientAPI api, ElementBounds bounds, PlayerMenuLayout layout,
        PlayerMenuSessionState session, PlayerMenuGallery? gallery, Action<string> intent, InventoryPage? inventory = null) : base(api, bounds)
    {
        Layout = layout; this.session = session; this.gallery = gallery; this.intent = intent;
        this.inventory = inventory;
        itemRenderer = new(api, bounds);
        focus = session.Current.Focus;
    }
    public static string T(string key, params object[] args) => Lang.Get("vrpg:menu-" + key, args);
    public override void ComposeElements(Context ctxStatic, ImageSurface surfaceStatic) { Bounds.CalcWorldBounds(); Refresh(); }
    public override void RenderInteractiveElements(float deltaTime)
    {
        if (gallery?.Equipment.Advance(deltaTime) == true) Refresh();
        if (textureId > 0) api.Render.Render2DTexturePremultipliedAlpha(textureId, Bounds);
        foreach (var icon in visibleIcons)
            itemRenderer.Render(icon.Slot, icon.Bounds.X, icon.Bounds.Y, icon.Bounds.Width, RuntimeEnv.GUIScale, 100, deltaTime, false);
    }
    public void Refresh()
    {
        if (Bounds.OuterWidthInt <= 0 || Bounds.OuterHeightInt <= 0) return;
        controls.Clear(); visibleIcons.Clear();
        using var surface = new ImageSurface((Format)0, Bounds.OuterWidthInt, Bounds.OuterHeightInt);
        using var ctx = genContext(surface);
        ctx.Scale(RuntimeEnv.GUIScale, RuntimeEnv.GUIScale);
        Draw(ctx); generateTexture(surface, ref textureId);
    }
    private void Draw(Context ctx)
    {
        Fill(ctx, new(0, 0, Layout.Width, Layout.Height), PlayerMenuTheme.Iron900);
        Stroke(ctx, new(2, 2, Layout.Width - 4, Layout.Height - 4), PlayerMenuTheme.Iron700, 3);
        Button(ctx, "back", "back", Layout.Back);
        Button(ctx, "close", "close", Layout.Close);
        Text(ctx, T(gallery != null ? "gallery-title" : inventory?.View.Error.Length > 0 ? "browse-error-short" : inventory?.View.Loading == true ? "loading" : "browse-title"),
            new(122, 12, Math.Max(0, Layout.Width - 244), 30), Layout.Width < 800 ? 13 : 16);
        if (inventory != null && Layout.AlternatePanes)
            Button(ctx, "inspect", inventory.View.Error.Length > 0 ? "browse-error-short" : "browse-options", new(122, 12, Math.Max(0, Layout.Width - 244), 30));
        int columns = Layout.AlternatePanes ? 3 : 6;
        double tabWidth = (Layout.Width - 48) / columns;
        foreach (var page in Enum.GetValues<PlayerMenuPage>())
        {
            int i = (int)page;
            Button(ctx, "page:" + page, page.ToString().ToLowerInvariant(), new(24 + i % columns * tabWidth, 52 + i / columns * 38, tabWidth - 4, 34),
                session.Page == page ? MenuControlState.Selected : MenuControlState.Default);
        }
        string inventoryBinding = api.Input.GetHotKeyByCode(VanillaMenuActions.Inventory)?.CurrentMapping?.ToString() ?? "";
        string characterBinding = api.Input.GetHotKeyByCode(VanillaMenuActions.Character)?.CurrentMapping?.ToString() ?? "";
        if (!Layout.AlternatePanes && inventoryBinding.Length > 0 && characterBinding.Length > 0)
            Text(ctx, T("bindings", inventoryBinding, characterBinding), new(24, 94, Layout.Width - 48, 24), 13, PlayerMenuTheme.Muted);
        bool inspect = !Layout.AlternatePanes || session.Current.InspectorOnly;
        bool browse = !Layout.AlternatePanes || !session.Current.InspectorOnly;
        if (inventory != null)
        {
            if (session.Page != PlayerMenuPage.Inventory)
            { Text(ctx, T("page-unavailable"), Layout.Browser, 18); return; }
            if (browse) DrawInventoryBrowser(ctx);
            if (inspect) DrawInventoryInspector(ctx);
            return;
        }
        if (browse) DrawBrowser(ctx);
        if (inspect) DrawInspector(ctx);
        if (inspect) Button(ctx, "action", "gallery-action", Layout.PrimaryAction, gallery!.State);
        else
        {
            double third = (Layout.PrimaryAction.Width - 8) / 3;
            Button(ctx, "state", "gallery-state", Layout.PrimaryAction with { Width = third });
            Button(ctx, "labels", "gallery-labels", Layout.PrimaryAction with { X = Layout.PrimaryAction.X + third + 4, Width = third });
            Button(ctx, "motion", session.ReducedMotion ? "motion-reduced" : "motion-normal",
                Layout.PrimaryAction with { X = Layout.PrimaryAction.X + 2 * third + 8, Width = third });
        }
    }
    private void DrawBrowser(Context ctx)
    {
        var bounds = Layout.Browser;
        Fill(ctx, bounds, PlayerMenuTheme.Iron950);
        if (Layout.CondensedBrowserChrome)
        {
            double half = (bounds.Width - 28) / 2;
            Button(ctx, "category-next", "category-" + session.Current.Category, new(bounds.X + 12, bounds.Y + 2, half, 34), MenuControlState.Selected);
            Button(ctx, "count", "gallery-count", new(bounds.X + 16 + half, bounds.Y + 2, half, 34));
        }
        else
        {
            double tabWidth = (bounds.Width - 24) / 3;
            var categories = TypedTabStrip.Categories;
            for (int i = 0; i < categories.Length; i++)
                Button(ctx, "category:" + categories[i], "category-" + categories[i],
                    new(bounds.X + 12 + i % 3 * tabWidth, bounds.Y + 10 + i / 3 * 38, tabWidth - 4, 34),
                    session.Current.Category == categories[i] ? MenuControlState.Selected : MenuControlState.Default);
            Button(ctx, "count", "gallery-count", new(bounds.X + 12 + 2 * tabWidth, bounds.Y + 48, tabWidth - 4, 34));
        }
        controls.Add(("search", Layout.BrowserSearch, new("search", "search", MenuControlState.Default)));
        var entries = FilteredEntries();
        var clip = Layout.BrowserContent;
        ctx.Save(); ctx.Rectangle(clip.X, clip.Y, clip.Width, clip.Height); ctx.Clip();
        if (entries.Length == 0) Text(ctx, T(gallery!.Entries(session.Current.Category).Count == 0 ? "empty-category" : "empty-search"), clip with { Height = 90 }, 18);
        int start = Math.Min(session.Current.ScrollAnchor, Math.Max(0, entries.Length - Layout.VisibleRows));
        foreach (var pair in entries.Skip(start).Take(Layout.RenderRowLimit).Select((entry, index) => (entry, index)))
        {
            var row = Layout.BrowserRow(pair.index);
            bool selected = session.Current.SelectedEntry == pair.entry.Id;
            Button(ctx, "entry:" + pair.entry.Id, "", row, selected ? MenuControlState.Selected : MenuControlState.Default);
            Text(ctx, T(pair.entry.NameKey) + " " + (start + pair.index + 1), new(row.X + 50, row.Y + 4, row.Width - 60, 22), 15);
            Text(ctx, pair.entry.Quantity.ToString("N0", CultureInfo.CurrentCulture), new(row.X + 50, row.Y + 26, row.Width - 60, 20), 13, PlayerMenuTheme.Muted);
            if (row.Y >= clip.Y && row.Bottom <= clip.Bottom && ResolveIcon(pair.entry.AssetCode) is { } slot)
                visibleIcons.Add((slot, new(row.X + 4, row.Y + 4, 40, 40)));
        }
        ctx.Restore();
        // Partially clipped overscan rows are never clickable outside the content well.
        controls.RemoveAll(x => x.Id.StartsWith("entry:", StringComparison.Ordinal) && (x.Bounds.Y < clip.Y || x.Bounds.Bottom > clip.Bottom));
        if (!Layout.AlternatePanes)
        {
            double third = (bounds.Width - 8) / 3;
            Button(ctx, "state", "gallery-state", new(bounds.X, Layout.PrimaryAction.Y, third, 38));
            Button(ctx, "labels", "gallery-labels", new(bounds.X + third + 4, Layout.PrimaryAction.Y, third, 38));
            Button(ctx, "motion", session.ReducedMotion ? "motion-reduced" : "motion-normal", new(bounds.X + 2 * third + 8, Layout.PrimaryAction.Y, third, 38));
        }
    }
    private void DrawInspector(Context ctx)
    {
        var b = Layout.Inspector;
        Fill(ctx, b, PlayerMenuTheme.Iron950);
        ctx.Save(); ctx.Rectangle(b.X + 2, b.Y + 2, b.Width - 4, b.Height - 4); ctx.Clip();
        var entry = FilteredEntries().FirstOrDefault(x => x.Id == session.Current.SelectedEntry);
        double x = b.X + 20, y = b.Y + 16 - session.Current.InspectorScroll, width = b.Width - 40;
        string name = T(entry?.NameKey ?? "gallery-select");
        double nameHeight = MeasureTextHeight(ctx, name, width, 21);
        Text(ctx, name, new(x, y, width, nameHeight), 21);
        y += nameHeight + 20;
        if (entry != null && session.Current.Category == PlayerItemCategories.Gear)
        {
            Text(ctx, T("gallery-effects"), new(x, y, width, 70), 17); y += 78;
            Text(ctx, T("gallery-comparison"), new(x, y, width, 50), 15, PlayerMenuTheme.Muted); y += 58;
        }
        else if (entry != null)
        {
            Text(ctx, entry.Quantity.ToString("N0", CultureInfo.CurrentCulture), new(x, y, width, 52), 20); y += 64;
        }
        if (session.Page == PlayerMenuPage.Inventory && session.Current.Category == PlayerItemCategories.Gear)
        {
            Button(ctx, "equipment", session.Current.EquipmentExpanded ? "equipment-open" : "equipment", new(x, y, width, 36)); y += 44;
            if (gallery!.Equipment.VisibleFraction > 0)
            {
                double reveal = 50 * gallery.Equipment.VisibleFraction;
                ctx.Save(); ctx.Rectangle(x, y, width, reveal); ctx.Clip();
                Text(ctx, T("gallery-equipment"), new(x, y - 50 + reveal, width, 46), 15); ctx.Restore();
                y += reveal;
            }
        }
        double feedbackHeight = MeasureTextHeight(ctx, T(gallery!.FeedbackKey), width, 14);
        Text(ctx, T(gallery.FeedbackKey), new(x, y + 8, width, feedbackHeight), 14,
            gallery.State == MenuControlState.Rejected ? PlayerMenuTheme.Danger : PlayerMenuTheme.Muted);
        inspectorMaximumScroll = Math.Max(0, (int)(y + feedbackHeight + 24 + session.Current.InspectorScroll - b.Bottom));
        ctx.Restore();
        controls.RemoveAll(control => control.Id == "equipment" && (control.Bounds.Y < b.Y || control.Bounds.Bottom > b.Bottom));
    }
    public GalleryEntry[] FilteredEntries() => gallery!.Entries(session.Current.Category)
        .Where(x => T(x.NameKey).Contains(session.Current.Search, StringComparison.CurrentCultureIgnoreCase)).ToArray();
    private ItemSlot? ResolveIcon(string code)
    {
        if (icons.TryGetValue(code, out var cached)) return cached;
        var item = api.World.GetItem(new AssetLocation(code));
        if (item == null) return null;
        var slot = new DummySlot(new ItemStack(item)); icons.Add(code, slot); return slot;
    }
    private void Button(Context ctx, string id, string key, MenuRect bounds, MenuControlState state = MenuControlState.Default)
    {
        var button = new FittedButton(id, key, state);
        controls.Add((id, bounds, button));
        var visual = pressed == id && button.CanActivate ? MenuControlState.Pressed : hover == id && button.CanActivate ? MenuControlState.Hover : state;
        double offset = new FittedButton(id, key, visual).PressOffset(session.ReducedMotion);
        Fill(ctx, bounds with { Y = bounds.Y + offset }, visual == MenuControlState.Pressed ? PlayerMenuTheme.Iron950 : PlayerMenuTheme.Iron800);
        Stroke(ctx, bounds, visual == MenuControlState.Hover ? PlayerMenuTheme.CopperWorn : PlayerMenuTheme.CopperDull, .7);
        if (state is MenuControlState.Selected or MenuControlState.Pending)
            Fill(ctx, new(bounds.X + 3, bounds.Y + 4, 3, bounds.Height - 8), PlayerMenuTheme.Heat);
        if (focus == id || state == MenuControlState.Focused) Stroke(ctx, new(bounds.X + 3, bounds.Y + 3, bounds.Width - 6, bounds.Height - 6), PlayerMenuTheme.HeatHot, 2);
        string label = key.Length == 0 ? "" : key == "gallery-count" ? T(key, gallery!.Entries(session.Current.Category).Count,
            session.Current.Category == PlayerItemCategories.Gear ? 120 : 512) : T(key);
        if (key.Length > 0) Text(ctx, label, new(bounds.X + 10, bounds.Y + 5 + offset, bounds.Width - 20, bounds.Height - 8), 14,
            button.CanActivate ? PlayerMenuTheme.Text : PlayerMenuTheme.Muted);
    }
    private static void Color(Context ctx, string hex) => ctx.SetSourceRGB(Convert.ToInt32(hex[..2], 16) / 255d, Convert.ToInt32(hex[2..4], 16) / 255d, Convert.ToInt32(hex[4..], 16) / 255d);
    private static void Fill(Context ctx, MenuRect b, string color) { Color(ctx, color); ctx.Rectangle(b.X, b.Y, b.Width, b.Height); ctx.Fill(); }
    private static void Stroke(Context ctx, MenuRect b, string color, double line) { Color(ctx, color); ctx.LineWidth = line; ctx.Rectangle(b.X, b.Y, b.Width, b.Height); ctx.Stroke(); }
    private static void Text(Context ctx, string text, MenuRect b, double size, string color = PlayerMenuTheme.Text)
    {
        if (b.Width <= 0 || b.Height <= 0) return;
        ctx.Save(); ctx.Rectangle(b.X, b.Y, b.Width, b.Height); ctx.Clip();
        ctx.SelectFontFace(PlayerMenuTheme.Font, FontSlant.Normal, FontWeight.Normal); ctx.SetFontSize(size); Color(ctx, color);
        double y = b.Y + size;
        foreach (string line in PlayerMenuTextLayout.Wrap(text, b.Width, value => ctx.TextExtents(value).Width))
        { ctx.MoveTo(b.X, y); ctx.ShowText(line); y += size + 5; }
        ctx.Restore();
    }
    private static double MeasureTextHeight(Context ctx, string text, double width, double size)
    {
        ctx.SelectFontFace(PlayerMenuTheme.Font, FontSlant.Normal, FontWeight.Normal); ctx.SetFontSize(size);
        return PlayerMenuTextLayout.Wrap(text, width, value => ctx.TextExtents(value).Width).Count * (size + 5);
    }
    private void DrawInventoryBrowser(Context ctx)
    {
        var page = inventory!; var view = page.View; var b = Layout.Browser;
        Fill(ctx, b, PlayerMenuTheme.Iron950);
        double inner = b.Width - 24, third = (inner - 8) / 3;
        double top = b.Y + (Layout.CondensedBrowserChrome ? 2 : 10);
        string category = page.Query.Category.Length == 0 ? T("all-types") : PlayerItemCategories.Required.Contains(page.Query.Category)
            ? T("category-" + page.Query.Category) : view.Categories.FirstOrDefault(x => x.Code == page.Query.Category)?.DisplayName ?? T("other-type");
        if (Layout.CondensedBrowserChrome)
        {
            Button(ctx, "category-next", "", new(b.X + 12, top, inner * .55 - 4, 34), MenuControlState.Selected);
            Text(ctx, category, new(b.X + 22, top + 5, inner * .55 - 24, 26), 14);
            Button(ctx, "location", "location-" + page.Query.Location.ToString().ToLowerInvariant(), new(b.X + 12 + inner * .55, top, inner * .45, 34));
        }
        else
        {
            var categories = new[] { "" }.Concat(PlayerItemCategories.Required).ToArray();
            for (int i = 0; i < categories.Length; i++) Button(ctx, "category:" + categories[i], categories[i].Length == 0 ? "all-types" : "category-" + categories[i],
                new(b.X + 12 + i % 3 * (third + 4), b.Y + 10 + i / 3 * 38, third, 34), page.Query.Category == categories[i] ? MenuControlState.Selected : MenuControlState.Default);
            Button(ctx, "location", "location-" + page.Query.Location.ToString().ToLowerInvariant(), new(b.X + 12, b.Y + 86, third, 44));
            Button(ctx, "filter", "filter-" + page.Query.Filters.ToString().ToLowerInvariant(), new(b.X + 16 + third, b.Y + 86, third, 44));
            Button(ctx, "sort", "sort-" + page.Query.Sort.ToString().ToLowerInvariant(), new(b.X + 20 + third * 2, b.Y + 86, third, 44));
        }
        controls.Add(("search", Layout.InventorySearch, new("search", "search", MenuControlState.Default)));
        var clip = Layout.InventoryContent;
        if (!Layout.CondensedBrowserChrome)
            Text(ctx, view.Capacity == null ? T("loading") : T("capacity", view.Capacity.UniqueEntriesUsed, view.Capacity.UniqueEntryLimit,
                view.Capacity.AggregateEntriesUsed, view.Capacity.AggregateEntryLimit, view.RecoveryCount), new(b.X + 12, b.Y + 162, inner, 17), 12);
        ctx.Save(); ctx.Rectangle(clip.X, clip.Y, clip.Width, clip.Height); ctx.Clip();
        bool grid = view.Grid && page.Query.Category == PlayerItemCategories.Gear;
        int columns = grid ? 2 : 1;
        int start = Math.Clamp(session.Current.ScrollAnchor, 0, Math.Max(0, (view.Entries.Count + columns - 1) / columns - Layout.InventoryVisibleRows));
        int index = 0;
        foreach (var entry in InventoryItemGrid.Visible(view.Entries, start, Layout.InventoryVisibleRows, grid))
        {
            var row = Layout.InventoryRow(index / columns);
            if (grid) row = row with { X = row.X + index % 2 * (row.Width + 4) / 2, Width = (row.Width - 4) / 2 };
            index++;
            Button(ctx, "entry:" + entry.EntryId, "", row, view.Stale || view.Loading ? MenuControlState.Disabled
                : entry.EntryId == view.SelectedId ? MenuControlState.Selected : MenuControlState.Default);
            Text(ctx, entry.DisplayName, new(row.X + 48, row.Y + 3, row.Width - 54, 23), 15);
            string marker = view.Selection.Amounts.ContainsKey(entry.EntryId) ? T("selected-marker") + " " : "";
            string quantity = entry.Kind == PlayerItemStorageKind.Ledger ? T("balance", InventoryItemRow.QuantityOverlay(entry.Quantity))
                : entry.Kind == PlayerItemStorageKind.Protected ? T("protected-record") : InventoryItemRow.QuantityOverlay(entry.Quantity);
            string status = entry.Location == PlayerItemLocation.Unresolved ? T("unavailable")
                : entry.Flags.HasFlag(PlayerItemFlags.Protected) ? T("protected-record") : entry.Flags.HasFlag(PlayerItemFlags.Favorite) ? T("favorite") : "";
            string type = page.Query.Category.Length == 0 ? PlayerItemCategories.Required.Contains(entry.Category) ? T("category-" + entry.Category)
                : entry.CategoryName.Length > 0 ? entry.CategoryName : T("other-type") : "";
            Text(ctx, marker + quantity + "  " + status + "  " + type, new(row.X + 48, row.Y + 27, row.Width - 54, 18), 12, PlayerMenuTheme.Muted);
            if (entry.Location != PlayerItemLocation.Unresolved && row.Y >= clip.Y && row.Bottom <= clip.Bottom && ResolveInventoryIcon(entry) is { } slot)
                visibleIcons.Add((slot, new(row.X + 4, row.Y + 4, 40, 40)));
        }
        if (view.Entries.Count == 0) Text(ctx, T(view.Loading ? "loading" : view.Error.Length > 0 ? "browse-error" : page.Query.Search.Length > 0 ? "no-results" : "no-items"), clip with { Height = 110 }, 17);
        ctx.Restore();
        controls.RemoveAll(x => x.Id.StartsWith("entry:", StringComparison.Ordinal) && (x.Bounds.Y < clip.Y || x.Bounds.Bottom > clip.Bottom));
        double footerWidth = (b.Width - 8) / 3;
        Button(ctx, "previous-page", "previous", new(b.X, Layout.PrimaryAction.Y, footerWidth, 38), !view.Loading && page.Query.Offset > 0 ? MenuControlState.Default : MenuControlState.Disabled);
        Button(ctx, "next-page", "next", new(b.X + footerWidth + 4, Layout.PrimaryAction.Y, footerWidth, 38), !view.Loading && page.Query.Offset + 64 < view.Total ? MenuControlState.Default : MenuControlState.Disabled);
        Button(ctx, "refresh", "refresh", new(b.X + 2 * (footerWidth + 4), Layout.PrimaryAction.Y, footerWidth, 38), view.Loading ? MenuControlState.Pending : MenuControlState.Default);
    }
    private ItemSlot? ResolveInventoryIcon(PlayerItemEntryPacket entry)
    {
        string key = entry.AssetKind + ":" + entry.AssetCode;
        if (icons.TryGetValue(key, out var slot)) return slot;
        var asset = new AssetLocation(entry.AssetCode);
        ItemStack? stack = entry.AssetKind == PlayerItemAssetKind.Block ? api.World.GetBlock(asset) is { } block ? new ItemStack(block) : null
            : api.World.GetItem(asset) is { } item ? new ItemStack(item) : null;
        if (stack == null) return null;
        slot = new DummySlot(stack); icons[key] = slot; return slot;
    }
    private void DrawInventoryInspector(Context ctx)
    {
        int firstInspectorControl = controls.Count;
        var page = inventory!; var view = page.View; var b = Layout.Inspector;
        Fill(ctx, b, PlayerMenuTheme.Iron950);
        ctx.Save(); ctx.Rectangle(b.X + 2, b.Y + 2, b.Width - 4, b.Height - 4); ctx.Clip();
        double x = b.X + 18, y = b.Y + 12 - session.Current.InspectorScroll, width = b.Width - 36;
        void Line(string text, double size = 15, string color = PlayerMenuTheme.Text)
        { double height = MeasureTextHeight(ctx, text, width, size); Text(ctx, text, new(x, y, width, height), size, color); y += height + 8; }
        Line(T("selection-count", view.Selection.Amounts.Count, view.Selection.HiddenCount(view.Entries)), 14, PlayerMenuTheme.Muted);
        Button(ctx, "select-page", "select-page", new(x, y, (width - 4) / 2, 34), view.Loading || view.Stale || view.Entries.Count == 0 ? MenuControlState.Disabled : MenuControlState.Default);
        Button(ctx, "clear-selection", "clear-selection", new(x + (width + 4) / 2, y, (width - 4) / 2, 34), view.Selection.Amounts.Count > 0 ? MenuControlState.Default : MenuControlState.Disabled); y += 44;
        if (page.Query.Category == PlayerItemCategories.Gear)
        { Button(ctx, "view", view.Grid ? "list-view" : "grid-view", new(x, y, width, 34)); y += 44; }
        if (view.CategoryTotal > 0)
        {
            Button(ctx, "addon-type", "addon-types", new(x, y, (width - 4) / 2, 34));
            Button(ctx, "more-types", "more-types", new(x + (width + 4) / 2, y, (width - 4) / 2, 34)); y += 44;
            if (!PlayerItemCategories.Required.Contains(page.Query.Category) && page.Query.Category.Length > 0)
                Line(view.Categories.FirstOrDefault(c => c.Code == page.Query.Category)?.DisplayName ?? T("other-type"));
        }
        if (view.Error.Length > 0) Line(T("browse-error"), 15, PlayerMenuTheme.Danger);
        else if (view.Stale) Line(T("stale"), 14, PlayerMenuTheme.Muted);
        if (view.Selected is not { } entry) Line(T("select-item"), 20);
        else
        {
            Line(entry.DisplayName, 21);
            Line(PlayerItemCategories.Required.Contains(entry.Category) ? T("category-" + entry.Category) : entry.CategoryName, 14, PlayerMenuTheme.Muted);
            if (entry.Kind == PlayerItemStorageKind.Protected) Line(T("protected-record"));
            Line(T(entry.Kind == PlayerItemStorageKind.Ledger ? "balance" : "quantity", InventoryItemRow.ExactQuantity(entry.Quantity)), 18);
            if (entry.ReservedQuantity > 0) Line(T("reserved", InventoryItemRow.ExactQuantity(entry.ReservedQuantity)));
            if (entry.Flags.HasFlag(PlayerItemFlags.Favorite)) Line(T("favorite"));
            if (entry.Flags.HasFlag(PlayerItemFlags.Protected)) Line(T(entry.ProtectionSource.HasFlag(PlayerItemProtectionSource.System) ? "system-protected" : "user-protected"));
            if (RecoveryView.IsRecovery(entry.Location)) Line(T(RecoveryView.ReasonKey(entry)), 15, PlayerMenuTheme.Muted);
            if (view.DetailLoading) Line(T("detail-loading"));
            if (view.DetailError.Length > 0)
            {
                Line(T(view.DetailError == "invalid-quantity" ? "invalid-quantity" : view.DetailError == "requirement-not-met" ? "requirement-not-met" : "detail-error"), 15, PlayerMenuTheme.Danger);
                Button(ctx, "retry-detail", "retry", new(x, y, width, 34)); y += 44;
            }
            if (view.Detail is { } detail)
            {
                if (detail.RequiredLevel > 0) Line(T("required-level", detail.RequiredLevel));
                if (detail.Effects.Length > 0) Line(T("effects"), 18);
                foreach (var effect in detail.Effects)
                {
                    var display = InventoryItemRow.DisplayValue(effect.Value, effect.UnitCode);
                    Line(T(effect.Operation == "resolved" ? "effect-total" : "effect-value", StatName(effect.StatCode), display.Value.ToString("0.####", CultureInfo.CurrentCulture),
                        T("unit-" + display.Unit), T("operation-" + effect.Operation)));
                }
                if (detail.Comparison.Length > 0) Line(T("comparison"), 18);
                foreach (var comparison in detail.Comparison)
                {
                    var before = InventoryItemRow.DisplayValue(comparison.Before, comparison.UnitCode);
                    var after = InventoryItemRow.DisplayValue(comparison.After, comparison.UnitCode);
                    var difference = InventoryItemRow.DisplayValue(comparison.Difference, comparison.UnitCode);
                    Line(T("comparison-value", StatName(comparison.StatCode), before.Value.ToString("0.##", CultureInfo.CurrentCulture),
                        after.Value.ToString("0.##", CultureInfo.CurrentCulture), difference.Value.ToString("+0.##;-0.##;0", CultureInfo.CurrentCulture), T("unit-" + before.Unit)));
                    if (comparison.ConditionCode.Length > 0) Line(T("condition-" + comparison.ConditionCode, detail.ReferenceLevel), 13, PlayerMenuTheme.Muted);
                }
            }
        }
        Line(T("read-only"), 14, PlayerMenuTheme.Muted);
        if (Layout.CondensedBrowserChrome || view.CategoryTotal > 0)
        {
            Line(T("browse-options"), 18);
            foreach (var option in new[] { ("filter", "filter-" + page.Query.Filters.ToString().ToLowerInvariant()),
                ("sort", "sort-" + page.Query.Sort.ToString().ToLowerInvariant()), ("view", view.Grid ? "list-view" : "grid-view"),
                ("more-types", "more-types"), ("addon-type", "addon-types"), ("refresh", "refresh") })
            { Button(ctx, option.Item1, option.Item2, new(x, y, width, 34)); y += 42; }
        }
        inspectorMaximumScroll = Math.Max(0, (int)(y + session.Current.InspectorScroll - b.Bottom));
        ctx.Restore();
        for (int i = controls.Count - 1; i >= firstInspectorControl; i--)
            if (controls[i].Bounds.Y < b.Y || controls[i].Bounds.Bottom > b.Bottom) controls.RemoveAt(i);
        controls.Add(("quantity", Layout.PrimaryAction with { Width = Layout.PrimaryAction.Width * .56 }, new("quantity", "quantity", MenuControlState.Default)));
        Button(ctx, "select-quantity", "select", Layout.PrimaryAction with { X = Layout.PrimaryAction.X + Layout.PrimaryAction.Width * .58, Width = Layout.PrimaryAction.Width * .42 },
            view.Selected != null && !view.Loading && !view.Stale && InventoryItemRow.CanSelectQuantity(view.Selected) ? MenuControlState.Default : MenuControlState.Disabled);
    }
    private string StatName(string code)
    {
        string normalized = VRPG.Inventory.Gear.GearModifierContract.NormalizeStat(code);
        var translated = Lang.GetIfExists("vrpg:menu-stat-" + normalized) ?? Lang.GetIfExists(code + "-name");
        if (translated != null) return translated;
        var data = api.ModLoader?.GetModSystem<VRPGModSystem>()?.DataRegistry;
        return data?.Stats.Get(code)?.Name ?? data?.Skills.Get(code)?.Name ?? T("additional-effect");
    }
    private string[] BrowseIds() => inventory != null ? inventory.View.Entries.Select(x => x.EntryId).ToArray()
        : FilteredEntries().Select(x => x.Id).ToArray();
    private string At(int x, int y) => controls.LastOrDefault(control => control.Bounds.Contains((x - Bounds.absX) / RuntimeEnv.GUIScale, (y - Bounds.absY) / RuntimeEnv.GUIScale)).Id ?? "";
    public override void OnMouseMove(ICoreClientAPI api, MouseEvent args) { string next = At(args.X, args.Y); if (hover != next) { hover = next; Refresh(); } }
    public override void OnMouseDownOnElement(ICoreClientAPI api, MouseEvent args) { pressed = At(args.X, args.Y); Refresh(); args.Handled = pressed.Length > 0; }
    public override void OnMouseUpOnElement(ICoreClientAPI api, MouseEvent args)
    {
        string target = At(args.X, args.Y); pressed = "";
        if (args.Button == EnumMouseButton.Left && target.Length > 0)
        { focus = target; session.Update(session.Current with { Focus = focus }); Activate(target); args.Handled = true; }
        Refresh();
    }
    public override void OnKeyDown(ICoreClientAPI api, KeyEvent args)
    {
        PlayerMenuBrowseKey? browseKey = args.KeyCode switch
        {
            (int)GlKeys.Up => PlayerMenuBrowseKey.Up,
            (int)GlKeys.Down => PlayerMenuBrowseKey.Down,
            (int)GlKeys.PageUp => PlayerMenuBrowseKey.PageUp,
            (int)GlKeys.PageDown => PlayerMenuBrowseKey.PageDown,
            (int)GlKeys.Home => PlayerMenuBrowseKey.Home,
            (int)GlKeys.End => PlayerMenuBrowseKey.End,
            _ => null
        };
        if (browseKey != null && PlayerMenuNavigation.BrowserHasKeyboardFocus(session.Current, Layout.AlternatePanes))
        {
            session.Update(InventoryItemGrid.MoveFocus(session.Current,
                BrowseIds(), browseKey.Value, inventory != null ? Layout.InventoryVisibleRows : Layout.VisibleRows, inventory?.View.Grid == true && inventory.Query.Category == PlayerItemCategories.Gear));
            if (inventory != null) inventory.View.Scroll = session.Current.ScrollAnchor;
            focus = session.Current.Focus; args.Handled = true; Refresh(); return;
        }
        if (args.KeyCode == (int)GlKeys.Tab)
        {
            AdvanceFocus(args.ShiftPressed); args.Handled = true;
        }
        else if (args.KeyCode == (int)GlKeys.Enter && focus.Length > 0) { Activate(focus); args.Handled = true; }
        else if (args.KeyCode is (int)GlKeys.PageDown or (int)GlKeys.PageUp)
        {
            session.Update(session.Current with { InspectorScroll = Math.Clamp(session.Current.InspectorScroll + (args.KeyCode == (int)GlKeys.PageDown ? 96 : -96), 0, inspectorMaximumScroll) });
            args.Handled = true; Refresh();
        }
    }
    public void AdvanceFocus(bool backwards)
    {
        var active = controls.Where(x => x.Button.CanActivate).Select(x => x.Id).ToArray();
        if (active.Length > 0) focus = active[(Array.IndexOf(active, focus) + (backwards ? active.Length - 1 : 1)) % active.Length];
        session.Update(session.Current with { Focus = focus });
        if (focus == "search") intent("focus-search");
        if (focus == "quantity") intent("quantity");
        Refresh();
    }
    private void Activate(string target)
    {
        if (controls.Any(x => x.Id == target && x.Button.CanActivate)) intent(target);
    }
    public override void OnMouseWheel(ICoreClientAPI api, MouseWheelEventArgs args)
    {
        int amount = Math.Sign(args.deltaPrecise == 0 ? args.delta : args.deltaPrecise);
        if (Layout.Inspector.Contains((api.Input.MouseX - Bounds.absX) / RuntimeEnv.GUIScale, (api.Input.MouseY - Bounds.absY) / RuntimeEnv.GUIScale)
            && (!Layout.AlternatePanes || session.Current.InspectorOnly))
        {
            session.Update(session.Current with { InspectorScroll = Math.Clamp(session.Current.InspectorScroll - amount * 32, 0, inspectorMaximumScroll) });
            args.SetHandled(); Refresh(); return;
        }
        if (!Layout.Browser.Contains((api.Input.MouseX - Bounds.absX) / RuntimeEnv.GUIScale, (api.Input.MouseY - Bounds.absY) / RuntimeEnv.GUIScale)
            || Layout.AlternatePanes && session.Current.InspectorOnly) return;
        int columns = inventory?.View.Grid == true && inventory.Query.Category == PlayerItemCategories.Gear ? 2 : 1;
        session.Update(session.Current with { ScrollAnchor = Math.Clamp(session.Current.ScrollAnchor - amount, 0, Math.Max(0, (BrowseIds().Length + columns - 1) / columns - (inventory != null ? Layout.InventoryVisibleRows : Layout.VisibleRows))) });
        if (inventory != null) inventory.View.Scroll = session.Current.ScrollAnchor;
        args.SetHandled(); Refresh();
    }
    public override void Dispose() { if (textureId > 0) { api.Render.GLDeleteTexture(textureId); textureId = 0; } base.Dispose(); }
}
