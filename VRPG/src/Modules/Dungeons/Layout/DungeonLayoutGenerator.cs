using System;
using System.Collections.Generic;
using VRPG.Data.Definitions;

namespace VRPG.Modules.Dungeons.Layout;

public sealed class DungeonLayoutGenerator
{
    private const int MaximumAttempts = 48;
    private const int MaximumSearchSteps = 200000;

    public DungeonLayout Generate(
        DungeonThemeDefinition theme,
        IReadOnlyCollection<DungeonRoomDefinition> availableRooms,
        ulong seed)
    {
        var rooms = new List<DungeonRoomDefinition>();
        foreach (DungeonRoomDefinition room in availableRooms)
        {
            if (SharesTheme(room.Themes, theme.RoomThemes))
            {
                rooms.Add(room);
            }
        }

        var starts = rooms.FindAll(room => room.HasRole("start"));
        var ordinary = rooms.FindAll(room => !room.HasRole("start") && !room.HasRole("boss"));
        var bosses = rooms.FindAll(room => room.HasRole("boss"));
        if (starts.Count == 0 || ordinary.Count == 0 || bosses.Count == 0)
        {
            throw new DungeonLayoutGenerationException(
                $"Theme {theme.Code}, seed {seed}: room pool needs start, ordinary, and boss pieces.");
        }

        string lastFailure = "search exhausted";
        int lastAttempt = 0;
        for (int attempt = 0; attempt < MaximumAttempts; attempt++)
        {
            lastAttempt = attempt;
            ulong attemptSeed = MixSeed(seed, (ulong)attempt);
            var random = new DungeonRandom(attemptSeed);
            int target = theme.TargetPlacements.Min
                + random.Next(theme.TargetPlacements.Max - theme.TargetPlacements.Min + 1);
            var state = new SearchState(theme, random, MaximumSearchSteps);
            DungeonRoomDefinition start = WeightedChoice(starts, random);
            int startRotation = start.AllowedRotations[random.Next(start.AllowedRotations.Length)];
            if (!state.TryAddInitial(start, startRotation))
            {
                lastFailure = "start footprint exceeded generation bounds";
                continue;
            }

            if (!TryBuildOrdinary(state, ordinary, bosses, target))
            {
                lastFailure = state.Failure;
                continue;
            }

            state.AddCompatibleLoops();
            DungeonLayout layout = state.ToLayout(seed);
            IReadOnlyList<string> topologyErrors = DungeonLayoutValidator.CollectErrors(theme, layout);
            if (topologyErrors.Count == 0)
            {
                return layout;
            }

            lastFailure = string.Join("; ", topologyErrors);
        }

        throw new DungeonLayoutGenerationException(
            $"Theme {theme.Code}, seed {seed}, attempt {lastAttempt}: generation failed after "
            + $"{MaximumAttempts} deterministic attempts. Last frontier rejection: {lastFailure}.");
    }

    private static bool TryBuildOrdinary(
        SearchState state,
        IReadOnlyList<DungeonRoomDefinition> ordinary,
        IReadOnlyList<DungeonRoomDefinition> bosses,
        int target)
    {
        if (!state.TakeSearchStep())
        {
            return false;
        }

        if (state.PlacementCount == target - 1)
        {
            return TryPlaceBoss(state, bosses);
        }

        List<OpenConnector> frontier = state.GetFrontier();
        state.Random.Shuffle(frontier);
        foreach (OpenConnector source in frontier)
        {
            List<PlacementCandidate> candidates = BuildCandidates(state, source, ordinary);
            foreach (PlacementCandidate candidate in candidates)
            {
                if (!state.TryAdd(source, candidate, isBoss: false))
                {
                    continue;
                }

                if (state.HasFrontier && TryBuildOrdinary(state, ordinary, bosses, target))
                {
                    return true;
                }

                state.RemoveLast();
            }
        }

        state.Failure = frontier.Count == 0
            ? "connector frontier became empty"
            : $"{state.DescribeFrontier(frontier)} had no valid ordinary room at placement {state.PlacementCount}";
        return false;
    }

