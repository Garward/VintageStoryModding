using System;
using VRPG.Data;

namespace VRPG.Data.Definitions;

public sealed class DungeonEncounterDefinition : IVrpgDataRecord
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string[] Themes { get; set; } = Array.Empty<string>();
    public DungeonEncounterVariantDefinition[] Variants { get; set; } = Array.Empty<DungeonEncounterVariantDefinition>();
}

public sealed class DungeonEncounterVariantDefinition
{
    public string Id { get; set; } = "";
    public int Weight { get; set; } = 100;
    public DungeonEncounterWaveDefinition[] Waves { get; set; } = Array.Empty<DungeonEncounterWaveDefinition>();
}

public sealed class DungeonEncounterWaveDefinition
{
    public string Id { get; set; } = "";
    public string StartMode { get; set; } = "immediate";
    public float DelaySeconds { get; set; }
    public DungeonEncounterSpawnDefinition[] Spawns { get; set; } = Array.Empty<DungeonEncounterSpawnDefinition>();
}

public sealed class DungeonEncounterSpawnDefinition
{
    public string CreatureCode { get; set; } = "";
    public int Count { get; set; } = 1;
    public int LevelOffset { get; set; }
    public string Rarity { get; set; } = "normal";
    public string[] AnchorTags { get; set; } = Array.Empty<string>();
    public int PerAnchorCap { get; set; } = 1;
    public int ConcurrentCap { get; set; } = 8;
    public float MinimumPlayerDistance { get; set; } = 6f;
    public bool AvoidPlayerLineOfSight { get; set; }
}
