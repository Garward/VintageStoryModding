using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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
    public StarterLoadoutDefinition? Starter { get; set; }
}

/// <summary>Data-owned preview and grant contract for a first-run combat style.</summary>
public sealed class StarterLoadoutDefinition
{
    public int GrantSchemaVersion { get; set; } = 1;
    public string CombatPattern { get; set; } = "";
    public string DefensiveApproach { get; set; } = "";
    public string PrimaryAttribute { get; set; } = "";
    public StarterItemDefinition[] Items { get; set; } = Array.Empty<StarterItemDefinition>();
    public string SkillCode { get; set; } = "";

    // Preserve unknown JSON so attempted mechanical overrides can be rejected explicitly.
    [JsonExtensionData]
    public IDictionary<string, JToken>? ExtensionData { get; set; }
}

/// <summary>One exact item promised by a starter loadout.</summary>
public sealed class StarterItemDefinition
{
    public string GearBaseCode { get; set; } = "";
    public string EquipmentRole { get; set; } = "";

    [JsonExtensionData]
    public IDictionary<string, JToken>? ExtensionData { get; set; }
}
