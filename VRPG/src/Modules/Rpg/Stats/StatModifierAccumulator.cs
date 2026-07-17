using System;
using VRPG.Data;

namespace VRPG.Modules.Rpg.Stats;

public sealed class StatModifierAccumulator
{
    public double Flat { get; private set; }
    public double Increased { get; private set; }
    public double MoreMultiplier { get; private set; } = 1.0;

    public void Add(string? operation, double value)
    {
        switch (StatModifierOperations.Normalize(operation))
        {
            case StatModifierOperations.Increased:
                Increased += value;
                break;
            case StatModifierOperations.More:
                MoreMultiplier *= 1.0 + value / 100.0;
                break;
            default:
                Flat += value;
                break;
        }
    }

    public double Resolve(double baseValue)
    {
        double resolved = (baseValue + Flat) * (1.0 + Increased / 100.0) * MoreMultiplier;
        return Math.Max(0.0, resolved);
    }
}
