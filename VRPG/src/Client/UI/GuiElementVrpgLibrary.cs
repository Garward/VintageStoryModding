using System;
using System.Collections.Generic;
using System.Linq;
using Cairo;
using VRPG.Network;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VRPG.Client.UI;

public sealed class GuiElementVrpgLibrary : GuiElement
{
    private const double HeaderHeight = 54.0;
    private const double SideWidth = 205.0;
    private const double DetailWidth = 310.0;
    private const double Padding = 18.0;
    private const double RowHeight = 30.0;
    private const double SearchHeight = 32.0;
    private static readonly string[] DefaultCategoryKeys =
    {
        "currency",
        "affixes",
        "fittings",
        "etchings",
        "unique_gear",
        "assemblies",
        "augments",
        "support_fittings",
        "status_effects",
        "skills"
    };

    private readonly LibraryEntryPacket[] entries;
    private readonly List<CategoryGroup> categories;
    private readonly Action close;
    private int textureId;
    private int selectedEntryIndex;
    private int selectedCategoryIndex;
    private int categoryScroll;
    private int entryScroll;
    private int hoverEntryIndex = -1;
    private int hoverCategoryIndex = -1;
    private string query = "";

    public GuiElementVrpgLibrary(ICoreClientAPI api, ElementBounds bounds, LibraryEntryPacket[] entries, Action close)
        : base(api, bounds)
    {
        this.entries = entries ?? Array.Empty<LibraryEntryPacket>();
        this.close = close;
        categories = BuildCategories(this.entries);
        MouseOverCursor = "pointer";
    }

    public override bool Focusable => true;

    public override void ComposeElements(Context ctxStatic, ImageSurface surfaceStatic)
    {
        Bounds.CalcWorldBounds();
        Redraw();
    }

    public override void RenderInteractiveElements(float deltaTime)
    {
        if (textureId > 0)
        {
            api.Render.Render2DTexturePremultipliedAlpha(textureId, Bounds);
        }
    }

    public override void OnMouseMove(ICoreClientAPI api, MouseEvent args)
    {
        int nextCategory = CategoryAt(args.X, args.Y);
        int nextEntry = EntryAt(args.X, args.Y);
        if (nextCategory != hoverCategoryIndex || nextEntry != hoverEntryIndex)
        {
            hoverCategoryIndex = nextCategory;
            hoverEntryIndex = nextEntry;
            Redraw();
        }
    }

    public override void OnMouseUpOnElement(ICoreClientAPI api, MouseEvent args)
    {
        if (args.Button != EnumMouseButton.Left)
        {
            return;
        }

        if (CloseAt(args.X, args.Y))
        {
            close();
            args.Handled = true;
            api.Gui.PlaySound("menubutton_press");
            return;
        }

        int category = CategoryAt(args.X, args.Y);
        if (category >= 0)
        {
            selectedCategoryIndex = category;
            entryScroll = 0;
            SelectFirstVisibleEntry();
            Redraw();
            args.Handled = true;
            api.Gui.PlaySound("menubutton_press");
            return;
        }

        int entryIndex = EntryAt(args.X, args.Y);
        if (entryIndex >= 0)
        {
            selectedEntryIndex = entryIndex;
            Redraw();
            args.Handled = true;
            api.Gui.PlaySound("menubutton_press");
        }
    }

    public void SetQuery(string value)
    {
        query = value?.Trim() ?? "";
        entryScroll = 0;
        SelectFirstVisibleEntry();
        Redraw();
    }

