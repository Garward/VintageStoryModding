using VRPG.Data;

namespace VRPG.Data.Definitions;

public sealed class DamageTypeDefinition : IVrpgDataRecord
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string ColorHex { get; set; } = "#ffffff";
    public string[] Tags { get; set; } = System.Array.Empty<string>();
}
