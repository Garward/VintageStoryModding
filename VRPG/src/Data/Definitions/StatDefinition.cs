using VRPG.Data;

namespace VRPG.Data.Definitions;

public sealed class StatDefinition : IVrpgDataRecord
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Category { get; set; } = "core";
    public float BaseValue { get; set; }
    public float MinValue { get; set; }
    public float MaxValue { get; set; } = 999999f;
    public bool Percent { get; set; }
}