    public override void OnMouseWheel(ICoreClientAPI api, MouseWheelEventArgs args)
    {
        if (!Bounds.PointInside(api.Input.MouseX, api.Input.MouseY))
        {
            return;
        }

        int direction = Math.Sign(args.deltaPrecise);
        if (direction == 0)
        {
            direction = Math.Sign(args.delta);
        }

        if (direction == 0)
        {
            return;
        }

        if (PointInCategories(api.Input.MouseX, api.Input.MouseY))
        {
            categoryScroll = GameMath.Clamp(categoryScroll - direction, 0, Math.Max(0, categories.Count - VisibleCategoryRows()));
        }
        else
        {
            List<int> visible = VisibleEntryIndices();
            entryScroll = GameMath.Clamp(entryScroll - direction, 0, Math.Max(0, visible.Count - VisibleEntryRows()));
        }

        Redraw();
        args.SetHandled();
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
        DrawBackdrop(ctx, width, height);
        DrawHeader(ctx, width);

        double contentY = scaled(HeaderHeight);
        double contentH = height - contentY - scaled(Padding + 34.0);
        double sideW = scaled(SideWidth);
        double detailW = scaled(DetailWidth);
        double listX = scaled(Padding) + sideW + scaled(14.0);
        double listW = width - listX - detailW - scaled(Padding + 14.0);
        double detailX = width - detailW - scaled(Padding);

        DrawCategories(ctx, scaled(Padding), contentY, sideW, contentH);
        DrawEntries(ctx, listX, contentY, listW, contentH);
        DrawDetail(ctx, detailX, contentY, detailW, contentH);
        DrawFooter(ctx, scaled(Padding), height - scaled(27.0), width - scaled(Padding * 2.0));
    }

    private static List<CategoryGroup> BuildCategories(LibraryEntryPacket[] entries)
    {
        var result = new List<CategoryGroup>
        {
            new CategoryGroup("all", "all", entries.Length, curated: true)
        };

        foreach (string key in DefaultCategoryKeys)
        {
            int count = entries.Count(entry => EntryInDefaultCategory(entry, key));
            result.Add(new CategoryGroup(key, DisplayCategory(key), count, curated: true));
        }

        return result;
    }

    private void DrawBackdrop(Context ctx, double width, double height)
    {
        RoundedRectangle(ctx, scaled(2.0), scaled(2.0), width - scaled(4.0), height - scaled(4.0), scaled(2.0));
        using (LinearGradient gradient = new LinearGradient(0, 0, width, height))
        {
            gradient.AddColorStop(0, new Color(0.10, 0.035, 0.022, 0.98));
            gradient.AddColorStop(0.5, new Color(0.20, 0.065, 0.032, 0.98));
            gradient.AddColorStop(1, new Color(0.065, 0.030, 0.023, 0.98));
            ctx.SetSource(gradient);
            ctx.FillPreserve();
        }

        ctx.LineWidth = scaled(2.0);
        ctx.SetSourceRGBA(VrpgGuiTheme.BorderR, VrpgGuiTheme.BorderG, VrpgGuiTheme.BorderB, 0.95);
        ctx.Stroke();

        ctx.Save();
        ctx.LineWidth = scaled(1.0);
        ctx.SetSourceRGBA(1, 0.72, 0.17, 0.25);
        for (double y = scaled(88.0); y < height - scaled(40.0); y += scaled(116.0))
        {
            ctx.MoveTo(scaled(18.0), Math.Floor(y) + 0.5);
            ctx.LineTo(width - scaled(18.0), Math.Floor(y) + 0.5);
            ctx.Stroke();
        }

        ctx.Restore();
    }

    private void DrawHeader(Context ctx, double width)
    {
        DrawText(ctx, "VRPG Library", scaled(28.0), scaled(35.0), scaled(20.0), bold: true, ColorGold());
        DrawHeaderTab(ctx, width / 2.0 - scaled(110.0), scaled(14.0), scaled(220.0), "Library", true);
        DrawHeaderTab(ctx, width - scaled(286.0), scaled(14.0), scaled(210.0), "Details", false);
        DrawCloseButton(ctx, width - scaled(34.0), scaled(18.0));
    }

    private void DrawHeaderTab(Context ctx, double x, double y, double width, string text, bool active)
    {
        ctx.Save();
        ctx.SetSourceRGBA(1.0, 0.62, 0.05, active ? 0.92 : 0.35);
        ctx.LineWidth = scaled(active ? 1.4 : 1.0);
        ctx.MoveTo(x, y + scaled(26.0));
        ctx.LineTo(x + width, y + scaled(26.0));
        ctx.Stroke();
        DrawText(ctx, text, x + width / 2.0, y + scaled(20.0), scaled(21.0), bold: true, active ? ColorGold() : ColorMuted(), center: true);
        ctx.Restore();
    }

