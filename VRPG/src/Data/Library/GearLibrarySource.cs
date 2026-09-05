using System.Collections.Generic;
using System.Linq;
using VRPG.Data.Definitions;

namespace VRPG.Data.Library;

public sealed class GearLibrarySource : ILibrarySource
{
    public string Code => "gear";

    public IEnumerable<LibraryEntry> Build(VRPGDataRegistry data)
    {
        foreach (GearBaseDefinition gearBase in data.GearBases.All)
        {
            string profile = gearBase.LevelOneWeaponPower > 0f
                ? $"{gearBase.LevelOneWeaponPower:0.###} Weapon Power"
                : $"{gearBase.LevelOneArmor:0.###} Armor";
            yield return new LibraryEntry
            {
                Code = gearBase.Code,
                Name = gearBase.Name,
                Category = "gear/bases",
                Summary = $"{gearBase.Family} — {profile}.",
                Tags = new[] { "gear", "base", gearBase.EquipmentRole, gearBase.Family }.Concat(gearBase.Tags ?? System.Array.Empty<string>()).ToArray(),
                Fields = new[]
                {
                    new LibraryField("Role", gearBase.EquipmentRole),
                    new LibraryField("Family", gearBase.Family),
                    new LibraryField("Chassis", gearBase.VisualChassis),
                    new LibraryField("Base profile", profile)
                }
            };
        }

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
                Summary = string.Join("; ", (affix.Tiers ?? System.Array.Empty<GearAffixTierDefinition>()).Select(FormatTier)),
                Tags = new[] { "gear", "affix", affix.Family, affix.MinimumRarityCode }.Concat(affix.RequiredTags ?? System.Array.Empty<string>()).ToArray(),
                Fields = new[]
                {
                    new LibraryField("Family", affix.Family),
                    new LibraryField("Minimum rarity", affix.MinimumRarityCode),
                    new LibraryField("Required tags", string.Join(", ", affix.RequiredTags ?? System.Array.Empty<string>())),
                    new LibraryField("Excluded tags", string.Join(", ", affix.ExcludedTags ?? System.Array.Empty<string>())),
                    new LibraryField("Tiers", (affix.Tiers ?? System.Array.Empty<GearAffixTierDefinition>()).Length.ToString())
                }
            };
        }
    }

    private static string FormatTier(GearAffixTierDefinition tier)
    {
        return $"Tier {tier.Tier} (Lv {tier.MinimumRequiredLevel}-{tier.MaximumRequiredLevel}): "
            + string.Join(", ", (tier.Modifiers ?? System.Array.Empty<StatModifierDefinition>()).Select(FormatModifier));
    }

    private static string FormatModifier(StatModifierDefinition modifier)
    {
        return $"{modifier.Operation} {modifier.Min:0.###}-{modifier.Max:0.###} {modifier.Stat}";
    }
}
