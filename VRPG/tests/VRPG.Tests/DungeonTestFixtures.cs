using System;
using VRPG.Data.Definitions;

namespace VRPG.Tests;

internal static class DungeonTestFixtures
{
    public static DungeonThemeDefinition Theme()
    {
        return new DungeonThemeDefinition
        {
            Code = "vrpg:test_theme",
            Name = "Test Theme",
            CellSize = 32,
            FloorY = 32,
            RoomHeight = 16,
            StandardDoorWidth = 6,
            StandardDoorHeight = 5,
            TargetPlacements = new DungeonPlacementRangeDefinition { Min = 9, Max = 12 },
            MinimumBossDistance = 5,
            LoopChance = 0.25f,
            BossUnlockPercent = 0.8f,
            GenerationRadiusCells = 12,
            RoomThemes = new[] { "vrpg:test_family" },
            EncounterPools = new[] { "vrpg:test_normal" },
            BossEncounter = "vrpg:test_boss"
        };
    }

    public static DungeonRoomDefinition[] Rooms()
    {
        return new[]
        {
            Room("vrpg:test_start", new[] { "start" }, false, Connector("north", "north")),
            Room(
                "vrpg:test_junction",
                new[] { "normal", "junction" },
                true,
                Connector("north", "north"),
                Connector("east", "east"),
                Connector("south", "south"),
                Connector("west", "west")),
            Room(
                "vrpg:test_combat",
                new[] { "normal", "combat" },
                true,
                Connector("north", "north"),
                Connector("south", "south")),
            Room("vrpg:test_boss_room", new[] { "boss" }, false, Connector("south", "south"))
        };
    }

    public static DungeonEncounterDefinition[] Encounters()
    {
        return new[]
        {
            Encounter("vrpg:test_normal"),
            Encounter("vrpg:test_boss")
        };
    }

    public static DungeonRoomDefinition LongRoom()
    {
        return new DungeonRoomDefinition
        {
            Code = "vrpg:long_room",
            Name = "Long Room",
            Schematic = "vrpg:test/long.json",
            Themes = new[] { "vrpg:test_family" },
            Roles = new[] { "normal" },
            Footprint = new DungeonRoomFootprintDefinition { WidthCells = 1, LengthCells = 2 },
            AllowedRotations = new[] { 0, 90, 180, 270 },
            Connectors = new[]
            {
                Connector("north", "north"),
                Connector("south", "south")
            }
        };
    }

    private static DungeonRoomDefinition Room(
        string code,
        string[] roles,
        bool countsForBossUnlock,
        params DungeonConnectorDefinition[] connectors)
    {
        return new DungeonRoomDefinition
        {
            Code = code,
            Name = code,
            Schematic = "vrpg:test/" + code[(code.IndexOf(':') + 1)..] + ".json",
            Themes = new[] { "vrpg:test_family" },
            Roles = roles,
            Weight = roles.Length > 1 && roles[1] == "junction" ? 80 : 100,
            Footprint = new DungeonRoomFootprintDefinition { WidthCells = 1, LengthCells = 1 },
            AllowedRotations = new[] { 0, 90, 180, 270 },
            CompletionWeight = countsForBossUnlock ? 1f : 0f,
            CountsForBossUnlock = countsForBossUnlock,
            Connectors = connectors,
            Zones = Array.Empty<DungeonZoneDefinition>(),
            Anchors = Array.Empty<DungeonAnchorDefinition>()
        };
    }

    private static DungeonConnectorDefinition Connector(string id, string side)
    {
        return new DungeonConnectorDefinition
        {
            Id = id,
            Side = side,
            Socket = "vrpg:standard",
            EdgeCell = 0,
            Offset = 16,
            FloorOffset = 1,
            Width = 6,
            Height = 5
        };
    }

    private static DungeonEncounterDefinition Encounter(string code)
    {
        return new DungeonEncounterDefinition
        {
            Code = code,
            Name = code,
            Themes = new[] { "vrpg:test_family" },
            Variants = new[]
            {
                new DungeonEncounterVariantDefinition
                {
                    Id = "default",
                    Weight = 100,
                    Waves = new[]
                    {
                        new DungeonEncounterWaveDefinition
                        {
                            Id = "wave",
                            Spawns = new[]
                            {
                                new DungeonEncounterSpawnDefinition
                                {
                                    CreatureCode = "game:drifter-normal",
                                    Count = 1,
                                    PerAnchorCap = 1,
                                    ConcurrentCap = 1
                                }
                            }
                        }
                    }
                }
            }
        };
    }
}