    private void DrawCategories(Context ctx, double x, double y, double width, double height)
    {
        DrawPane(ctx, x, y, width, height);
        DrawText(ctx, "Categories", x + scaled(12.0), y + scaled(25.0), scaled(15.0), bold: true, ColorText());

        int visibleRows = VisibleCategoryRows();
        for (int row = 0; row < visibleRows; row++)
        {
            int index = categoryScroll + row;
            if (index >= categories.Count) break;

            CategoryGroup category = categories[index];
            double rowY = y + scaled(45.0) + row * scaled(RowHeight);
            bool selected = index == selectedCategoryIndex;
            bool hover = index == hoverCategoryIndex;
            if (selected || hover)
            {
                DrawSelection(ctx, x + scaled(7.0), rowY - scaled(17.0), width - scaled(14.0), scaled(25.0), selected);
            }

            DrawText(ctx, category.DisplayName, x + scaled(14.0), rowY, scaled(14.0), bold: selected, selected ? ColorGold() : ColorText(), maxWidth: width - scaled(72.0));
            DrawText(ctx, category.Count.ToString(), x + width - scaled(18.0), rowY, scaled(14.0), bold: selected, selected ? ColorGold() : ColorMuted(), center: false, right: true);
        }
    }

    private void DrawEntries(Context ctx, double x, double y, double width, double height)
    {
        DrawPane(ctx, x, y, width, height);
        DrawText(ctx, "Entries", x + scaled(12.0), y + scaled(25.0), scaled(15.0), bold: true, ColorText());
        DrawSearchWell(ctx, x + scaled(12.0), y + scaled(34.0), width - scaled(24.0), scaled(28.0));

        List<int> visible = VisibleEntryIndices();
        int visibleRows = VisibleEntryRows();
        for (int row = 0; row < visibleRows; row++)
        {
            int listIndex = entryScroll + row;
            if (listIndex >= visible.Count) break;

            int entryIndex = visible[listIndex];
            LibraryEntryPacket entry = entries[entryIndex];
            double rowY = y + scaled(75.0) + row * scaled(RowHeight);
            bool selected = entryIndex == selectedEntryIndex;
            bool hover = entryIndex == hoverEntryIndex;
            if (selected || hover)
            {
                DrawSelection(ctx, x + scaled(7.0), rowY - scaled(17.0), width - scaled(14.0), scaled(25.0), selected);
            }

            DrawText(ctx, entry.Name, x + scaled(14.0), rowY, scaled(14.0), bold: selected, selected ? ColorGold() : ColorText(), maxWidth: width - scaled(28.0));
        }
    }

    private void DrawDetail(Context ctx, double x, double y, double width, double height)
    {
        DrawPane(ctx, x, y, width, height);
        LibraryEntryPacket? entry = SelectedEntry();
        if (entry == null)
        {
            DrawText(ctx, "No entry selected.", x + scaled(14.0), y + scaled(30.0), scaled(15.0), bold: false, ColorText());
            return;
        }

        DrawText(ctx, entry.Name, x + scaled(14.0), y + scaled(28.0), scaled(18.0), bold: true, ColorGold(), maxWidth: width - scaled(28.0));
        DrawText(ctx, entry.Code, x + scaled(14.0), y + scaled(50.0), scaled(12.0), bold: false, ColorMuted(), maxWidth: width - scaled(28.0));

        double textY = y + scaled(82.0);
        textY = DrawWrappedText(ctx, entry.Summary, x + scaled(14.0), textY, width - scaled(28.0), scaled(14.0), ColorText(), 5);

        textY += scaled(14.0);
        DrawText(ctx, "Category", x + scaled(14.0), textY, scaled(12.0), bold: true, ColorMuted());
        DrawText(ctx, DisplayCategory(entry.Category), x + scaled(104.0), textY, scaled(12.0), bold: false, ColorText(), maxWidth: width - scaled(118.0));
        textY += scaled(22.0);
        DrawText(ctx, "Source", x + scaled(14.0), textY, scaled(12.0), bold: true, ColorMuted());
        DrawText(ctx, string.IsNullOrWhiteSpace(entry.Source) ? "generated" : entry.Source, x + scaled(104.0), textY, scaled(12.0), bold: false, ColorText(), maxWidth: width - scaled(118.0));

        if (entry.Fields.Length == 0)
        {
            return;
        }

        textY += scaled(36.0);
        DrawText(ctx, "Fields", x + scaled(14.0), textY, scaled(14.0), bold: true, ColorGold());
        textY += scaled(20.0);
        foreach (LibraryFieldPacket field in entry.Fields.Take(5))
        {
            DrawField(ctx, x + scaled(14.0), textY, width - scaled(28.0), field.Label, field.Value);
            textY += scaled(34.0);
            if (textY > y + height - scaled(24.0)) break;
        }
    }

