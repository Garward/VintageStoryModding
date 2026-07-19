using System;
using System.Collections.Generic;
using VRPG.Data.Definitions;

namespace VRPG.Modules.Dungeons.Layout;

public readonly record struct DungeonConnectorGeometry(
    DungeonGridCell BoundaryCell,
    DungeonGridCell OutsideCell,
    DungeonSide Side);

public readonly record struct DungeonBlockPoint(int X, int Y, int Z);

public readonly record struct DungeonBlockBounds(DungeonBlockPoint Min, DungeonBlockPoint Max);

public static class DungeonGeometry
{
    public static int NormalizeRotation(int rotation)
    {
        int normalized = rotation % 360;
        return normalized < 0 ? normalized + 360 : normalized;
    }

    public static DungeonSide ParseSide(string side)
    {
        return side?.Trim().ToLowerInvariant() switch
        {
            "north" => DungeonSide.North,
            "east" => DungeonSide.East,
            "south" => DungeonSide.South,
            "west" => DungeonSide.West,
            _ => throw new ArgumentException($"Unknown dungeon side '{side}'.", nameof(side))
        };
    }

    public static DungeonSide Opposite(DungeonSide side)
    {
        return side switch
        {
            DungeonSide.North => DungeonSide.South,
            DungeonSide.East => DungeonSide.West,
            DungeonSide.South => DungeonSide.North,
            DungeonSide.West => DungeonSide.East,
            _ => throw new ArgumentOutOfRangeException(nameof(side))
        };
    }

    public static (int Width, int Length) RotatedDimensions(DungeonRoomDefinition room, int rotation)
    {
        int normalized = NormalizeRotation(rotation);
        if (normalized != 0 && normalized != 90 && normalized != 180 && normalized != 270)
        {
            throw new ArgumentException("Dungeon room rotations must be a quarter turn.", nameof(rotation));
        }

        return normalized == 90 || normalized == 270
            ? (room.Footprint.LengthCells, room.Footprint.WidthCells)
            : (room.Footprint.WidthCells, room.Footprint.LengthCells);
    }

