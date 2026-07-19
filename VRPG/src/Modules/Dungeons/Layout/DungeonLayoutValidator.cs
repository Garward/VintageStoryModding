using System;
using System.Collections.Generic;
using VRPG.Data.Definitions;

namespace VRPG.Modules.Dungeons.Layout;

public static class DungeonLayoutValidator
{
    public static void ValidateOrThrow(DungeonThemeDefinition theme, DungeonLayout layout)
    {
        IReadOnlyList<string> errors = CollectErrors(theme, layout);
        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                $"Dungeon layout {theme.Code}/{layout.Seed} is invalid:\n- " + string.Join("\n- ", errors));
        }
    }

    public static IReadOnlyList<string> CollectErrors(DungeonThemeDefinition theme, DungeonLayout layout)
    {
        var errors = new List<string>();
        var byId = new Dictionary<int, DungeonRoomPlacement>();
        var occupied = new Dictionary<DungeonGridCell, int>();
        int startCount = 0;
        int bossCount = 0;

        foreach (DungeonRoomPlacement placement in layout.Placements)
        {
            if (!byId.TryAdd(placement.Id, placement))
            {
                errors.Add($"duplicate placement ID {placement.Id}");
            }

            if (placement.Room.HasRole("start"))
            {
                startCount++;
            }

            if (placement.Room.HasRole("boss"))
            {
                bossCount++;
            }

            foreach (DungeonGridCell cell in DungeonGeometry.OccupiedCells(placement))
            {
                if (!occupied.TryAdd(cell, placement.Id))
                {
                    errors.Add($"placements {occupied[cell]} and {placement.Id} overlap at ({cell.X},{cell.Z})");
                }

                if (Math.Abs(cell.X) > theme.GenerationRadiusCells || Math.Abs(cell.Z) > theme.GenerationRadiusCells)
                {
                    errors.Add($"placement {placement.Id} exceeds generation radius at ({cell.X},{cell.Z})");
                }
            }
        }

        if (startCount != 1 || bossCount != 1)
        {
            errors.Add($"expected exactly one start and boss, found {startCount} start and {bossCount} boss rooms");
        }

        var adjacency = BuildAndValidateJoins(layout, byId, errors);
        if (startCount == 1)
        {
            int startId = layout.Start.Id;
            Dictionary<int, int> distances = Distances(startId, adjacency);
            if (distances.Count != layout.Placements.Count)
            {
                errors.Add($"only {distances.Count} of {layout.Placements.Count} rooms are reachable from the start");
            }

            if (bossCount == 1)
            {
                int bossId = layout.Boss.Id;
                if (!distances.TryGetValue(bossId, out int bossDistance)
                    || bossDistance < theme.MinimumBossDistance)
                {
                    errors.Add($"boss graph distance is below the required {theme.MinimumBossDistance}");
                }

                if (!adjacency.TryGetValue(bossId, out List<int>? bossEdges) || bossEdges.Count != 1)
                {
                    errors.Add("boss room must have exactly one joined connector so its gate cannot be bypassed");
                }
            }
        }

        if (layout.Placements.Count >= 6 && !HasBranchOrLoop(layout, adjacency))
        {
            errors.Add("layout has neither a branch nor a loop");
        }

        ValidateCompletionWeight(theme, layout, errors);
        return errors;
    }

    private static Dictionary<int, List<int>> BuildAndValidateJoins(
        DungeonLayout layout,
        IReadOnlyDictionary<int, DungeonRoomPlacement> placements,
        List<string> errors)
    {
        var adjacency = new Dictionary<int, List<int>>();
        var used = new HashSet<DungeonConnectorEndpoint>();
        foreach (int id in placements.Keys)
        {
            adjacency[id] = new List<int>();
        }

        foreach (DungeonConnectorJoin join in layout.Joins)
        {
            if (!placements.TryGetValue(join.First.PlacementId, out DungeonRoomPlacement? firstPlacement)
                || !placements.TryGetValue(join.Second.PlacementId, out DungeonRoomPlacement? secondPlacement))
            {
                errors.Add("join references a missing placement");
                continue;
            }

            DungeonConnectorDefinition? first = FindConnector(firstPlacement.Room, join.First.ConnectorId);
            DungeonConnectorDefinition? second = FindConnector(secondPlacement.Room, join.Second.ConnectorId);
            if (first == null || second == null)
            {
                errors.Add("join references a missing connector");
                continue;
            }

            if (!used.Add(join.First) || !used.Add(join.Second))
            {
                errors.Add("a connector participates in more than one join");
            }

            DungeonConnectorGeometry firstGeometry = DungeonGeometry.ConnectorGeometry(firstPlacement, first);
            DungeonConnectorGeometry secondGeometry = DungeonGeometry.ConnectorGeometry(secondPlacement, second);
            if (firstGeometry.OutsideCell != secondGeometry.BoundaryCell
                || secondGeometry.OutsideCell != firstGeometry.BoundaryCell
                || firstGeometry.Side != DungeonGeometry.Opposite(secondGeometry.Side)
                || !DungeonGeometry.ConnectorsAccept(firstPlacement.Room, first, secondPlacement.Room, second))
            {
                errors.Add($"join {firstPlacement.Id}/{first.Id} -> {secondPlacement.Id}/{second.Id} is geometrically or semantically incompatible");
            }

            adjacency[firstPlacement.Id].Add(secondPlacement.Id);
            adjacency[secondPlacement.Id].Add(firstPlacement.Id);
        }

        return adjacency;
    }

    private static Dictionary<int, int> Distances(int start, IReadOnlyDictionary<int, List<int>> adjacency)
    {
        var result = new Dictionary<int, int> { [start] = 0 };
        var queue = new Queue<int>();
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            int current = queue.Dequeue();
            foreach (int next in adjacency[current])
            {
                if (result.TryAdd(next, result[current] + 1))
                {
                    queue.Enqueue(next);
                }
            }
        }

        return result;
    }

    private static bool HasBranchOrLoop(DungeonLayout layout, IReadOnlyDictionary<int, List<int>> adjacency)
    {
        if (layout.Joins.Count >= layout.Placements.Count)
        {
            return true;
        }

        foreach (List<int> neighbors in adjacency.Values)
        {
            if (neighbors.Count >= 3)
            {
                return true;
            }
        }

        return false;
    }

    private static void ValidateCompletionWeight(
        DungeonThemeDefinition theme,
        DungeonLayout layout,
        List<string> errors)
    {
        float total = 0f;
        float availableBeforeBoss = 0f;
        foreach (DungeonRoomPlacement placement in layout.Placements)
        {
            if (!placement.Room.CountsForBossUnlock)
            {
                continue;
            }

            total += placement.Room.CompletionWeight;
            if (!placement.Room.HasRole("boss"))
            {
                availableBeforeBoss += placement.Room.CompletionWeight;
            }
        }

        if (total <= 0f)
        {
            errors.Add("layout has no positive boss-unlock completion weight");
            return;
        }

        if (availableBeforeBoss / total + 0.0001f < theme.BossUnlockPercent)
        {
            errors.Add($"only {availableBeforeBoss / total:P0} completion weight is available before the boss gate");
        }
    }

    private static DungeonConnectorDefinition? FindConnector(DungeonRoomDefinition room, string id)
    {
        foreach (DungeonConnectorDefinition connector in room.Connectors)
        {
            if (string.Equals(connector.Id, id, StringComparison.OrdinalIgnoreCase))
            {
                return connector;
            }
        }

        return null;
    }
}