    private void DrawFooter(Context ctx, double x, double y, double width)
    {
        ctx.SetSourceRGBA(1, 0.62, 0.05, 0.30);
        ctx.MoveTo(x, y - scaled(9.0));
        ctx.LineTo(x + width, y - scaled(9.0));
        ctx.LineWidth = scaled(1.0);
        ctx.Stroke();
        DrawText(ctx, entries.Length + " generated entries", x, y + scaled(8.0), scaled(12.0), bold: true, ColorGold());
        DrawText(ctx, "Data-first documentation generated from loaded VRPG registries.", x + scaled(145.0), y + scaled(8.0), scaled(12.0), bold: false, ColorMuted());
    }

    private void DrawPane(Context ctx, double x, double y, double width, double height)
    {
        RoundedRectangle(ctx, x, y, width, height, scaled(3.0));
        ctx.SetSourceRGBA(0.06, 0.028, 0.020, 0.60);
        ctx.FillPreserve();
        ctx.LineWidth = scaled(1.0);
        ctx.SetSourceRGBA(0.95, 0.58, 0.12, 0.45);
        ctx.Stroke();
    }

    private void DrawSearchWell(Context ctx, double x, double y, double width, double height)
    {
        RoundedRectangle(ctx, x, y, width, height, scaled(2.0));
        ctx.SetSourceRGBA(0, 0, 0, 0.26);
        ctx.FillPreserve();
        ctx.SetSourceRGBA(1, 0.62, 0.06, 0.36);
        ctx.Stroke();

        if (string.IsNullOrEmpty(query))
        {
            return;
        }
    }

    private void DrawCloseButton(Context ctx, double x, double y)
    {
        ctx.Save();
        ctx.LineWidth = scaled(2.0);
        ctx.SetSourceRGBA(0.98, 0.88, 0.70, 0.92);
        ctx.MoveTo(x - scaled(7.0), y - scaled(7.0));
        ctx.LineTo(x + scaled(7.0), y + scaled(7.0));
        ctx.MoveTo(x + scaled(7.0), y - scaled(7.0));
        ctx.LineTo(x - scaled(7.0), y + scaled(7.0));
        ctx.Stroke();
        ctx.Restore();
    }

    private void DrawSelection(Context ctx, double x, double y, double width, double height, bool selected)
    {
        RoundedRectangle(ctx, x, y, width, height, scaled(2.0));
        using LinearGradient gradient = new LinearGradient(x, y, x + width, y);
        gradient.AddColorStop(0, new Color(0.70, 0.22, 0.05, selected ? 0.56 : 0.28));
        gradient.AddColorStop(1, new Color(1.00, 0.63, 0.08, selected ? 0.26 : 0.12));
        ctx.SetSource(gradient);
        ctx.FillPreserve();
        ctx.SetSourceRGBA(1, 0.68, 0.16, selected ? 0.70 : 0.35);
        ctx.Stroke();
    }

    private void DrawEntryIcon(Context ctx, double x, double y, bool selected)
    {
        RoundedRectangle(ctx, x, y, scaled(20.0), scaled(20.0), scaled(2.0));
        ctx.SetSourceRGBA(0.18, 0.09, 0.04, 1);
        ctx.FillPreserve();
        ctx.SetSourceRGBA(1, 0.62, 0.06, selected ? 0.90 : 0.45);
        ctx.Stroke();
        ctx.SetSourceRGBA(VrpgGuiTheme.GreenR, VrpgGuiTheme.GreenG, VrpgGuiTheme.GreenB, selected ? 0.95 : 0.65);
        ctx.Arc(x + scaled(10.0), y + scaled(10.0), scaled(4.5), 0, Math.PI * 2.0);
        ctx.Fill();
    }

