using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using VRPG.Data.Definitions;
using VRPG.Modules.Rpg.Balance;

namespace VRPG.Balance;

internal static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            BalanceOptions options = BalanceOptions.Parse(args);
            if (options.ShowHelp)
            {
                WriteHelp();
                return 0;
            }

            string assetsRoot = BalanceDataLoader.FindAssetsRoot(options.AssetsPath);
            ScalingDefinition scaling = BalanceDataLoader.LoadScaling(assetsRoot);
            IReadOnlyList<SkillDefinition> skills = SelectSkills(BalanceDataLoader.LoadSkills(assetsRoot), options.Skills);
            IReadOnlyList<GearRarityDefinition> weaponRarities = SelectWeaponRarities(
                BalanceDataLoader.LoadGearRarities(assetsRoot),
                options.WeaponRarities);
            string output = Path.GetFullPath(options.OutputPath ?? BalanceDataLoader.DefaultOutputPath(assetsRoot));
            var simulator = new DamageScalingSimulator();
            var rows = new List<DamageScalingRow>();

            foreach (SkillDefinition skill in skills)
            {
                foreach (string rankToken in options.Ranks)
                {
                    foreach (double baseHealth in options.BaseHealthValues)
                    {
                        foreach (double encounterHealthMultiplier in options.EncounterHealthMultipliers)
                        {
                            int[] weaponLags = options.DamageModel == "weapon" && !options.WeaponRequiredLevel.HasValue
                                ? options.WeaponLevelLags
                                : new[] { 0 };
                            foreach (GearRarityDefinition weaponRarity in weaponRarities)
                            {
                                foreach (int weaponLag in weaponLags)
                                {
                                    DamageScalingScenario scenario = CreateScenario(
                                        options,
                                        scaling,
                                        skill,
                                        rankToken,
                                        baseHealth,
                                        encounterHealthMultiplier,
                                        weaponRarity,
                                        weaponLag);
                                    rows.AddRange(simulator.Run(scaling, skill, scenario));
                                }
                            }
                        }
                    }
                }
            }

            BalanceReportWriter.WriteCsv(output, rows);
            string summary = BalanceReportWriter.WriteMarkdownSummary(output, rows);
            Console.WriteLine($"Generated {rows.Count} rows for {skills.Count} skill(s) and {scaling.CreatureRarities.Length} rarity profile(s).");
            Console.WriteLine($"CSV: {output}");
            Console.WriteLine($"Summary: {summary}");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("VRPG balance tool failed: " + error.Message);
            return 1;
        }
    }

    private static DamageScalingScenario CreateScenario(
        BalanceOptions options,
        ScalingDefinition scaling,
        SkillDefinition skill,
        string rankToken,
        double baseHealth,
        double encounterHealthMultiplier,
        GearRarityDefinition weaponRarity,
        int weaponLag)
    {
        bool matched = string.Equals(rankToken, "matched", StringComparison.OrdinalIgnoreCase);
        int rank = string.Equals(rankToken, "max", StringComparison.OrdinalIgnoreCase)
            ? Math.Max(1, skill.MaxLevel)
            : matched
                ? 1
                : int.TryParse(rankToken, out int parsed)
                    ? parsed
                    : throw new ArgumentException($"Unknown rank '{rankToken}'. Use a number, max, or matched.");
        string rankLabel = matched ? "matched" : "r" + Math.Clamp(rank, 1, Math.Max(1, skill.MaxLevel));

        string weaponLabel = options.DamageModel == "legacy"
            ? "legacy"
            : options.WeaponRequiredLevel.HasValue
                ? "wl" + options.WeaponRequiredLevel.Value
                : "lag" + weaponLag;
        string rarityLabel = ShortCode(weaponRarity.Code);

        return new DamageScalingScenario
        {
            Code = ShortCode(skill.Code) + "-" + rankLabel + "-" + weaponLabel + "-" + rarityLabel
                + "-hp" + baseHealth.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)
                + "-eh" + encounterHealthMultiplier.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
            SkillRank = rank,
            MatchSkillRankToCreatureLevel = matched,
            MinCreatureLevel = options.MinLevel,
            MaxCreatureLevel = options.MaxLevel ?? Math.Max(1, scaling.MaxCreatureLevel),
            IncludeIneligibleRarities = options.IncludeIneligible,
            BaseCreatureHealth = Math.Max(1, baseHealth),
            EncounterHealthMultiplier = Math.Max(0.01, encounterHealthMultiplier),
            HitDamageOverride = options.HitDamageOverride,
            SkillWeaponDamagePercentOverride = options.WeaponEffectiveness,
            UseWeaponDamage = options.DamageModel == "weapon",
            WeaponLevelLag = weaponLag,
            WeaponRequiredLevelOverride = options.WeaponRequiredLevel,
            WeaponBaseDamageOverride = options.WeaponBaseDamage,
            FlatWeaponDamage = options.FlatWeaponDamage,
            AdditionalWeaponDamagePercent = options.AdditionalWeaponDamage,
            MoreWeaponDamagePercent = options.MoreWeaponDamage,
            WeaponRarityCode = weaponRarity.Code,
            WeaponRarityName = weaponRarity.Name,
            WeaponRarityMultiplier = weaponRarity.WeaponPowerScalar,
            FlatDamage = options.FlatDamage,
            AdditionalDamagePercent = options.AdditionalDamage,
            MoreDamagePercent = options.MoreDamage,
            FlatCriticalChancePercent = options.FlatCrit,
            AdditionalCriticalChancePercent = options.AdditionalCrit,
            MoreCriticalChancePercent = options.MoreCrit,
            CriticalDamagePercent = options.CritDamage,
            CastsPerSecond = options.CastsPerSecond,
            BaseCreatureHitDamage = options.BaseCreatureHitDamage,
            CreatureAttacksPerSecond = options.CreatureAttacksPerSecond,
            PlayerHealth = options.PlayerHealth,
            PlayerDamageReductionPercent = options.PlayerDamageReduction
        };
    }

    private static IReadOnlyList<GearRarityDefinition> SelectWeaponRarities(
        IReadOnlyList<GearRarityDefinition> loaded,
        string[] requested)
    {
        if (requested.Any(code => string.Equals(code, "all", StringComparison.OrdinalIgnoreCase)))
        {
            return loaded;
        }

        var selected = new List<GearRarityDefinition>();
        foreach (string requestedCode in requested)
        {
            GearRarityDefinition? rarity = loaded.FirstOrDefault(candidate =>
                string.Equals(candidate.Code, requestedCode, StringComparison.OrdinalIgnoreCase)
                || string.Equals(ShortCode(candidate.Code), ShortCode(requestedCode), StringComparison.OrdinalIgnoreCase));
            if (rarity == null)
            {
                throw new ArgumentException($"Unknown weapon rarity '{requestedCode}'. Loaded: {string.Join(", ", loaded.Select(candidate => candidate.Code))}");
            }

            if (!selected.Contains(rarity)) selected.Add(rarity);
        }

        return selected;
    }

    private static IReadOnlyList<SkillDefinition> SelectSkills(IReadOnlyList<SkillDefinition> loaded, string[] requested)
    {
        if (requested.Any(code => string.Equals(code, "all", StringComparison.OrdinalIgnoreCase)))
        {
            return loaded;
        }

        var selected = new List<SkillDefinition>();
        foreach (string requestedCode in requested)
        {
            if (string.Equals(ShortCode(requestedCode), "basic_attack", StringComparison.OrdinalIgnoreCase))
            {
                selected.Add(CreateBasicAttackProfile());
                continue;
            }

            SkillDefinition? skill = loaded.FirstOrDefault(candidate =>
                string.Equals(candidate.Code, requestedCode, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(ShortCode(candidate.Code), ShortCode(requestedCode), StringComparison.OrdinalIgnoreCase));
            if (skill == null)
            {
                throw new ArgumentException($"Unknown skill '{requestedCode}'. Loaded: {string.Join(", ", loaded.Select(candidate => candidate.Code))}");
            }

            if (!selected.Contains(skill)) selected.Add(skill);
        }

        return selected;
    }

    private static SkillDefinition CreateBasicAttackProfile()
    {
        return new SkillDefinition
        {
            Code = "vrpg:basic_attack",
            Name = "Uninvested Basic Attack",
            MaxLevel = 1,
            CooldownSeconds = 1,
            Damage = new SkillDamageDefinition
            {
                WeaponDamagePercent = 100,
                WeaponDamagePerLevelPercent = 0
            }
        };
    }

    private static string ShortCode(string code)
    {
        int separator = code.IndexOf(':');
        return separator >= 0 ? code.Substring(separator + 1) : code;
    }

    private static void WriteHelp()
    {
        Console.WriteLine("""
VRPG damage-scaling sweep

Usage:
  dotnet run --project VRPG/tools/VRPG.Balance -- [options]

Core options:
  --skill all|code[,code]       Skills to simulate. basic_attack is the synthetic no-skill profile.
  --rank 1,max,matched          Fixed ranks or rank scaled across creature levels.
  --base-health 20[,50]        Unscaled vanilla creature health profiles.
  --encounter-health-multiplier 1[,20]  Boss/archetype Health layer after level and rarity.
  --min-level 1                First creature level.
  --max-level 100              Last creature level.
  --include-ineligible true    Keep rarity rows below their configured minimum level.
  --output path.csv            CSV destination; a Markdown summary is written beside it.

Weapon profile options:
  --damage-model weapon        Use Weapon Damage coefficients; legacy uses prototype flat damage.
  --weapon-lag 0,20,40        Compare unchanged weapons this many levels below each creature.
  --weapon-level N            Use one fixed weapon requirement instead of relative lag.
  --weapon-rarity common      Gear rarity scalar; accepts comma lists or all.
  --weapon-base N             Override the configured level-1 weapon baseline.
  --flat-weapon-damage N      Flat Weapon Damage applied after the level baseline.
  --additional-weapon-damage N  Summed Additional Weapon Damage percent.
  --more-weapon-damage N      Aggregate More Weapon Damage percent.

Build profile options:
  --hit-damage N               Override the complete pre-stat skill hit.
  --weapon-effectiveness N     Override the skill's rank-resolved Weapon Damage percent.
  --flat-damage N              Flat damage before percentage layers.
  --additional-damage N        Summed Additional Damage percent.
  --more-damage N              Aggregate More Damage percent.
  --flat-crit N                Rare flat percentage points added to the 5% base.
  --additional-crit N          Summed Additional Critical Chance percent.
  --more-crit N                Aggregate More Critical Chance percent.
  --crit-damage 150            Total critical hit damage percent.
  --casts-per-second N         Override cadence; otherwise derive it from cooldown.
  --base-creature-hit 2        Unscaled damage of one representative creature hit.
  --creature-attacks-per-second 0.6667  Sustained representative incoming cadence.
  --player-health 100          Health available to the simulated player profile.
  --player-damage-reduction 0  Aggregate provisional incoming reduction percent.
  --assets path                Override assets/vrpg/vrpg discovery.
""");
    }
}
