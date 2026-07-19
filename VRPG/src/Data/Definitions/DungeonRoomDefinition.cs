using System;
using VRPG.Data;

namespace VRPG.Data.Definitions;

public sealed class DungeonRoomDefinition : IVrpgDataRecord
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Schematic { get; set; } = "";
    public string[] Themes { get; set; } = Array.Empty<string>();
    public string[] Roles { get; set; } = Array.Empty<string>();
    public int Weight { get; set; } = 100;
    public DungeonRoomFootprintDefinition Footprint { get; set; } = new DungeonRoomFootprintDefinition();
    public int[] AllowedRotations { get; set; } = new[] { 0, 90, 180, 270 };
    public float CompletionWeight { get; set; } = 1f;
    public bool CountsForBossUnlock { get; set; } = true;
    public DungeonConnectorDefinition[] Connectors { get; set; } = Array.Empty<DungeonConnectorDefinition>();
    public DungeonZoneDefinition[] Zones { get; set; } = Array.Empty<DungeonZoneDefinition>();
    public DungeonAnchorDefinition[] Anchors { get; set; } = Array.Empty<DungeonAnchorDefinition>();

    public bool HasRole(string role)
    {
        foreach (string candidate in Roles)
        {
            if (string.Equals(candidate, role, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}

public sealed class DungeonRoomFootprintDefinition
{
    public int WidthCells { get; set; } = 1;
    public int LengthCells { get; set; } = 1;
}

public sealed class DungeonConnectorDefinition
{
    public string Id { get; set; } = "";
    public string Side { get; set; } = "north";
    public string Socket { get; set; } = "vrpg:standard";
    public int EdgeCell { get; set; }
    public int Offset { get; set; } = 16;
    public int FloorOffset { get; set; } = 1;
    public int Width { get; set; } = 6;
    public int Height { get; set; } = 5;
    public string[] AllowRoomCodes { get; set; } = Array.Empty<string>();
    public string[] DenyRoomCodes { get; set; } = Array.Empty<string>();
    public string[] AllowConnectorIds { get; set; } = Array.Empty<string>();
    public string[] DenyConnectorIds { get; set; } = Array.Empty<string>();
}

public sealed class DungeonZoneDefinition
{
    public string Id { get; set; } = "";
    public string Kind { get; set; } = "discovery";
    public int[] Min { get; set; } = new int[3];
    public int[] Max { get; set; } = new int[3];
    public string EncounterPool { get; set; } = "";
    public bool RequiredForCompletion { get; set; }
}

public sealed class DungeonAnchorDefinition
{
    public string Id { get; set; } = "";
    public string Kind { get; set; } = "mob-spawn";
    public int[] Position { get; set; } = new int[3];
    public string Facing { get; set; } = "north";
    public string[] Tags { get; set; } = Array.Empty<string>();
}
