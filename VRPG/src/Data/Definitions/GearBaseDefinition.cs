using System;
using VRPG.Data;

namespace VRPG.Data.Definitions;

/// <summary>Defines one permanent mechanical gear identity and its visual chassis.</summary>
public sealed class GearBaseDefinition : IVrpgDataRecord
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string EquipmentRole { get; set; } = "";
    public string Family { get; set; } = "";
    public string[] Tags { get; set; } = Array.Empty<string>();
    public string[] AffixFamilies { get; set; } = Array.Empty<string>();
    public string VisualChassis { get; set; } = "";
    public float LevelOneWeaponPower { get; set; }
    public float LevelOneArmor { get; set; }
    public StatModifierDefinition[] Implicits { get; set; } = Array.Empty<StatModifierDefinition>();
    public string UniqueCode { get; set; } = "";
}
