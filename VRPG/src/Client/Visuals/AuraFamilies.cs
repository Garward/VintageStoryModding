using System.Collections.Generic;

namespace VRPG.Client.Visuals;

public sealed class AuraFamily
{
    public int ColorRgba;
    public float RiseVelocity;
    public float Gravity;
    public float Scale;
    public float QuantityPerTick;
    public bool Cube;
}

public static class AuraFamilies
{
    private static readonly Dictionary<string, AuraFamily> byName = new Dictionary<string, AuraFamily>(System.StringComparer.OrdinalIgnoreCase)
    {
        ["rustflakes"] = new AuraFamily { ColorRgba = unchecked((int)0xd0c96a1e), RiseVelocity = -0.05f, Gravity = 0.35f, Scale = 0.22f, QuantityPerTick = 1.6f, Cube = true },
        ["embers"] = new AuraFamily { ColorRgba = unchecked((int)0xd0f06a28), RiseVelocity = 0.35f, Gravity = -0.02f, Scale = 0.2f, QuantityPerTick = 1.8f },
        ["drips"] = new AuraFamily { ColorRgba = unchecked((int)0xd0b81c2e), RiseVelocity = -0.15f, Gravity = 0.6f, Scale = 0.18f, QuantityPerTick = 1.2f },
        ["frost"] = new AuraFamily { ColorRgba = unchecked((int)0xc078c9f0), RiseVelocity = 0.06f, Gravity = 0.02f, Scale = 0.24f, QuantityPerTick = 1.2f },
        ["sparks"] = new AuraFamily { ColorRgba = unchecked((int)0xe0ffe66d), RiseVelocity = 0.25f, Gravity = 0.1f, Scale = 0.14f, QuantityPerTick = 1.5f },
        ["mark"] = new AuraFamily { ColorRgba = unchecked((int)0xc0ffd166), RiseVelocity = 0.5f, Gravity = 0f, Scale = 0.26f, QuantityPerTick = 0.8f }
    };

    public static AuraFamily? Get(string name)
    {
        return !string.IsNullOrWhiteSpace(name) && byName.TryGetValue(name, out AuraFamily? family) ? family : null;
    }
}