    private void DrawField(Context ctx, double x, double y, double width, string label, string value)
    {
        RoundedRectangle(ctx, x, y - scaled(15.0), width, scaled(26.0), scaled(2.0));
        ctx.SetSourceRGBA(0.0, 0.0, 0.0, 0.18);
        ctx.Fill();
        DrawText(ctx, label, x + scaled(8.0), y, scaled(11.0), bold: true, ColorMuted(), maxWidth: width * 0.36);
        DrawText(ctx, value, x + width * 0.38, y, scaled(11.0), bold: false, ColorText(), maxWidth: width * 0.58);
    }

    private void SelectFirstVisibleEntry()
    {
        List<int> visible = VisibleEntryIndices();
        selectedEntryIndex = visible.Count == 0 ? -1 : visible[0];
    }

    private LibraryEntryPacket? SelectedEntry()
    {
        if (selectedEntryIndex < 0 || selectedEntryIndex >= entries.Length)
        {
            return entries.FirstOrDefault();
        }

        return entries[selectedEntryIndex];
    }

    private List<int> VisibleEntryIndices()
    {
        CategoryGroup category = selectedCategoryIndex >= 0 && selectedCategoryIndex < categories.Count ? categories[selectedCategoryIndex] : categories[0];
        var result = new List<int>();
        for (int i = 0; i < entries.Length; i++)
        {
            if (CategoryMatches(entries[i], category) && MatchesQuery(entries[i]))
            {
                result.Add(i);
            }
        }

        return result;
    }

    private static bool CategoryMatches(LibraryEntryPacket entry, CategoryGroup category)
    {
        if (category.Name == "all")
        {
            return true;
        }

        if (category.Curated)
        {
            return EntryInDefaultCategory(entry, category.Name);
        }

        return string.Equals(entry.Category, category.Name, StringComparison.OrdinalIgnoreCase);
    }

