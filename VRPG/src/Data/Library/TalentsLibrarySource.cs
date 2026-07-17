using System.Collections.Generic;
using System.Linq;
using VRPG.Data.Definitions;

namespace VRPG.Data.Library;

public sealed class TalentsLibrarySource : ILibrarySource
{
    public string Code => "talents";

    public IEnumerable<LibraryEntry> Build(VRPGDataRegistry data)
    {
        foreach (TalentNodeDefinition talent in data.Talents.All)
        {
            yield return new LibraryEntry
            {
                Code = talent.Code,
                Name = talent.Name,
                Category = talent.Keystone ? "talents/keystones" : "talents/nodes",
                Summary = talent.Description,
                Tags = talent.Keystone ? new[] { "talent", "keystone" } : new[] { "talent" },
                Fields = new[]
                {
                    new LibraryField("Cost", talent.Cost.ToString()),
                    new LibraryField("Position", talent.X + ", " + talent.Y),
                    new LibraryField("Links", string.Join(", ", talent.Links)),
                    new LibraryField("Modifiers", string.Join(", ", talent.Modifiers.Select(FormatModifier)))
                }
            };
        }
    }

    private static string FormatModifier(StatModifierDefinition modifier)
    {
        return $"{modifier.Operation} {modifier.Min:0.###}-{modifier.Max:0.###} {modifier.Stat}";
    }
}
