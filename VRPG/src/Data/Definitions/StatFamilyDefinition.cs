using VRPG.Data;

namespace VRPG.Data.Definitions;

public sealed class StatFamilyDefinition : IVrpgDataRecord
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string SourceReference { get; set; } = "";
    public string ConversionNote { get; set; } = "";
    public StatAxisDefinition[] Axes { get; set; } = System.Array.Empty<StatAxisDefinition>();
    public StatFamilyEntryDefinition[] Families { get; set; } = System.Array.Empty<StatFamilyEntryDefinition>();
}

public sealed class StatAxisDefinition
{
    public string Code { get; set; } = "";
    public string[] Values { get; set; } = System.Array.Empty<string>();
}

public sealed class StatFamilyEntryDefinition
{
    public string Code { get; set; } = "";
    public string NamePattern { get; set; } = "";
    public string Category { get; set; } = "";
    public string[] Axes { get; set; } = System.Array.Empty<string>();
    public string Operation { get; set; } = "add";
    public string Layer { get; set; } = "";
    public bool Percent { get; set; }
    public float BaseValue { get; set; }
    public float MinValue { get; set; }
    public float MaxValue { get; set; } = 999999f;
    public string AppliesTo { get; set; } = "";
    public string Conversion { get; set; } = "";
    public string Notes { get; set; } = "";
}
