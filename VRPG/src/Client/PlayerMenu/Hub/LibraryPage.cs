using System;
using System.Linq;
using VRPG.Client.PlayerMenu.Components;
using VRPG.Client.PlayerMenu.Inventory;
using VRPG.Network;

namespace VRPG.Client.PlayerMenu.Hub;

/// <summary>Connection-owned library filtering, selection and bounded list viewport.</summary>
public sealed class LibraryPage
{
    private readonly HubPageClientService hub;
    private LibraryEntryPacket[] entries = Array.Empty<LibraryEntryPacket>();
    private int visibleRows = 1;
    public string Query => hub.State.LibraryQuery;
    public string Category { get; private set; } = "";
    public string[] Categories { get; private set; } = new[] { "" };
    public LibraryEntryPacket[] VisibleEntries { get; private set; } = Array.Empty<LibraryEntryPacket>();
    public LibraryEntryPacket? SelectedEntry => VisibleEntries.FirstOrDefault(e => e.Code == hub.State.LibrarySelection);
    public int Scroll { get; private set; }
    public LibraryPage(HubPageClientService hub) => this.hub = hub;
    public static string ReadableName(LibraryEntryPacket entry) => !string.IsNullOrWhiteSpace(entry.Name) && !entry.Name.Contains(':') && entry.Name != entry.Code
        ? entry.Name : PlayerItemDisplayNameResolver.HumanizeAssetPath(entry.Code);
    public void SetQuery(string value)
    {
        var search = new SearchField(); search.SetText(value ?? "");
        hub.State = hub.State with { LibraryQuery = search.Text }; Scroll = 0; Filter();
    }
    public void SelectCategory(string category)
    {
        Category = Categories.FirstOrDefault(candidate => string.Equals(candidate, category, StringComparison.OrdinalIgnoreCase)) ?? "";
        Scroll = 0;
        Filter();
    }
    public void SelectEntry(string code)
    { if (VisibleEntries.Any(e => e.Code == code)) hub.State = hub.State with { LibrarySelection = code }; }
    public void ScrollBy(int delta, int rows)
    { visibleRows = Math.Max(1, rows); Scroll = (int)Math.Clamp((long)Scroll + delta, 0, Math.Max(0, VisibleEntries.Length - visibleRows)); }
    internal void Receive(LibraryEntryPacket[] next)
    {
        entries = next;
        Categories = new[] { "" }.Concat(entries.Select(e => e.Category ?? "").Where(c => c.Length > 0)
            .GroupBy(c => c, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderBy(c => c, StringComparer.Ordinal).First())
            .OrderBy(c => c, StringComparer.OrdinalIgnoreCase).ThenBy(c => c, StringComparer.Ordinal)).ToArray();
        Category = Categories.FirstOrDefault(candidate => string.Equals(candidate, Category, StringComparison.OrdinalIgnoreCase)) ?? "";
        Filter();
    }
    private void Filter()
    {
        string query = Query.Trim();
        bool Matches(string? text) => LibraryDisplayText.Format(text).Contains(query, StringComparison.OrdinalIgnoreCase);
        VisibleEntries = entries.Where(e => (Category.Length == 0 || string.Equals(e.Category, Category, StringComparison.OrdinalIgnoreCase))
            && (query.Length == 0 || Matches(ReadableName(e)) || Matches(e.Summary) || e.Tags.Any(Matches)))
            .OrderBy(ReadableName, StringComparer.OrdinalIgnoreCase).ThenBy(e => e.Code, StringComparer.Ordinal).ToArray();
        if (SelectedEntry == null) hub.State = hub.State with { LibrarySelection = VisibleEntries.FirstOrDefault()?.Code ?? "" };
        ScrollBy(0, visibleRows);
    }
    internal void Reset()
    { entries = Array.Empty<LibraryEntryPacket>(); Categories = new[] { "" }; Category = ""; VisibleEntries = Array.Empty<LibraryEntryPacket>(); Scroll = 0; }
}
