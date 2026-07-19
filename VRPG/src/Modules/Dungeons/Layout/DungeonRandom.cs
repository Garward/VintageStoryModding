using System;
using System.Collections.Generic;

namespace VRPG.Modules.Dungeons.Layout;

internal sealed class DungeonRandom
{
    private ulong state;

    public DungeonRandom(ulong seed)
    {
        state = seed == 0 ? 0x9e3779b97f4a7c15UL : seed;
    }

    public ulong NextUInt64()
    {
        ulong value = state;
        value ^= value >> 12;
        value ^= value << 25;
        value ^= value >> 27;
        state = value;
        return value * 2685821657736338717UL;
    }

    public int Next(int exclusiveMax)
    {
        if (exclusiveMax <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(exclusiveMax));
        }

        return (int)(NextUInt64() % (uint)exclusiveMax);
    }

    public float NextFloat()
    {
        return (NextUInt64() >> 40) / (float)(1UL << 24);
    }

    public void Shuffle<T>(IList<T> values)
    {
        for (int i = values.Count - 1; i > 0; i--)
        {
            int swap = Next(i + 1);
            (values[i], values[swap]) = (values[swap], values[i]);
        }
    }
}