    private static bool TryPlaceBoss(SearchState state, IReadOnlyList<DungeonRoomDefinition> bosses)
    {
        List<OpenConnector> frontier = state.GetFrontier();
        state.Random.Shuffle(frontier);
        foreach (OpenConnector source in frontier)
        {
            if (state.GraphDistanceFromStart(source.Placement.Id) + 1 < state.Theme.MinimumBossDistance)
            {
                continue;
            }

            List<PlacementCandidate> candidates = BuildCandidates(state, source, bosses);
            foreach (PlacementCandidate candidate in candidates)
            {
                if (state.TryAdd(source, candidate, isBoss: true))
                {
                    return true;
                }
            }
        }

        state.Failure = state.DescribeFrontier(frontier)
            + " had no boss connector meeting minimum graph distance and geometry constraints";
        return false;
    }

    private static List<PlacementCandidate> BuildCandidates(
        SearchState state,
        OpenConnector source,
        IReadOnlyList<DungeonRoomDefinition> rooms)
    {
        var candidates = new List<PlacementCandidate>();
        foreach (DungeonRoomDefinition room in rooms)
        {
            foreach (int rotation in room.AllowedRotations)
            {
                for (int connectorIndex = 0; connectorIndex < room.Connectors.Length; connectorIndex++)
                {
                    DungeonConnectorDefinition connector = room.Connectors[connectorIndex];
                    var originPlacement = new DungeonRoomPlacement(-1, room, 0, 0, rotation);
                    DungeonConnectorGeometry geometry = DungeonGeometry.ConnectorGeometry(originPlacement, connector);
                    if (geometry.Side != DungeonGeometry.Opposite(source.Geometry.Side)
                        || !DungeonGeometry.ConnectorsAccept(
                            source.Placement.Room,
                            source.Connector,
                            room,
                            connector))
                    {
                        continue;
                    }

                    int originX = source.Geometry.OutsideCell.X - geometry.BoundaryCell.X;
                    int originZ = source.Geometry.OutsideCell.Z - geometry.BoundaryCell.Z;
                    double priority = Math.Pow(
                        Math.Max(0.000001f, state.Random.NextFloat()),
                        1d / Math.Max(1, room.Weight));
                    candidates.Add(new PlacementCandidate(room, rotation, connectorIndex, originX, originZ, priority));
                }
            }
        }

        candidates.Sort((left, right) => right.Priority.CompareTo(left.Priority));
        return candidates;
    }

    private static DungeonRoomDefinition WeightedChoice(IReadOnlyList<DungeonRoomDefinition> rooms, DungeonRandom random)
    {
        int total = 0;
        foreach (DungeonRoomDefinition room in rooms)
        {
            total += Math.Max(1, room.Weight);
        }

        int roll = random.Next(total);
        foreach (DungeonRoomDefinition room in rooms)
        {
            roll -= Math.Max(1, room.Weight);
            if (roll < 0)
            {
                return room;
            }
        }

        return rooms[rooms.Count - 1];
    }

    private static ulong MixSeed(ulong seed, ulong attempt)
    {
        ulong mixed = seed + 0x9e3779b97f4a7c15UL * (attempt + 1);
        mixed = (mixed ^ (mixed >> 30)) * 0xbf58476d1ce4e5b9UL;
        mixed = (mixed ^ (mixed >> 27)) * 0x94d049bb133111ebUL;
        return mixed ^ (mixed >> 31);
    }

