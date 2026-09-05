using System.Collections.Generic;

namespace VRPG.Worlds.Generation.Onboarding;

/// <summary>Bounded creator room, common room and raised exit landing, generated without population.</summary>
public static class TavernGenerator
{
    public static TavernLayout Generate(long seed)
    {
        var blocks = new TavernBlock[TavernLayout.Width, TavernLayout.Height, TavernLayout.Depth];
        var surfaces = new List<TavernSurface>();

        // Solve walk heights first. The full-width grade has no exposed one-block shoulders.
        for (int x = 0; x < TavernLayout.Width; x++)
        for (int z = 0; z < TavernLayout.Depth; z++)
        {
            int halfHeight = z < 16 ? 4 : z == 16 ? 5 : 6;
            bool perimeter = x == 0 || x == TavernLayout.Width - 1
                || z == 0 || z == TavernLayout.Depth - 1;
            bool partition = z == 7 && (x < 7 || x > 11);
            // Built-in solid scenery suggests booths and a bar without containers, fire or NPCs.
            bool furniture = (x == 2 || x == 3 || x == 15 || x == 16)
                && z >= 10 && z <= 13;
            for (int y = 0; y < TavernLayout.Height; y++)
            {
                if (perimeter || y == TavernLayout.Height - 1 || partition)
                    blocks[x, y, z] = y == 0 ? TavernBlock.Stone : TavernBlock.Timber;
                else if (2 * (y + 1) <= halfHeight)
                    blocks[x, y, z] = TavernBlock.Stone;
                else if (2 * y < halfHeight)
                    blocks[x, y, z] = TavernBlock.BottomSlab;
                else if (furniture && y < 4)
                    blocks[x, y, z] = TavernBlock.Timber;
            }

            if (!perimeter && !partition && !furniture)
                surfaces.Add(new TavernSurface(x, z, halfHeight));
        }

        // Seed affects only a ceiling inset, never safety or circulation.
        for (int x = 5; x <= 13; x++)
            blocks[x, 8, 12] = (seed & 1) == 0 ? TavernBlock.Stone : TavernBlock.Timber;
        foreach (int z in new[] { 3, 11, 20 })
            blocks[9, 8, z] = TavernBlock.Lamp;

        return new TavernLayout(seed, blocks, surfaces, new List<TavernAnchor>
        {
            new("arrival", 9, 3, 4),
            new("recovery", 9, 5, 4),
            new("common-room", 9, 11, 4),
            // A semantic interaction anchor, not an unguarded hole into ungenerated space.
            new("exit", 9, 20, 6)
        });
    }
}
