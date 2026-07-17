using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace VRPG.Balance;

internal sealed class BalanceOptions
{
    public string? AssetsPath { get; private set; }
    public string? OutputPath { get; private set; }
    public string[] Skills { get; private set; } = new[] { "all" };
    public string[] Ranks { get; private set; } = new[] { "1", "max" };
    public double[] BaseHealthValues { get; private set; } = new[] { 20.0 };
    public double[] EncounterHealthMultipliers { get; private set; } = new[] { 1.0 };
    public int MinLevel { get; private set; } = 1;
    public int? MaxLevel { get; private set; }
    public bool IncludeIneligible { get; private set; } = true;
    public double? HitDamageOverride { get; private set; }
    public double? WeaponEffectiveness { get; private set; }
    public string DamageModel { get; private set; } = "weapon";
    public int[] WeaponLevelLags { get; private set; } = new[] { 0, 20, 40 };
    public int? WeaponRequiredLevel { get; private set; }
    public string[] WeaponRarities { get; private set; } = new[] { "common" };
    public double WeaponBaseDamage { get; private set; }
    public double FlatWeaponDamage { get; private set; }
    public double AdditionalWeaponDamage { get; private set; }
    public double MoreWeaponDamage { get; private set; }
    public double FlatDamage { get; private set; }
    public double AdditionalDamage { get; private set; }
    public double MoreDamage { get; private set; }
    public double FlatCrit { get; private set; }
    public double AdditionalCrit { get; private set; }
    public double MoreCrit { get; private set; }
    public double CritDamage { get; private set; } = 150.0;
    public double CastsPerSecond { get; private set; }
    public double BaseCreatureHitDamage { get; private set; } = 2.0;
    public double CreatureAttacksPerSecond { get; private set; } = 2.0 / 3.0;
    public double PlayerHealth { get; private set; } = 100.0;
    public double PlayerDamageReduction { get; private set; }
    public bool ShowHelp { get; private set; }

    public static BalanceOptions Parse(string[] args)
    {
        var options = new BalanceOptions();
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (arg is "-h" or "--help")
            {
                options.ShowHelp = true;
                continue;
            }

            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Unexpected argument '{arg}'. Options must use --name value.");
            }

