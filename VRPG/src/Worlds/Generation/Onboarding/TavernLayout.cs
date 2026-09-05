using System;
using System.Collections.Generic;

namespace VRPG.Worlds.Generation.Onboarding;

public enum TavernBlock { Air, Stone, Timber, BottomSlab, Lamp }

public readonly record struct TavernCell(int X, int Y, int Z);
public readonly record struct TavernSurface(int X, int Z, int HalfHeight);
public readonly record struct TavernAnchor(string Code, int X, int Z, int HalfHeight)
{
    public double CenterX => X + 0.5;
    public double FeetY => HalfHeight / 2.0;
    public double CenterZ => Z + 0.5;
}

/// <summary>Immutable, local-coordinate generation output; no engine IDs or gameplay grants.</summary>
public sealed class TavernLayout
{
    public const int Width = 19;
    public const int Height = 9;
    public const int Depth = 23;
    public const int GeneratorVersion = 1;
    public const string DefinitionCode = "vrpg:safe-arrival-tavern";

    private readonly TavernBlock[,,] blocks;
    public long Seed { get; }
    public IReadOnlyList<TavernSurface> Surfaces { get; }
    public IReadOnlyList<TavernAnchor> Anchors { get; }

    internal TavernLayout(long seed, TavernBlock[,,] blocks,
        List<TavernSurface> surfaces, List<TavernAnchor> anchors)
    {
        Seed = seed;
        this.blocks = (TavernBlock[,,])blocks.Clone();
        Surfaces = surfaces.AsReadOnly();
        Anchors = anchors.AsReadOnly();
    }

    public TavernBlock GetBlock(int x, int y, int z) => blocks[x, y, z];

    public TavernAnchor GetAnchor(string code)
    {
        foreach (TavernAnchor anchor in Anchors)
            if (anchor.Code == code) return anchor;
        throw new ArgumentException("Unknown tavern anchor.", nameof(code));
    }

    public IEnumerable<TavernCell> Cells()
    {
        for (int x = 0; x < Width; x++)
        for (int z = 0; z < Depth; z++)
        for (int y = 0; y < Height; y++)
            yield return new TavernCell(x, y, z);
    }
}
