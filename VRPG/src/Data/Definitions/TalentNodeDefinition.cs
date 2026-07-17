using VRPG.Data;

namespace VRPG.Data.Definitions;

public sealed class TalentNodeDefinition : IVrpgDataRecord
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    public string[] Links { get; set; } = System.Array.Empty<string>();
    public StatModifierDefinition[] Modifiers { get; set; } = System.Array.Empty<StatModifierDefinition>();
    public int Cost { get; set; } = 1;
    public bool Keystone { get; set; }
    public bool Starter { get; set; }
    public string Foundation { get; set; } = "";
    public string VisualTier { get; set; } = "normal";
}
