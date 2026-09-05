using VRPG.Data;

namespace VRPG.Data.Definitions;

public sealed class GearAffixDefinition : IVrpgDataRecord
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Family { get; set; } = "";
    public string[] RequiredTags { get; set; } = System.Array.Empty<string>();
    public string[] ExcludedTags { get; set; } = System.Array.Empty<string>();
    public string MutualExclusionGroup { get; set; } = "";
    public bool UniquePerItem { get; set; }
    public string MinimumRarityCode { get; set; } = "";
    public GearAffixTierDefinition[] Tiers { get; set; } = System.Array.Empty<GearAffixTierDefinition>();
}

public sealed class GearAffixTierDefinition
{
    public int Tier { get; set; }
    public int MinimumRequiredLevel { get; set; } = 1;
    public int MaximumRequiredLevel { get; set; } = 100;
    public StatModifierDefinition[] Modifiers { get; set; } = System.Array.Empty<StatModifierDefinition>();
}

public sealed class StatModifierDefinition
{
    public string Stat { get; set; } = "";
    public float Min { get; set; }
    public float Max { get; set; }
    public string Operation { get; set; } = "add";
}
