using System.Collections.Generic;
using VRPG.Data.Definitions;

namespace VRPG.Data.Library;

public sealed class StatFamiliesLibrarySource : ILibrarySource
{
    public string Code => "statfamilies";

    public IEnumerable<LibraryEntry> Build(VRPGDataRegistry data)
    {
        foreach (StatFamilyDefinition familyFile in data.StatFamilies.All)
        {
            yield return new LibraryEntry
            {
                Code = familyFile.Code,
                Name = familyFile.Name,
                Category = "stat families",
                Summary = familyFile.ConversionNote,
                Tags = new[] { "generated", "stat-family" },
                Fields = new[]
                {
                    new LibraryField("Families", familyFile.Families.Length.ToString()),
                    new LibraryField("Axes", familyFile.Axes.Length.ToString()),
                    new LibraryField("Reference", familyFile.SourceReference)
                }
            };

            foreach (StatFamilyEntryDefinition entry in familyFile.Families)
            {
                yield return new LibraryEntry
                {
                    Code = familyFile.Code + "/" + entry.Code,
                    Name = string.IsNullOrWhiteSpace(entry.NamePattern) ? entry.Code : entry.NamePattern,
                    Category = "stat family/" + entry.Category,
                    Summary = FirstNonEmpty(entry.Notes, entry.Conversion, entry.AppliesTo),
                    Tags = new[] { "generated", "stat-family", entry.Operation },
                    Fields = new[]
                    {
                        new LibraryField("Operation", entry.Operation),
                        new LibraryField("Layer", entry.Layer),
                        new LibraryField("Axes", string.Join(", ", entry.Axes)),
                        new LibraryField("Applies To", entry.AppliesTo)
                    }
                };
            }
        }
    }

    private static string FirstNonEmpty(params string[] values)
    {
        foreach (string value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return "";
    }
}
