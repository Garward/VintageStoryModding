using VRPG.Data;

namespace VRPG.Data.Definitions;

public sealed class GearAffixDefinition : IVrpgDataRecord
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string SlotTag { get; set; } = "any";
    public string Rarity { get; set; } = "common";
    public StatModifierDefinition[] Modifiers { get; set; } = System.Array.Empty<StatModifierDefinition>();
}

public sealed class StatModifierDefinition
{
    public string Stat { get; set; } = "";
    public float Min { get; set; }
    public float Max { get; set; }
    public string Operation { get; set; } = "add";
}
