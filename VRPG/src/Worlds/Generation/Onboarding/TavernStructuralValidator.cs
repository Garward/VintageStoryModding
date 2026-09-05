using System;
using System.Collections.Generic;

namespace VRPG.Worlds.Generation.Onboarding;

public sealed record TavernValidationIssue(string Code, TavernCell Cell);

/// <summary>Checks actual voxels against the shared half-step navigation raster.</summary>
public static class TavernStructuralValidator
{
    private static readonly (int X, int Z)[] Directions = { (1, 0), (-1, 0), (0, 1), (0, -1) };

    public static IReadOnlyList<TavernValidationIssue> Validate(TavernLayout layout,
        Func<int, int, int, TavernBlock>? readBlock = null)
    {
        ArgumentNullException.ThrowIfNull(layout);
        readBlock ??= layout.GetBlock;
        var issues = new List<TavernValidationIssue>();
        var walk = new Dictionary<(int X, int Z), TavernSurface>();

        foreach (TavernCell cell in layout.Cells())
        {
            TavernBlock block = readBlock(cell.X, cell.Y, cell.Z);
            if (!Enum.IsDefined(block)) issues.Add(new("unknown-block", cell));
            if ((cell.Y == 0 || cell.Y == TavernLayout.Height - 1
                || cell.X == 0 || cell.X == TavernLayout.Width - 1
                || cell.Z == 0 || cell.Z == TavernLayout.Depth - 1) && !IsFull(block))
                issues.Add(new("open-envelope", cell));
            if (block == TavernBlock.BottomSlab
                && (cell.Y == 0 || !IsFull(readBlock(cell.X, cell.Y - 1, cell.Z))))
                issues.Add(new("unsupported-slab", cell));
        }

        foreach (TavernSurface surface in layout.Surfaces)
        {
            walk.Add((surface.X, surface.Z), surface);
            int topY = (surface.HalfHeight - 1) / 2;
            var cell = new TavernCell(surface.X, topY, surface.Z);
            TavernBlock top = readBlock(surface.X, topY, surface.Z);
            if ((surface.HalfHeight % 2 == 0 && !IsFull(top))
                || (surface.HalfHeight % 2 != 0 && top != TavernBlock.BottomSlab))
                issues.Add(new("walk-surface", cell));
            for (int y = 0; y < topY; y++)
                if (!IsFull(readBlock(surface.X, y, surface.Z)))
                    issues.Add(new("floor-support", new(surface.X, y, surface.Z)));
            // Two full blocks of body clearance, including the slab's unused upper half.
            int clearanceTop = (surface.HalfHeight + 4 + 1) / 2;
            for (int y = topY + 1; y < clearanceTop; y++)
                if (readBlock(surface.X, y, surface.Z) != TavernBlock.Air)
                    issues.Add(new("head-clearance", new(surface.X, y, surface.Z)));
        }

        foreach (TavernSurface surface in layout.Surfaces)
        foreach (var direction in Directions)
            if (walk.TryGetValue((surface.X + direction.X, surface.Z + direction.Z), out var next)
                && Math.Abs(surface.HalfHeight - next.HalfHeight) > 1)
                issues.Add(new("route-grade", new(surface.X, surface.HalfHeight / 2, surface.Z)));

        TavernAnchor arrival = layout.GetAnchor("arrival");
        var visited = new HashSet<(int X, int Z)> { (arrival.X, arrival.Z) };
        var queue = new Queue<(int X, int Z)>();
        queue.Enqueue((arrival.X, arrival.Z));
        while (queue.TryDequeue(out var current))
        {
            if (!walk.TryGetValue(current, out var surface)) continue;
            foreach (var direction in Directions)
            {
                var position = (current.X + direction.X, current.Z + direction.Z);
                if (walk.TryGetValue(position, out var next)
                    && Math.Abs(surface.HalfHeight - next.HalfHeight) <= 1 && visited.Add(position))
                    queue.Enqueue(position);
            }
        }
        foreach (TavernSurface surface in layout.Surfaces)
            if (!visited.Contains((surface.X, surface.Z)))
                issues.Add(new("unreachable", new(surface.X, surface.HalfHeight / 2, surface.Z)));

        foreach (TavernAnchor anchor in layout.Anchors)
        {
            // Every anchor has a level 3x3 landing, safe for positioning and recovery drift.
            for (int dx = -1; dx <= 1; dx++)
            for (int dz = -1; dz <= 1; dz++)
                if (!walk.TryGetValue((anchor.X + dx, anchor.Z + dz), out var surface)
                    || surface.HalfHeight != anchor.HalfHeight
                    || !visited.Contains((surface.X, surface.Z)))
                    issues.Add(new("anchor-landing", new(anchor.X + dx, anchor.HalfHeight / 2, anchor.Z + dz)));
        }
        return issues.AsReadOnly();
    }

    private static bool IsFull(TavernBlock block) => block is TavernBlock.Stone or TavernBlock.Timber or TavernBlock.Lamp;
}
