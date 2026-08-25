using System;
using VRPG.Data;

namespace VRPG.Data.Definitions;

public sealed class SkillFxPresetDefinition : IVrpgDataRecord
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public SkillFxLayerDefinition[] Layers { get; set; } = Array.Empty<SkillFxLayerDefinition>();
}
