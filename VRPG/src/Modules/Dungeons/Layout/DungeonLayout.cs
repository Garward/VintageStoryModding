using System;
using System.Collections.Generic;
using VRPG.Data.Definitions;

namespace VRPG.Modules.Dungeons.Layout;

public readonly record struct DungeonGridCell(int X, int Z);

public enum DungeonSide
{
    North,
    East,
    South,
    West
}

public sealed class DungeonRoomPlacement
{
    public DungeonRoomPlacement(int id, DungeonRoomDefinition room, int cellX, int cellZ, int rotation)
    {
        Id = id;
        Room = room;
        CellX = cellX;
        CellZ = cellZ;
        Rotation = DungeonGeometry.NormalizeRotation(rotation);
    }

    public int Id { get; }
    public DungeonRoomDefinition Room { get; }
    public int CellX { get; }
    public int CellZ { get; }
    public int Rotation { get; }
}

public readonly record struct DungeonConnectorEndpoint(int PlacementId, string ConnectorId);

public sealed class DungeonConnectorJoin
{
    public DungeonConnectorJoin(DungeonConnectorEndpoint first, DungeonConnectorEndpoint second, bool isLoop)
    {
        First = first;
        Second = second;
        IsLoop = isLoop;
    }

    public DungeonConnectorEndpoint First { get; }
    public DungeonConnectorEndpoint Second { get; }
    public bool IsLoop { get; }
}

public sealed record DungeonPlacedZone(
    int PlacementId,
    DungeonZoneDefinition Definition,
    DungeonBlockBounds Bounds);

public sealed record DungeonPlacedAnchor(
    int PlacementId,
    DungeonAnchorDefinition Definition,
    DungeonBlockPoint Position);

public sealed class DungeonLayout
{
    public DungeonLayout(
        string themeCode,
        ulong seed,
        int cellSize,
        int floorY,
        IReadOnlyList<DungeonRoomPlacement> placements,
        IReadOnlyList<DungeonConnectorJoin> joins)
    {
        ThemeCode = themeCode;
        Seed = seed;
        Placements = placements;
        Joins = joins;
        Zones = BuildZones(placements, cellSize, floorY);
        Anchors = BuildAnchors(placements, cellSize, floorY);
    }

    public string ThemeCode { get; }
    public ulong Seed { get; }
    public IReadOnlyList<DungeonRoomPlacement> Placements { get; }
    public IReadOnlyList<DungeonConnectorJoin> Joins { get; }
    public IReadOnlyList<DungeonPlacedZone> Zones { get; }
    public IReadOnlyList<DungeonPlacedAnchor> Anchors { get; }

    public DungeonRoomPlacement Start => FindRole("start");
    public DungeonRoomPlacement Boss => FindRole("boss");

    private DungeonRoomPlacement FindRole(string role)
    {
        foreach (DungeonRoomPlacement placement in Placements)
        {
            if (placement.Room.HasRole(role))
            {
                return placement;
            }
        }

        throw new InvalidOperationException($"Layout has no {role} placement.");
    }

    private static IReadOnlyList<DungeonPlacedZone> BuildZones(
        IReadOnlyList<DungeonRoomPlacement> placements,
        int cellSize,
        int floorY)
    {
        var result = new List<DungeonPlacedZone>();
        foreach (DungeonRoomPlacement placement in placements)
        {
            foreach (DungeonZoneDefinition zone in placement.Room.Zones)
            {
                result.Add(new DungeonPlacedZone(
                    placement.Id,
                    zone,
                    DungeonGeometry.TransformBounds(placement, zone.Min, zone.Max, cellSize, floorY)));
            }
        }

        return result.ToArray();
    }

    private static IReadOnlyList<DungeonPlacedAnchor> BuildAnchors(
        IReadOnlyList<DungeonRoomPlacement> placements,
        int cellSize,
        int floorY)
    {
        var result = new List<DungeonPlacedAnchor>();
        foreach (DungeonRoomPlacement placement in placements)
        {
            foreach (DungeonAnchorDefinition anchor in placement.Room.Anchors)
            {
                result.Add(new DungeonPlacedAnchor(
                    placement.Id,
                    anchor,
                    DungeonGeometry.TransformPoint(placement, anchor.Position, cellSize, floorY)));
            }
        }

        return result.ToArray();
    }
}

public sealed class DungeonLayoutGenerationException : InvalidOperationException
{
    public DungeonLayoutGenerationException(string message) : base(message)
    {
    }
}
