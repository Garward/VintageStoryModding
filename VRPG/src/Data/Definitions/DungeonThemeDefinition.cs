using VRPG.Data;

namespace VRPG.Data.Definitions;

public sealed class DungeonThemeDefinition : IVrpgDataRecord
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Summary { get; set; } = "";
    public string[] Tags { get; set; } = System.Array.Empty<string>();
    public string FloorBlock { get; set; } = "game:rock-granite";
    public string WallBlock { get; set; } = "game:rock-andesite";
    public string AccentBlock { get; set; } = "game:rock-basalt";
    public int FloorY { get; set; } = 24;
    public int RoomHeight { get; set; } = 7;
    public int DoorWidth { get; set; } = 6;
}