    private static bool SharesTheme(IEnumerable<string> roomThemes, IEnumerable<string> selectedThemes)
    {
        foreach (string roomTheme in roomThemes)
        {
            foreach (string selectedTheme in selectedThemes)
            {
                if (string.Equals(roomTheme, selectedTheme, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private sealed record PlacementCandidate(
        DungeonRoomDefinition Room,
        int Rotation,
        int ConnectorIndex,
        int OriginX,
        int OriginZ,
        double Priority);

    private sealed record OpenConnector(
        DungeonRoomPlacement Placement,
        int ConnectorIndex,
        DungeonConnectorDefinition Connector,
        DungeonConnectorGeometry Geometry);

    private sealed class SearchState
    {
        private readonly List<DungeonRoomPlacement> placements = new List<DungeonRoomPlacement>();
        private readonly List<DungeonConnectorJoin> joins = new List<DungeonConnectorJoin>();
        private readonly Dictionary<DungeonGridCell, int> occupied = new Dictionary<DungeonGridCell, int>();
        private readonly Dictionary<int, HashSet<int>> usedConnectors = new Dictionary<int, HashSet<int>>();
        private readonly int maximumSearchSteps;
        private int searchSteps;

        public SearchState(DungeonThemeDefinition theme, DungeonRandom random, int maximumSearchSteps)
        {
            Theme = theme;
            Random = random;
            this.maximumSearchSteps = maximumSearchSteps;
        }

        public DungeonThemeDefinition Theme { get; }
        public DungeonRandom Random { get; }
        public string Failure { get; set; } = "search exhausted";
        public int PlacementCount => placements.Count;
        public bool HasFrontier => GetFrontier().Count > 0;

        public bool TakeSearchStep()
        {
            searchSteps++;
            if (searchSteps <= maximumSearchSteps)
            {
                return true;
            }

            Failure = $"backtracking exceeded {maximumSearchSteps} search steps";
            return false;
        }

        public bool TryAddInitial(DungeonRoomDefinition room, int rotation)
        {
            var placement = new DungeonRoomPlacement(0, room, 0, 0, rotation);
            if (!CanOccupy(placement))
            {
                return false;
            }

            AddOccupancy(placement);
            placements.Add(placement);
            usedConnectors[placement.Id] = new HashSet<int>();
            return true;
        }

        public bool TryAdd(OpenConnector source, PlacementCandidate candidate, bool isBoss)
        {
            var placement = new DungeonRoomPlacement(
                placements.Count,
                candidate.Room,
                candidate.OriginX,
                candidate.OriginZ,
                candidate.Rotation);
            if (!CanOccupy(placement))
            {
                return false;
            }

            if (isBoss && candidate.Room.Connectors.Length != 1)
            {
                return false;
            }

            AddOccupancy(placement);
            placements.Add(placement);
            usedConnectors[placement.Id] = new HashSet<int> { candidate.ConnectorIndex };
            usedConnectors[source.Placement.Id].Add(source.ConnectorIndex);
            joins.Add(new DungeonConnectorJoin(
                new DungeonConnectorEndpoint(source.Placement.Id, source.Connector.Id),
                new DungeonConnectorEndpoint(placement.Id, candidate.Room.Connectors[candidate.ConnectorIndex].Id),
                false));
            return true;
        }

        public void RemoveLast()
        {
            DungeonRoomPlacement placement = placements[placements.Count - 1];
            DungeonConnectorJoin join = joins[joins.Count - 1];
            RemoveOccupancy(placement);
            placements.RemoveAt(placements.Count - 1);
            usedConnectors.Remove(placement.Id);
            joins.RemoveAt(joins.Count - 1);

            DungeonConnectorEndpoint source = join.First.PlacementId == placement.Id ? join.Second : join.First;
            DungeonRoomPlacement sourcePlacement = placements[source.PlacementId];
            int connectorIndex = FindConnectorIndex(sourcePlacement.Room, source.ConnectorId);
            usedConnectors[source.PlacementId].Remove(connectorIndex);
        }

        public List<OpenConnector> GetFrontier()
        {
            var frontier = new List<OpenConnector>();
            foreach (DungeonRoomPlacement placement in placements)
            {
                for (int i = 0; i < placement.Room.Connectors.Length; i++)
                {
                    if (!usedConnectors[placement.Id].Contains(i))
                    {
                        DungeonConnectorDefinition connector = placement.Room.Connectors[i];
                        frontier.Add(new OpenConnector(
                            placement,
                            i,
                            connector,
                            DungeonGeometry.ConnectorGeometry(placement, connector)));
                    }
                }
            }

            return frontier;
        }

        public string DescribeFrontier(IReadOnlyList<OpenConnector> frontier)
        {
            if (frontier.Count == 0)
            {
                return "empty frontier";
            }

            OpenConnector first = frontier[0];
            return $"frontier[{frontier.Count}] beginning at placement {first.Placement.Id}/"
                + $"{first.Connector.Id} ({first.Geometry.OutsideCell.X},{first.Geometry.OutsideCell.Z})";
        }

        public int GraphDistanceFromStart(int placementId)
        {
            var adjacency = BuildAdjacency();
            var distances = new Dictionary<int, int> { [0] = 0 };
            var queue = new Queue<int>();
            queue.Enqueue(0);
            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                foreach (int next in adjacency[current])
                {
                    if (distances.TryAdd(next, distances[current] + 1))
                    {
                        queue.Enqueue(next);
                    }
                }
            }

            return distances.TryGetValue(placementId, out int distance) ? distance : -1;
        }

        public void AddCompatibleLoops()
        {
            List<OpenConnector> frontier = GetFrontier();
            Random.Shuffle(frontier);
            for (int i = 0; i < frontier.Count; i++)
            {
                OpenConnector first = frontier[i];
                if (usedConnectors[first.Placement.Id].Contains(first.ConnectorIndex))
                {
                    continue;
                }

                for (int j = i + 1; j < frontier.Count; j++)
                {
                    OpenConnector second = frontier[j];
                    if (first.Placement.Id == second.Placement.Id
                        || usedConnectors[second.Placement.Id].Contains(second.ConnectorIndex)
                        || first.Geometry.OutsideCell != second.Geometry.BoundaryCell
                        || second.Geometry.OutsideCell != first.Geometry.BoundaryCell
                        || first.Geometry.Side != DungeonGeometry.Opposite(second.Geometry.Side)
                        || !DungeonGeometry.ConnectorsAccept(
                            first.Placement.Room,
                            first.Connector,
                            second.Placement.Room,
                            second.Connector)
                        || Random.NextFloat() > Theme.LoopChance)
                    {
                        continue;
                    }

                    usedConnectors[first.Placement.Id].Add(first.ConnectorIndex);
                    usedConnectors[second.Placement.Id].Add(second.ConnectorIndex);
                    joins.Add(new DungeonConnectorJoin(
                        new DungeonConnectorEndpoint(first.Placement.Id, first.Connector.Id),
                        new DungeonConnectorEndpoint(second.Placement.Id, second.Connector.Id),
                        true));
                    break;
                }
            }
        }

        public DungeonLayout ToLayout(ulong seed)
        {
            return new DungeonLayout(
                Theme.Code,
                seed,
                Theme.CellSize,
                Theme.FloorY,
                placements.ToArray(),
                joins.ToArray());
        }

        private bool CanOccupy(DungeonRoomPlacement placement)
        {
            foreach (DungeonGridCell cell in DungeonGeometry.OccupiedCells(placement))
            {
                if (occupied.ContainsKey(cell)
                    || Math.Abs(cell.X) > Theme.GenerationRadiusCells
                    || Math.Abs(cell.Z) > Theme.GenerationRadiusCells)
                {
                    return false;
                }
            }

            return true;
        }

        private void AddOccupancy(DungeonRoomPlacement placement)
        {
            foreach (DungeonGridCell cell in DungeonGeometry.OccupiedCells(placement))
            {
                occupied.Add(cell, placement.Id);
            }
        }

        private void RemoveOccupancy(DungeonRoomPlacement placement)
        {
            foreach (DungeonGridCell cell in DungeonGeometry.OccupiedCells(placement))
            {
                occupied.Remove(cell);
            }
        }

        private Dictionary<int, List<int>> BuildAdjacency()
        {
            var result = new Dictionary<int, List<int>>();
            foreach (DungeonRoomPlacement placement in placements)
            {
                result[placement.Id] = new List<int>();
            }

            foreach (DungeonConnectorJoin join in joins)
            {
                result[join.First.PlacementId].Add(join.Second.PlacementId);
                result[join.Second.PlacementId].Add(join.First.PlacementId);
            }

            return result;
        }

        private static int FindConnectorIndex(DungeonRoomDefinition room, string connectorId)
        {
            for (int i = 0; i < room.Connectors.Length; i++)
            {
                if (string.Equals(room.Connectors[i].Id, connectorId, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            throw new InvalidOperationException($"Room {room.Code} has no connector {connectorId}.");
        }
    }
}
