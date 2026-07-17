using System.Collections.Generic;
using System.Linq;
using VRPG.Data.Definitions;

namespace VRPG.Data.Library;

public sealed class GearLibrarySource : ILibrarySource
{
    public string Code => "gear";

    public IEnumerable<LibraryEntry> Build(VRPGDataRegistry data)
    {
        foreach (GearRarityDefinition rarity in data.Rarities.All)
        {
            yield return new LibraryEntry
            {
                Code = rarity.Code,
                Name = rarity.Name,
                Category = "gear/rarities",
                Summary = $"{rarity.MinAffixes}-{rarity.MaxAffixes} affixes, x{rarity.StatScalar:0.###} stat scalar.",
                Tags = rarity.UniqueLike ? new[] { "gear", "rarity", "unique" } : new[] { "gear", "rarity" },
                Fields = new[]
                {
                    new LibraryField("Weight", rarity.Weight.ToString()),
                    new LibraryField("Color", rarity.ColorHex)
                }
            };
        }

        foreach (GearAffixDefinition affix in data.Affixes.All)
        {
            yield return new LibraryEntry
            {
                Code = affix.Code,
                Name = affix.Name,
                Category = "gear/affixes",
                Summary = string.Join(", ", affix.Modifiers.Select(FormatModifier)),
                Tags = new[] { "gear", "affix", affix.SlotTag, affix.Rarity },
                Fields = new[]
                {
                    new LibraryField("Slot", affix.SlotTag),
                    new LibraryField("Rarity", affix.Rarity),
                    new LibraryField("Modifiers", affix.Modifiers.Length.ToString())
                }
            };
        }
    }

    private static string FormatModifier(StatModifierDefinition modifier)
    {
        return $"{modifier.Operation} {modifier.Min:0.###}-{modifier.Max:0.###} {modifier.Stat}";
    }
}
