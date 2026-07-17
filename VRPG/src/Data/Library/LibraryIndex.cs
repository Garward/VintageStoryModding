using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace VRPG.Data.Library;

public sealed class LibraryIndex
{
    private readonly List<LibraryEntry> entries = new List<LibraryEntry>();

    public IReadOnlyList<LibraryEntry> Entries => entries;
    public int Count => entries.Count;

    public static LibraryIndex Build(VRPGDataRegistry data)
    {
        var index = new LibraryIndex();
        ILibrarySource[] sources =
        {
            new ManualLibrarySource(),
            new StatsLibrarySource(),
            new StatFamiliesLibrarySource(),
            new DamageTypesLibrarySource(),
            new GearLibrarySource(),
            new StatusEffectsLibrarySource(),
            new TalentsLibrarySource(),
            new SkillsLibrarySource(),
            new DungeonsLibrarySource()
        };

        foreach (ILibrarySource source in sources)
        {
            foreach (LibraryEntry entry in source.Build(data))
            {
                if (!string.IsNullOrWhiteSpace(entry.Code))
                {
                    entry.Source = string.IsNullOrWhiteSpace(entry.Source) ? source.Code : entry.Source;
                    index.entries.Add(entry);
                }
            }
        }

        index.entries.Sort((left, right) =>
        {
            int cat = string.Compare(left.Category, right.Category, StringComparison.OrdinalIgnoreCase);
            return cat != 0 ? cat : string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
        });

        return index;
    }

    public IReadOnlyList<LibraryEntry> Search(string query, int limit = 20)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return entries.Take(limit).ToArray();
        }

        string needle = query.Trim();
        return entries
            .Where(entry => Matches(entry, needle))
            .Take(limit)
            .ToArray();
    }

    public string FormatCategories()
    {
        if (entries.Count == 0)
        {
            return "Library: no entries.";
        }

        var groups = entries
            .GroupBy(entry => entry.Category)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase);

        var sb = new StringBuilder();
        sb.Append("Library categories:");
        foreach (var group in groups)
        {
            sb.AppendLine().Append("- ").Append(group.Key).Append(": ").Append(group.Count());
        }

        return sb.ToString();
    }

    public string FormatList(string title, IEnumerable<LibraryEntry> list, int limit = 20)
    {
        LibraryEntry[] shown = list.Take(limit).ToArray();
        if (shown.Length == 0)
        {
            return title + ": no entries.";
        }

        var sb = new StringBuilder();
        sb.Append(title).Append(": ").Append(shown.Length).Append(" shown");
        foreach (LibraryEntry entry in shown)
        {
            sb.AppendLine()
                .Append("- [").Append(entry.Category).Append("] ")
                .Append(entry.Code).Append(": ").Append(entry.Name);
        }

        return sb.ToString();
    }

    private static bool Matches(LibraryEntry entry, string query)
    {
        return Contains(entry.Code, query)
            || Contains(entry.Name, query)
            || Contains(entry.Category, query)
            || Contains(entry.Summary, query)
            || entry.Tags.Any(tag => Contains(tag, query));
    }

    private static bool Contains(string haystack, string needle)
    {
        return haystack?.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