            string key = arg.Substring(2);
            if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Option '--{key}' requires a value.");
            }

            values[key] = args[++i];
        }

        options.AssetsPath = Get(values, "assets");
        options.OutputPath = Get(values, "output");
        options.Skills = Split(Get(values, "skill") ?? "all");
        options.Ranks = Split(Get(values, "rank") ?? "1,max");
        options.BaseHealthValues = SplitDoubles(Get(values, "base-health") ?? "20", "base-health");
        options.EncounterHealthMultipliers = SplitDoubles(
            Get(values, "encounter-health-multiplier") ?? "1",
            "encounter-health-multiplier");
        options.MinLevel = GetInt(values, "min-level", 1);
        options.MaxLevel = TryGetInt(values, "max-level");
        options.IncludeIneligible = GetBool(values, "include-ineligible", true);
        options.HitDamageOverride = TryGetDouble(values, "hit-damage");
        options.WeaponEffectiveness = TryGetDouble(values, "weapon-effectiveness");
        options.DamageModel = (Get(values, "damage-model") ?? "weapon").ToLowerInvariant();
        if (options.DamageModel is not ("weapon" or "legacy"))
        {
            throw new ArgumentException("Option '--damage-model' must be weapon or legacy.");
        }
        options.WeaponLevelLags = SplitInts(Get(values, "weapon-lag") ?? "0,20,40", "weapon-lag");
        if (options.WeaponLevelLags.Any(value => value < 0))
        {
            throw new ArgumentException("Option '--weapon-lag' cannot contain negative values.");
        }
        options.WeaponRequiredLevel = TryGetInt(values, "weapon-level");
        options.WeaponRarities = Split(Get(values, "weapon-rarity") ?? "common");
        options.WeaponBaseDamage = GetDouble(values, "weapon-base", 0);
        options.FlatWeaponDamage = GetDouble(values, "flat-weapon-damage", 0);
        options.AdditionalWeaponDamage = GetDouble(values, "additional-weapon-damage", 0);
        options.MoreWeaponDamage = GetDouble(values, "more-weapon-damage", 0);
        options.FlatDamage = GetDouble(values, "flat-damage", 0);
        options.AdditionalDamage = GetDouble(values, "additional-damage", 0);
        options.MoreDamage = GetDouble(values, "more-damage", 0);
        options.FlatCrit = GetDouble(values, "flat-crit", 0);
        options.AdditionalCrit = GetDouble(values, "additional-crit", 0);
        options.MoreCrit = GetDouble(values, "more-crit", 0);
        options.CritDamage = GetDouble(values, "crit-damage", 150);
        options.CastsPerSecond = GetDouble(values, "casts-per-second", 0);
        options.BaseCreatureHitDamage = GetDouble(values, "base-creature-hit", 2);
        options.CreatureAttacksPerSecond = GetDouble(values, "creature-attacks-per-second", 2.0 / 3.0);
        options.PlayerHealth = GetDouble(values, "player-health", 100);
        options.PlayerDamageReduction = GetDouble(values, "player-damage-reduction", 0);

        string[] known =
        {
            "assets", "output", "skill", "rank", "base-health", "min-level", "max-level",
            "include-ineligible", "hit-damage", "weapon-effectiveness", "flat-damage", "additional-damage", "more-damage",
            "flat-crit", "additional-crit", "more-crit", "crit-damage", "casts-per-second",
            "damage-model", "weapon-lag", "weapon-level", "weapon-rarity", "weapon-base",
            "flat-weapon-damage", "additional-weapon-damage", "more-weapon-damage",
            "base-creature-hit", "creature-attacks-per-second", "player-health", "player-damage-reduction",
            "encounter-health-multiplier"
        };
        string? unknown = values.Keys.FirstOrDefault(key => !known.Contains(key, StringComparer.OrdinalIgnoreCase));
        if (unknown != null)
        {
            throw new ArgumentException($"Unknown option '--{unknown}'.");
        }

        return options;
    }

    private static string? Get(IReadOnlyDictionary<string, string> values, string key)
    {
        return values.TryGetValue(key, out string? value) ? value : null;
    }

    private static string[] Split(string value)
    {
        return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static double[] SplitDoubles(string value, string key)
    {
        return Split(value).Select(part => ParseDouble(part, key)).ToArray();
    }

    private static int[] SplitInts(string value, string key)
    {
        return Split(value).Select(part => int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result)
            ? result
            : throw new ArgumentException($"Option '--{key}' requires comma-separated integers, received '{part}'."))
            .ToArray();
    }

    private static int GetInt(IReadOnlyDictionary<string, string> values, string key, int fallback)
    {
        return TryGetInt(values, key) ?? fallback;
    }

    private static int? TryGetInt(IReadOnlyDictionary<string, string> values, string key)
    {
        string? value = Get(values, key);
        if (value == null)
        {
            return null;
        }

        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result)
            ? result
            : throw new ArgumentException($"Option '--{key}' requires an integer, received '{value}'.");
    }

    private static double GetDouble(IReadOnlyDictionary<string, string> values, string key, double fallback)
    {
        return TryGetDouble(values, key) ?? fallback;
    }

    private static double? TryGetDouble(IReadOnlyDictionary<string, string> values, string key)
    {
        string? value = Get(values, key);
        return value == null ? null : ParseDouble(value, key);
    }

    private static double ParseDouble(string value, string key)
    {
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double result)
            ? result
            : throw new ArgumentException($"Option '--{key}' requires a number, received '{value}'.");
    }

    private static bool GetBool(IReadOnlyDictionary<string, string> values, string key, bool fallback)
    {
        string? value = Get(values, key);
        return value == null
            ? fallback
            : bool.TryParse(value, out bool result)
                ? result
                : throw new ArgumentException($"Option '--{key}' requires true or false, received '{value}'.");
    }
}
