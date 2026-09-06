using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Cairo;
using VRPG.Client.PlayerMenu.Components;
using VRPG.Client.UI;
using VRPG.Inventory;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace VRPG.Client.PlayerMenu.Shell;

/// <summary>Native cached composition host. Pages issue named intents; rendering owns no gameplay state.</summary>
public sealed class PlayerMenuShell : GuiElement
{
    private readonly PlayerMenuSessionState session;
    private readonly PlayerMenuGallery gallery;
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
        PlayerMenuSessionState session, PlayerMenuGallery gallery, Action<string> intent) : base(api, bounds)
    {
        Layout = layout; this.session = session; this.gallery = gallery; this.intent = intent;
        itemRenderer = new(api, bounds);
        focus = session.Current.Focus;
    }
    public static string T(string key, params object[] args) => Lang.Get("vrpg:menu-" + key, args);
    public override void ComposeElements(Context ctxStatic, ImageSurface surfaceStatic) { Bounds.CalcWorldBounds(); Refresh(); }
    public override void RenderInteractiveElements(float deltaTime)
    {
        if (gallery.Equipment.Advance(deltaTime)) Refresh();
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
        Text(ctx, T("gallery-title"), new(122, 12, Math.Max(0, Layout.Width - 244), 30), 16);
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
        if (browse) DrawBrowser(ctx);
        if (inspect) DrawInspector(ctx);
        if (inspect) Button(ctx, "action", "gallery-action", Layout.PrimaryAction, gallery.State);
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
        double tabWidth = (bounds.Width - 24) / 3;
        var categories = TypedTabStrip.Categories;
        for (int i = 0; i < categories.Length; i++)
            Button(ctx, "category:" + categories[i], "category-" + categories[i],
                new(bounds.X + 12 + i % 3 * tabWidth, bounds.Y + 10 + i / 3 * 38, tabWidth - 4, 34),
                session.Current.Category == categories[i] ? MenuControlState.Selected : MenuControlState.Default);
        Button(ctx, "count", "gallery-count", new(bounds.X + 12 + 2 * tabWidth, bounds.Y + 48, tabWidth - 4, 34));
        controls.Add(("search", new(bounds.X + 12, bounds.Y + 94, bounds.Width - 24, 30), new("search", "search", MenuControlState.Default)));
        var entries = FilteredEntries();
        var clip = new MenuRect(bounds.X + 8, bounds.Y + PlayerMenuLayout.BrowserHeaderHeight, bounds.Width - 16, Math.Max(0, bounds.Height - PlayerMenuLayout.BrowserHeaderHeight - 4));
        ctx.Save(); ctx.Rectangle(clip.X, clip.Y, clip.Width, clip.Height); ctx.Clip();
        if (entries.Length == 0) Text(ctx, T(gallery.Entries(session.Current.Category).Count == 0 ? "empty-category" : "empty-search"), clip with { Height = 90 }, 18);
        int start = Math.Min(session.Current.ScrollAnchor, Math.Max(0, entries.Length - Layout.VisibleRows));
        foreach (var pair in entries.Skip(start).Take(Layout.RenderRowLimit).Select((entry, index) => (entry, index)))
        {
            var row = new MenuRect(clip.X + 4, clip.Y + pair.index * PlayerMenuLayout.RowHeight, clip.Width - 8, 48);
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
            if (gallery.Equipment.VisibleFraction > 0)
            {
                double reveal = 50 * gallery.Equipment.VisibleFraction;
                ctx.Save(); ctx.Rectangle(x, y, width, reveal); ctx.Clip();
                Text(ctx, T("gallery-equipment"), new(x, y - 50 + reveal, width, 46), 15); ctx.Restore();
                y += reveal;
            }
        }
        double feedbackHeight = MeasureTextHeight(ctx, T(gallery.FeedbackKey), width, 14);
        Text(ctx, T(gallery.FeedbackKey), new(x, y + 8, width, feedbackHeight), 14,
            gallery.State == MenuControlState.Rejected ? PlayerMenuTheme.Danger : PlayerMenuTheme.Muted);
        inspectorMaximumScroll = Math.Max(0, (int)(y + feedbackHeight + 24 + session.Current.InspectorScroll - b.Bottom));
        ctx.Restore();
        controls.RemoveAll(control => control.Id == "equipment" && (control.Bounds.Y < b.Y || control.Bounds.Bottom > b.Bottom));
    }
    public GalleryEntry[] FilteredEntries() => gallery.Entries(session.Current.Category)
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
        string label = key == "gallery-count" ? T(key, gallery.Entries(session.Current.Category).Count,
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
        double y = b.Y + size; string line = "";
        foreach (string word in text.Split(' '))
        {
            string next = line.Length == 0 ? word : line + " " + word;
            if (ctx.TextExtents(next).Width > b.Width && line.Length > 0)
            { ctx.MoveTo(b.X, y); ctx.ShowText(line); y += size + 5; line = word; }
            else line = next;
        }
        ctx.MoveTo(b.X, y); ctx.ShowText(line); ctx.Restore();
    }
    private static double MeasureTextHeight(Context ctx, string text, double width, double size)
    {
        ctx.SelectFontFace(PlayerMenuTheme.Font, FontSlant.Normal, FontWeight.Normal); ctx.SetFontSize(size);
        int lines = 1; string line = "";
        foreach (string word in text.Split(' '))
        {
            string next = line.Length == 0 ? word : line + " " + word;
            if (ctx.TextExtents(next).Width > width && line.Length > 0) { lines++; line = word; }
            else line = next;
        }
        return lines * (size + 5);
    }
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
        session.Update(session.Current with { ScrollAnchor = Math.Clamp(session.Current.ScrollAnchor - amount, 0, Math.Max(0, FilteredEntries().Length - Layout.VisibleRows)) });
        args.SetHandled(); Refresh();
    }
    public override void Dispose() { if (textureId > 0) { api.Render.GLDeleteTexture(textureId); textureId = 0; } base.Dispose(); }
}
