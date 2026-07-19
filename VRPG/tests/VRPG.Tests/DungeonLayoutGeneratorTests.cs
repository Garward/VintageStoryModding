using System.Collections.Generic;
using System.Linq;
using System.Text;
using VRPG.Data.Definitions;
using VRPG.Modules.Dungeons.Layout;
using Xunit;

namespace VRPG.Tests;

public sealed class DungeonLayoutGeneratorTests
{
    [Fact]
    public void RotatedLongRoomTransformsFootprintAndConnectors()
    {
        DungeonRoomDefinition room = DungeonTestFixtures.LongRoom();
        var placement = new DungeonRoomPlacement(0, room, 10, 20, 90);

        Assert.Equal((2, 1), DungeonGeometry.RotatedDimensions(room, 90));
        Assert.Equal(
            new[] { new DungeonGridCell(10, 20), new DungeonGridCell(11, 20) },
            DungeonGeometry.OccupiedCells(placement).ToArray());

        DungeonConnectorGeometry north = DungeonGeometry.ConnectorGeometry(placement, room.Connectors[0]);
        DungeonConnectorGeometry south = DungeonGeometry.ConnectorGeometry(placement, room.Connectors[1]);
        Assert.Equal(DungeonSide.East, north.Side);
        Assert.Equal(new DungeonGridCell(11, 20), north.BoundaryCell);
        Assert.Equal(DungeonSide.West, south.Side);
        Assert.Equal(new DungeonGridCell(10, 20), south.BoundaryCell);

        DungeonBlockPoint point = DungeonGeometry.TransformPoint(
            placement,
            new[] { 0, 3, 0 },
            32,
            32);
        Assert.Equal(new DungeonBlockPoint(383, 35, 640), point);

        DungeonBlockBounds bounds = DungeonGeometry.TransformBounds(
            placement,
            new[] { 0, 1, 0 },
            new[] { 31, 8, 63 },
            32,
            32);
        Assert.Equal(new DungeonBlockPoint(320, 33, 640), bounds.Min);
        Assert.Equal(new DungeonBlockPoint(383, 40, 671), bounds.Max);
    }

    [Fact]
    public void FixedSeedProducesIdenticalLayout()
    {
        var generator = new DungeonLayoutGenerator();
        DungeonThemeDefinition theme = DungeonTestFixtures.Theme();
        DungeonRoomDefinition[] rooms = DungeonTestFixtures.Rooms();

        DungeonLayout first = generator.Generate(theme, rooms, 8675309UL);
        DungeonLayout second = generator.Generate(theme, rooms, 8675309UL);

        Assert.Equal(Signature(first), Signature(second));
    }

    [Fact]
    public void ConnectorRestrictionsAreBidirectional()
    {
        DungeonRoomDefinition[] rooms = DungeonTestFixtures.Rooms();
        DungeonConnectorDefinition first = rooms[1].Connectors[0];
        DungeonConnectorDefinition second = rooms[2].Connectors[1];

        Assert.True(DungeonGeometry.ConnectorsAccept(rooms[1], first, rooms[2], second));

        second.DenyRoomCodes = new[] { rooms[1].Code };
        Assert.False(DungeonGeometry.ConnectorsAccept(rooms[1], first, rooms[2], second));
    }

    [Fact]
    public void FourPiecePoolProducesValidLayoutsAcrossTwoThousandSeeds()
    {
        var generator = new DungeonLayoutGenerator();
        DungeonThemeDefinition theme = DungeonTestFixtures.Theme();
        DungeonRoomDefinition[] rooms = DungeonTestFixtures.Rooms();

        for (ulong seed = 1; seed <= 2000; seed++)
        {
            DungeonLayout layout = generator.Generate(theme, rooms, seed);
            Assert.InRange(layout.Placements.Count, theme.TargetPlacements.Min, theme.TargetPlacements.Max);
            Assert.Empty(DungeonLayoutValidator.CollectErrors(theme, layout));
        }
    }

    private static string Signature(DungeonLayout layout)
    {
        var result = new StringBuilder();
        foreach (DungeonRoomPlacement placement in layout.Placements)
        {
            result.Append(placement.Id).Append(':')
                .Append(placement.Room.Code).Append('@')
                .Append(placement.CellX).Append(',').Append(placement.CellZ).Append(',')
                .Append(placement.Rotation).Append('|');
        }

        foreach (DungeonConnectorJoin join in layout.Joins)
        {
            result.Append(join.First.PlacementId).Append('/').Append(join.First.ConnectorId)
                .Append('-').Append(join.Second.PlacementId).Append('/').Append(join.Second.ConnectorId)
                .Append(join.IsLoop ? 'L' : 'T').Append('|');
        }

        return result.ToString();
    }
}
