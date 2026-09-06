using System;
using VRPG.Inventory.Gear;
using VRPG.Modules.Rpg.Stats;

namespace VRPG.Modules.Rpg.Players;

public static class CoreAttributeResolver
{
    public const string Strength = "strength";
    public const string Dexterity = "dexterity";
    public const string Intelligence = "intelligence";

    private static readonly string[] AttributeCodes = { Strength, Dexterity, Intelligence };

    public static string ResolvePrimary(RpgPlayerState state)
    {
        int highest = Math.Max(GetValue(state, Strength), Math.Max(GetValue(state, Dexterity), GetValue(state, Intelligence)));
        if (highest <= 0)
        {
            return Normalize(state.StartingAttributeAffinity);
        }

        string affinity = Normalize(state.StartingAttributeAffinity);
        if (IsCoreAttribute(affinity) && GetValue(state, affinity) == highest)
        {
            return affinity;
        }

        for (int i = 0; i < AttributeCodes.Length; i++)
        {
            if (GetValue(state, AttributeCodes[i]) == highest)
            {
                return AttributeCodes[i];
            }
        }

        return "";
    }

    public static int GetValue(RpgPlayerState state, string code)
    {
        string normalized = Normalize(code);
        if (state.BaseStats.TryGetValue("vrpg:" + normalized, out int value))
        {
            return Math.Max(0, value);
        }

        return state.BaseStats.TryGetValue(normalized, out value) ? Math.Max(0, value) : 0;
    }

    /// <summary>Resolves transient equipped attributes without rewriting allocated stats or class authority.</summary>
    public static double GetEffectiveValue(RpgPlayerState state, string code, EquippedGearStats gear)
    {
        string canonical = "vrpg:" + Normalize(code);
        var modifiers = new StatModifierAccumulator();
        if (gear.Valid)
        {
            foreach (var modifier in gear.Modifiers)
            {
                if (GearModifierContract.IsCoreAttribute(modifier.Stat) && modifier.Stat == canonical)
                    modifiers.Add(modifier.Operation, StatValueMath.ModifierValue(modifier));
            }
        }
        return modifiers.Resolve(GetValue(state, code));
    }

    public static int TotalAllocated(RpgPlayerState state)
    {
        return GetValue(state, Strength) + GetValue(state, Dexterity) + GetValue(state, Intelligence);
    }

    public static bool IsCoreAttribute(string code)
    {
        string normalized = Normalize(code);
        return normalized == Strength || normalized == Dexterity || normalized == Intelligence;
    }

    public static string Normalize(string code)
    {
        string normalized = (code ?? "").Trim().ToLowerInvariant();
        return normalized.StartsWith("vrpg:", StringComparison.Ordinal) ? normalized.Substring("vrpg:".Length) : normalized;
    }
}
