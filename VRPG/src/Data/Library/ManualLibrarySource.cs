using System.Collections.Generic;
using VRPG.Data.Definitions;

namespace VRPG.Data.Library;

public sealed class ManualLibrarySource : ILibrarySource
{
    public string Code => "manual";

    public IEnumerable<LibraryEntry> Build(VRPGDataRegistry data)
    {
        foreach (LibraryEntryDefinition entry in data.Library.All)
        {
            yield return new LibraryEntry
            {
                Code = entry.Code,
                Name = entry.Name,
                Category = entry.Category,
                Summary = entry.Summary,
                Tags = entry.Tags,
                Source = Code
            };
        }
    }
}