    public static IEnumerable<DungeonGridCell> OccupiedCells(DungeonRoomPlacement placement)
    {
        (int width, int length) = RotatedDimensions(placement.Room, placement.Rotation);
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < length; z++)
            {
                yield return new DungeonGridCell(placement.CellX + x, placement.CellZ + z);
            }
        }
    }

    public static DungeonConnectorGeometry ConnectorGeometry(
        DungeonRoomPlacement placement,
        DungeonConnectorDefinition connector)
    {
        int width = placement.Room.Footprint.WidthCells;
        int length = placement.Room.Footprint.LengthCells;
        DungeonSide originalSide = ParseSide(connector.Side);
        DungeonGridCell canonical = originalSide switch
        {
            DungeonSide.North => new DungeonGridCell(connector.EdgeCell, 0),
            DungeonSide.East => new DungeonGridCell(width - 1, connector.EdgeCell),
            DungeonSide.South => new DungeonGridCell(connector.EdgeCell, length - 1),
            DungeonSide.West => new DungeonGridCell(0, connector.EdgeCell),
            _ => throw new ArgumentOutOfRangeException()
        };

        DungeonGridCell rotated = RotateCell(canonical, width, length, placement.Rotation);
        DungeonSide side = RotateSide(originalSide, placement.Rotation);
        var boundary = new DungeonGridCell(placement.CellX + rotated.X, placement.CellZ + rotated.Z);
        DungeonGridCell step = SideStep(side);
        var outside = new DungeonGridCell(boundary.X + step.X, boundary.Z + step.Z);
        return new DungeonConnectorGeometry(boundary, outside, side);
    }

    public static bool ConnectorsAccept(
        DungeonRoomDefinition firstRoom,
        DungeonConnectorDefinition first,
        DungeonRoomDefinition secondRoom,
        DungeonConnectorDefinition second)
    {
        return string.Equals(first.Socket, second.Socket, StringComparison.OrdinalIgnoreCase)
            && first.Width == second.Width
            && first.Height == second.Height
            && first.FloorOffset == second.FloorOffset
            && first.Offset == second.Offset
            && RestrictionAccepts(first, secondRoom.Code, second.Id)
            && RestrictionAccepts(second, firstRoom.Code, first.Id);
    }

    public static DungeonBlockPoint TransformPoint(
        DungeonRoomPlacement placement,
        int[] point,
        int cellSize,
        int floorY)
    {
        if (point == null || point.Length != 3)
        {
            throw new ArgumentException("Dungeon points require exactly three coordinates.", nameof(point));
        }

        int width = placement.Room.Footprint.WidthCells * cellSize;
        int length = placement.Room.Footprint.LengthCells * cellSize;
        DungeonGridCell rotated = RotateCell(
            new DungeonGridCell(point[0], point[2]),
            width,
            length,
            placement.Rotation);
        return new DungeonBlockPoint(
            placement.CellX * cellSize + rotated.X,
            floorY + point[1],
            placement.CellZ * cellSize + rotated.Z);
    }

    public static DungeonBlockBounds TransformBounds(
        DungeonRoomPlacement placement,
        int[] min,
        int[] max,
        int cellSize,
        int floorY)
    {
        DungeonBlockPoint[] corners =
        {
            TransformPoint(placement, new[] { min[0], min[1], min[2] }, cellSize, floorY),
            TransformPoint(placement, new[] { min[0], min[1], max[2] }, cellSize, floorY),
            TransformPoint(placement, new[] { max[0], min[1], min[2] }, cellSize, floorY),
            TransformPoint(placement, new[] { max[0], min[1], max[2] }, cellSize, floorY)
        };
        int minX = corners[0].X;
        int minZ = corners[0].Z;
        int maxX = corners[0].X;
        int maxZ = corners[0].Z;
        foreach (DungeonBlockPoint corner in corners)
        {
            minX = Math.Min(minX, corner.X);
            minZ = Math.Min(minZ, corner.Z);
            maxX = Math.Max(maxX, corner.X);
            maxZ = Math.Max(maxZ, corner.Z);
        }

        return new DungeonBlockBounds(
            new DungeonBlockPoint(minX, floorY + min[1], minZ),
            new DungeonBlockPoint(maxX, floorY + max[1], maxZ));
    }

    private static DungeonGridCell RotateCell(DungeonGridCell cell, int width, int length, int rotation)
    {
        return NormalizeRotation(rotation) switch
        {
            0 => cell,
            90 => new DungeonGridCell(length - 1 - cell.Z, cell.X),
            180 => new DungeonGridCell(width - 1 - cell.X, length - 1 - cell.Z),
            270 => new DungeonGridCell(cell.Z, width - 1 - cell.X),
            _ => throw new ArgumentException("Dungeon room rotations must be a quarter turn.", nameof(rotation))
        };
    }

    private static DungeonSide RotateSide(DungeonSide side, int rotation)
    {
        int turns = NormalizeRotation(rotation) / 90;
        return (DungeonSide)(((int)side + turns) % 4);
    }

    private static DungeonGridCell SideStep(DungeonSide side)
    {
        return side switch
        {
            DungeonSide.North => new DungeonGridCell(0, -1),
            DungeonSide.East => new DungeonGridCell(1, 0),
            DungeonSide.South => new DungeonGridCell(0, 1),
            DungeonSide.West => new DungeonGridCell(-1, 0),
            _ => throw new ArgumentOutOfRangeException(nameof(side))
        };
    }

    private static bool RestrictionAccepts(
        DungeonConnectorDefinition connector,
        string otherRoomCode,
        string otherConnectorId)
    {
        if (Contains(connector.DenyRoomCodes, otherRoomCode)
            || Contains(connector.DenyConnectorIds, otherConnectorId))
        {
            return false;
        }

        bool roomAllowed = connector.AllowRoomCodes == null
            || connector.AllowRoomCodes.Length == 0
            || Contains(connector.AllowRoomCodes, otherRoomCode);
        bool connectorAllowed = connector.AllowConnectorIds == null
            || connector.AllowConnectorIds.Length == 0
            || Contains(connector.AllowConnectorIds, otherConnectorId);
        return roomAllowed && connectorAllowed;
    }

    private static bool Contains(IEnumerable<string> values, string wanted)
    {
        foreach (string value in values ?? Array.Empty<string>())
        {
            if (string.Equals(value, wanted, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
