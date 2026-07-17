using System.Collections.Generic;
using VRPG.Data.Definitions;

namespace VRPG.Data.Library;

public sealed class StatsLibrarySource : ILibrarySource
{
    public string Code => "stats";

    public IEnumerable<LibraryEntry> Build(VRPGDataRegistry data)
    {
        foreach (StatDefinition stat in data.Stats.All)
        {
            yield return new LibraryEntry
            {
                Code = stat.Code,
                Name = stat.Name,
                Category = "stats/" + stat.Category,
                Summary = stat.Description,
                Tags = stat.Percent ? new[] { "stat", "percent" } : new[] { "stat" },
                Fields = new[]
                {
                    new LibraryField("Base", stat.BaseValue.ToString("0.###")),
                    new LibraryField("Range", stat.MinValue.ToString("0.###") + " to " + stat.MaxValue.ToString("0.###")),
                    new LibraryField("Percent", stat.Percent ? "yes" : "no")
                }
            };
        }
    }
}
