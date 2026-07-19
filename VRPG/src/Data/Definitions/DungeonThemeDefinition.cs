using System;
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
    public int CellSize { get; set; } = 32;
    public int FloorY { get; set; } = 32;
    public int RoomHeight { get; set; } = 16;
    public int StandardDoorWidth { get; set; } = 6;
    public int StandardDoorHeight { get; set; } = 5;
    public DungeonPlacementRangeDefinition TargetPlacements { get; set; } = new DungeonPlacementRangeDefinition();
    public int MinimumBossDistance { get; set; } = 5;
    public float LoopChance { get; set; } = 0.25f;
    public float BossUnlockPercent { get; set; } = 0.8f;
    public int GenerationRadiusCells { get; set; } = 12;
    public string[] RoomThemes { get; set; } = Array.Empty<string>();
    public string[] EncounterPools { get; set; } = Array.Empty<string>();
    public string BossEncounter { get; set; } = "";

    // Retained while the old proof-of-concept column generator still exists.
    public int DoorWidth { get; set; } = 6;
}

public sealed class DungeonPlacementRangeDefinition
{
    public int Min { get; set; } = 9;
    public int Max { get; set; } = 12;
}
