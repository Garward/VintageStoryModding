using System;
using VRPG.Data;

namespace VRPG.Data.Definitions;

public sealed class ClassDefinition : IVrpgDataRecord
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Icon { get; set; } = "class";
    public string Color { get; set; } = "#ff9f0d";
    public string[] Tags { get; set; } = Array.Empty<string>();
    public int SortOrder { get; set; }
}
