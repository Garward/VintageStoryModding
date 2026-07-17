using System.Collections.Generic;
using VRPG.Data.Definitions;

namespace VRPG.Data.Library;

public sealed class DamageTypesLibrarySource : ILibrarySource
{
    public string Code => "damage-types";

    public IEnumerable<LibraryEntry> Build(VRPGDataRegistry data)
    {
        foreach (DamageTypeDefinition damageType in data.DamageTypes.All)
        {
            yield return new LibraryEntry
            {
                Code = damageType.Code,
                Name = damageType.Name,
                Category = "stats/damage_types",
                Summary = damageType.Description,
                Tags = damageType.Tags,
                Fields = new[]
                {
                    new LibraryField("Color", damageType.ColorHex),
                    new LibraryField("Tags", string.Join(", ", damageType.Tags))
                }
            };
        }
    }
}