    private bool MatchesQuery(LibraryEntryPacket entry)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        return Contains(entry.Name, query)
            || Contains(entry.Code, query)
            || Contains(entry.Category, query)
            || Contains(DisplayCategory(entry.Category), query)
            || Contains(entry.Summary, query)
            || Contains(entry.Source, query)
            || entry.Tags.Any(tag => Contains(tag, query))
            || entry.Fields.Any(field => Contains(field.Label, query) || Contains(field.Value, query));
    }

    private int CategoryAt(double mouseX, double mouseY)
    {
        double localX = mouseX - Bounds.absX;
        double localY = mouseY - Bounds.absY;
        double x = scaled(Padding);
        double y = scaled(HeaderHeight + 45.0);
        double width = scaled(SideWidth);
        if (localX < x || localX > x + width || localY < y - scaled(18.0)) return -1;
        int row = (int)((localY - (y - scaled(18.0))) / scaled(RowHeight));
        int index = categoryScroll + row;
        return row >= 0 && row < VisibleCategoryRows() && index < categories.Count ? index : -1;
    }

    private int EntryAt(double mouseX, double mouseY)
    {
        double localX = mouseX - Bounds.absX;
        double localY = mouseY - Bounds.absY;
        double sideW = scaled(SideWidth);
        double detailW = scaled(DetailWidth);
        double x = scaled(Padding) + sideW + scaled(14.0);
        double width = Bounds.OuterWidth - x - detailW - scaled(Padding + 14.0);
        double y = scaled(HeaderHeight + 75.0);
        if (localX < x || localX > x + width || localY < y - scaled(18.0)) return -1;

        int row = (int)((localY - (y - scaled(18.0))) / scaled(RowHeight));
        List<int> visible = VisibleEntryIndices();
        int listIndex = entryScroll + row;
        return row >= 0 && row < VisibleEntryRows() && listIndex < visible.Count ? visible[listIndex] : -1;
    }

    private bool PointInCategories(double mouseX, double mouseY)
    {
        double localX = mouseX - Bounds.absX;
        double localY = mouseY - Bounds.absY;
        return localX >= scaled(Padding)
            && localX <= scaled(Padding + SideWidth)
            && localY >= scaled(HeaderHeight)
            && localY <= Bounds.OuterHeight - scaled(Padding);
    }

    private int VisibleCategoryRows()
    {
        double height = Bounds.OuterHeight - scaled(HeaderHeight + Padding * 2.0 + 79.0);
        return Math.Max(1, (int)(height / scaled(RowHeight)));
    }

    private int VisibleEntryRows()
    {
        double height = Bounds.OuterHeight - scaled(HeaderHeight + Padding * 2.0 + 79.0 + SearchHeight);
        return Math.Max(1, (int)(height / scaled(RowHeight)));
    }

    private bool CloseAt(double mouseX, double mouseY)
    {
        double localX = mouseX - Bounds.absX;
        double localY = mouseY - Bounds.absY;
        double closeX = Bounds.OuterWidth - scaled(34.0);
        double closeY = scaled(18.0);
        return Math.Abs(localX - closeX) <= scaled(18.0) && Math.Abs(localY - closeY) <= scaled(18.0);
    }

    private double DrawWrappedText(Context ctx, string text, double x, double y, double maxWidth, double size, double[] color, int maxLines)
    {
        foreach (string line in WrapText(ctx, text ?? "", maxWidth, size, false).Take(maxLines))
        {
            DrawText(ctx, line, x, y, size, bold: false, color);
            y += size * 1.28;
        }

        return y;
    }

    private static IEnumerable<string> WrapText(Context ctx, string text, double maxWidth, double size, bool bold)
    {
        SelectFont(ctx, size, bold);
        var current = "";
        foreach (string word in text.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string next = string.IsNullOrEmpty(current) ? word : current + " " + word;
            TextExtents extents = ctx.TextExtents(next);
            if (extents.Width > maxWidth && current.Length > 0)
            {
                yield return current;
                current = word;
            }
            else
            {
                current = next;
            }
        }

        if (current.Length > 0)
        {
            yield return current;
        }
    }

    private static void DrawText(Context ctx, string text, double x, double y, double size, bool bold, double[] color, bool center = false, bool right = false, double maxWidth = 0)
    {
        if (string.IsNullOrEmpty(text)) return;
        SelectFont(ctx, size, bold);
        string display = maxWidth > 0 ? Ellipsize(ctx, text, maxWidth) : text;
        TextExtents extents = ctx.TextExtents(display);
        double drawX = x;
        if (center) drawX -= extents.Width / 2.0;
        if (right) drawX -= extents.Width;

        ctx.SetSourceRGBA(0, 0, 0, 0.42);
        ctx.MoveTo(drawX + 1.0, y + 1.0);
        ctx.ShowText(display);
        ctx.SetSourceRGBA(color[0], color[1], color[2], color[3]);
        ctx.MoveTo(drawX, y);
        ctx.ShowText(display);
    }

    private static void SelectFont(Context ctx, double size, bool bold)
    {
        ctx.SelectFontFace("Lora", FontSlant.Normal, bold ? FontWeight.Bold : FontWeight.Normal);
        ctx.SetFontSize(size);
    }

    private static string Ellipsize(Context ctx, string text, double maxWidth)
    {
        if (ctx.TextExtents(text).Width <= maxWidth) return text;
        const string suffix = "...";
        for (int length = text.Length - 1; length > 0; length--)
        {
            string candidate = text.Substring(0, length).TrimEnd() + suffix;
            if (ctx.TextExtents(candidate).Width <= maxWidth)
            {
                return candidate;
            }
        }

        return suffix;
    }

    private static bool Contains(string value, string needle)
    {
        return !string.IsNullOrEmpty(value) && value.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool EntryInDefaultCategory(LibraryEntryPacket entry, string categoryKey)
    {
        return CategoryNameInDefaultCategory(entry.Category, categoryKey)
            || Contains(entry.Name, CategorySearchTerm(categoryKey))
            || Contains(entry.Code, CategorySearchTerm(categoryKey))
            || entry.Tags.Any(tag => Contains(tag, CategorySearchTerm(categoryKey)));
    }

    private static bool CategoryNameInDefaultCategory(string category, string categoryKey)
    {
        string normalized = Normalize(category);
        switch (categoryKey)
        {
            case "currency": return normalized == "currency" || normalized.StartsWith("currency/");
            case "affixes": return normalized == "affixes" || normalized == "gear/affixes" || normalized.StartsWith("affixes/");
            case "fittings": return normalized == "fittings" || normalized.StartsWith("fittings/") || normalized == "gems" || normalized.StartsWith("gems/");
            case "etchings": return normalized == "etchings" || normalized.StartsWith("etchings/") || normalized == "runes" || normalized.StartsWith("runes/");
            case "unique_gear": return normalized == "unique gear" || normalized == "unique_gear" || normalized == "gear/uniques" || normalized == "gear/unique";
            case "assemblies": return normalized == "assemblies" || normalized.StartsWith("assemblies/") || normalized == "runewords" || normalized.StartsWith("runewords/");
            case "augments": return normalized == "augments" || normalized.StartsWith("augments/");
            case "support_fittings": return normalized == "support fittings" || normalized == "support_fittings" || normalized == "support gems" || normalized == "support_gems" || normalized == "gems/support" || normalized == "support/gems";
            case "status_effects": return normalized == "status effects" || normalized == "status_effects" || normalized == "effects" || normalized == "effects/status";
            case "skills": return normalized == "skills" || normalized.StartsWith("skills/") || normalized.StartsWith("talents/");
            default: return false;
        }
    }

    private static string CategorySearchTerm(string categoryKey)
    {
        switch (categoryKey)
        {
            case "currency": return "currency";
            case "affixes": return "affix";
            case "fittings": return "fitting";
            case "etchings": return "etch";
            case "unique_gear": return "unique";
            case "assemblies": return "assembly";
            case "augments": return "augment";
            case "support_fittings": return "support";
            case "status_effects": return "effect";
            case "skills": return "talent";
            default: return categoryKey;
        }
    }

    private static string Normalize(string value)
    {
        return (value ?? "").Trim().Replace('_', ' ').ToLowerInvariant();
    }

    private static string DisplayCategory(string category)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            return "general";
        }

        switch (category)
        {
            case "currency": return "Currency";
            case "affixes": return "Affixes";
            case "fittings": return "Fittings";
            case "etchings": return "Etchings";
            case "unique_gear": return "Unique Gear";
            case "assemblies": return "Assemblies";
            case "augments": return "Augments";
            case "support_fittings": return "Support Fittings";
            case "status_effects": return "Status Effects";
            case "skills": return "Skills";
        }

        if (string.Equals(category, "stat families", StringComparison.OrdinalIgnoreCase))
        {
            return "stats";
        }

        if (category.StartsWith("stat family/", StringComparison.OrdinalIgnoreCase))
        {
            return "stats/" + category.Substring("stat family/".Length);
        }

        return category;
    }

    private static void RoundedRectangle(Context ctx, double x, double y, double width, double height, double radius)
    {
        if (width <= 0.0 || height <= 0.0) return;
        radius = Math.Min(radius, Math.Min(width, height) / 2.0);
        ctx.NewSubPath();
        ctx.Arc(x + width - radius, y + radius, radius, -Math.PI / 2.0, 0.0);
        ctx.Arc(x + width - radius, y + height - radius, radius, 0.0, Math.PI / 2.0);
        ctx.Arc(x + radius, y + height - radius, radius, Math.PI / 2.0, Math.PI);
        ctx.Arc(x + radius, y + radius, radius, Math.PI, 3.0 * Math.PI / 2.0);
        ctx.ClosePath();
    }

    private static double[] ColorGold() => new[] { VrpgGuiTheme.GoldR, VrpgGuiTheme.GoldG, VrpgGuiTheme.GoldB, 1.0 };
    private static double[] ColorText() => new[] { VrpgGuiTheme.TextR, VrpgGuiTheme.TextG, VrpgGuiTheme.TextB, 1.0 };
    private static double[] ColorMuted() => new[] { VrpgGuiTheme.MutedR, VrpgGuiTheme.MutedG, VrpgGuiTheme.MutedB, 1.0 };

    private sealed class CategoryGroup
    {
        public CategoryGroup(string name, string displayName, int count, bool curated)
        {
            Name = name;
            DisplayName = displayName;
            Count = count;
            Curated = curated;
        }

        public string Name { get; }
        public string DisplayName { get; }
        public int Count { get; }
        public bool Curated { get; }
    }
}
