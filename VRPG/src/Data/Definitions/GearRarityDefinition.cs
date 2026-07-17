using VRPG.Data;

namespace VRPG.Data.Definitions;

public sealed class GearRarityDefinition : IVrpgDataRecord
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string ColorHex { get; set; } = "#ffffff";
    public int Weight { get; set; } = 100;
    public int MinAffixes { get; set; }
    public int MaxAffixes { get; set; }
    public float StatScalar { get; set; } = 1f;
    public float WeaponPowerScalar { get; set; } = 1f;
    public bool UniqueLike { get; set; }
}
