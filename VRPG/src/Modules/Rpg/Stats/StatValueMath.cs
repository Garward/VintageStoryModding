using System;
using VRPG.Data.Definitions;

namespace VRPG.Modules.Rpg.Stats;

/// <summary>Finite boundaries for resolved runtime stats; intermediate arithmetic stays in double precision.</summary>
internal static class StatValueMath
{
    public static double ModifierValue(StatModifierDefinition modifier)
    {
        if (!float.IsFinite(modifier.Min) || !float.IsFinite(modifier.Max)) return double.NaN;
        return modifier.Min == modifier.Max ? modifier.Min : ((double)modifier.Min + modifier.Max) / 2d;
    }

    public static float NonNegativeFloat(double value, float minimum = 0f)
    {
        // An indeterminate result contributes nothing. Positive overflow saturates at the storage boundary.
        if (double.IsNaN(value) || value <= minimum) return minimum;
        return (float)Math.Min(float.MaxValue, value);
    }
}
