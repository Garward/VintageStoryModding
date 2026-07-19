using System.Linq;
using VRPG.Data;
using VRPG.Data.Definitions;
using Xunit;

namespace VRPG.Tests;

public sealed class DungeonContentValidatorTests
{
    [Fact]
    public void FourPiecePoolPassesSchemaValidation()
    {
        var errors = DungeonContentValidator.CollectErrors(
            new[] { DungeonTestFixtures.Theme() },
            DungeonTestFixtures.Rooms(),
            DungeonTestFixtures.Encounters());

        Assert.Empty(errors);
    }

    [Fact]
    public void DuplicateConnectorIdsFailLoudly()
    {
        DungeonRoomDefinition[] rooms = DungeonTestFixtures.Rooms();
        rooms[2].Connectors[1].Id = rooms[2].Connectors[0].Id;

        var errors = DungeonContentValidator.CollectErrors(
            new[] { DungeonTestFixtures.Theme() },
            rooms,
            DungeonTestFixtures.Encounters());

        Assert.Contains(errors, error => error.Contains("connector IDs", System.StringComparison.Ordinal));
    }

    [Fact]
    public void MissingEncounterReferenceFailsLoudly()
    {
        DungeonThemeDefinition theme = DungeonTestFixtures.Theme();
        theme.EncounterPools = new[] { "vrpg:missing" };

        var errors = DungeonContentValidator.CollectErrors(
            new[] { theme },
            DungeonTestFixtures.Rooms(),
            DungeonTestFixtures.Encounters());

        Assert.Contains(errors, error => error.Contains("unknown encounter pool", System.StringComparison.Ordinal));
    }

    [Fact]
    public void RequiredEncounterRejectsInsufficientSpawnAnchors()
    {
        DungeonRoomDefinition[] rooms = DungeonTestFixtures.Rooms();
        rooms[2].Zones = new[]
        {
            new DungeonZoneDefinition
            {
                Id = "fight",
                Kind = "encounter",
                Min = new[] { 1, 1, 1 },
                Max = new[] { 30, 12, 30 },
                EncounterPool = "vrpg:test_normal",
                RequiredForCompletion = true
            }
        };

        var errors = DungeonContentValidator.CollectErrors(
            new[] { DungeonTestFixtures.Theme() },
            rooms,
            DungeonTestFixtures.Encounters());

        Assert.Contains(errors, error => error.Contains("compatible anchors", System.StringComparison.Ordinal));
    }
}
