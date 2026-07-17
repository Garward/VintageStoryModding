using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using VRPG.Modules.Rpg.Balance;

namespace VRPG.Balance;

internal static class BalanceReportWriter
{
    public static void WriteCsv(string path, IReadOnlyList<DamageScalingRow> rows)
    {
        EnsureParent(path);
        using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
        writer.WriteLine("scenario,skillCode,skillName,skillRank,creatureLevel,rarityCode,rarityName,rarityEligible,affixSlots,baseCreatureHealth,encounterHealthMultiplier,healthMultiplier,targetHealth,creatureDamageMultiplier,creatureExperience,creatureBuildPressureMultiplier,encounterBuildPressureMultiplier,weaponRequiredLevel,weaponLevelLag,weaponRarityCode,weaponRarityName,weaponRarityMultiplier,weaponLevelBaseDamage,finalWeaponDamage,skillWeaponDamagePercent,nonCriticalHit,finalCritChancePercent,criticalDamagePercent,expectedHit,hitsPerActivation,expectedDamagePerActivation,castsPerSecond,hitsPerSecond,expectedDps,expectedHitsToKill,wholeHitsToKill,expectedTimeToKillSeconds,incomingDamagePerHit,incomingDps,playerSurvivalSeconds,winsDamageRace");
        foreach (DamageScalingRow row in rows)
        {
            writer.WriteLine(string.Join(",", new[]
            {
                Csv(row.Scenario), Csv(row.SkillCode), Csv(row.SkillName), Number(row.SkillRank), Number(row.CreatureLevel),
                Csv(row.RarityCode), Csv(row.RarityName), row.RarityEligible ? "true" : "false", Number(row.AffixSlots),
                Number(row.BaseCreatureHealth), Number(row.EncounterHealthMultiplier), Number(row.HealthMultiplier), Number(row.TargetHealth),
                Number(row.CreatureDamageMultiplier), Number(row.CreatureExperience), Number(row.CreatureBuildPressureMultiplier),
                Number(row.EncounterBuildPressureMultiplier),
                Number(row.WeaponRequiredLevel),
                Number(row.WeaponLevelLag), Csv(row.WeaponRarityCode), Csv(row.WeaponRarityName),
                Number(row.WeaponRarityMultiplier), Number(row.WeaponLevelBaseDamage), Number(row.FinalWeaponDamage),
                Number(row.SkillWeaponDamagePercent), Number(row.NonCriticalHit),
                Number(row.FinalCriticalChancePercent), Number(row.CriticalDamagePercent), Number(row.ExpectedHit),
                Number(row.HitsPerActivation), Number(row.ExpectedDamagePerActivation), Number(row.CastsPerSecond),
                Number(row.HitsPerSecond), Number(row.ExpectedDps), Number(row.ExpectedHitsToKill),
                Number(row.WholeHitsToKill), Number(row.ExpectedTimeToKillSeconds), Number(row.IncomingDamagePerHit),
                Number(row.IncomingDps), Number(row.PlayerSurvivalSeconds), row.WinsDamageRace ? "true" : "false"
            }));
        }
    }

    public static string WriteMarkdownSummary(string csvPath, IReadOnlyList<DamageScalingRow> rows)
    {
        string path = Path.ChangeExtension(csvPath, ".md");
        EnsureParent(path);
        var text = new StringBuilder();
        text.AppendLine("# VRPG Damage Scaling Summary").AppendLine();
        text.Append("Generated from the same level, tier, and provisional rarity formulas used by runtime scaling. Full data: `")
            .Append(Path.GetFileName(csvPath)).AppendLine("`.").AppendLine();

        foreach (IGrouping<string, DamageScalingRow> scenario in rows.GroupBy(row => row.Scenario))
        {
            DamageScalingRow first = scenario.OrderBy(row => row.CreatureLevel).First();
            DamageScalingRow last = scenario.OrderByDescending(row => row.CreatureLevel).First();
            text.Append("## ").Append(first.SkillName).Append(" — ").Append(first.Scenario).AppendLine().AppendLine();
            text.Append("Weapon requirement: `").Append(Range(first.WeaponRequiredLevel, last.WeaponRequiredLevel))
                .Append("`, weapon lag: `").Append(Range(first.WeaponLevelLag, last.WeaponLevelLag))
                .Append("`, rarity: `").Append(first.WeaponRarityName).Append(" ×")
                .Append(Number(first.WeaponRarityMultiplier)).Append("`, skill effectiveness: `")
                .Append(Range(first.SkillWeaponDamagePercent, last.SkillWeaponDamagePercent)).AppendLine("%`.").AppendLine();
            text.Append("Base Health: `").Append(Number(first.BaseCreatureHealth))
                .Append("`, encounter Health multiplier: `×")
                .Append(Number(first.EncounterHealthMultiplier)).AppendLine("`.").AppendLine();
            text.Append("Expected hit: `").Append(Range(first.ExpectedHit, last.ExpectedHit))
                .Append("`, hits/activation: `").Append(first.HitsPerActivation)
                .Append("`, activation damage: `").Append(Range(first.ExpectedDamagePerActivation, last.ExpectedDamagePerActivation))
                .Append("`, hits/s: `").Append(Number(first.HitsPerSecond)).Append("`, DPS: `")
                .Append(Range(first.ExpectedDps, last.ExpectedDps)).Append("`, final crit: `")
                .Append(Number(first.FinalCriticalChancePercent)).AppendLine("%`.").AppendLine();
            text.Append("Creature health-to-current-weapon growth pressure: `")
                .Append(Range(first.CreatureBuildPressureMultiplier, last.CreatureBuildPressureMultiplier))
                .AppendLine("×` (before the player's build multipliers).").AppendLine();
            text.Append("Pressure after encounter Health layer: `")
                .Append(Range(first.EncounterBuildPressureMultiplier, last.EncounterBuildPressureMultiplier))
                .AppendLine("×`.").AppendLine();
            DamageScalingRow? lastOrdinary = scenario
                .Where(row => string.Equals(row.RarityCode, "ordinary", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(row => row.CreatureLevel)
                .FirstOrDefault();
            if (lastOrdinary != null)
            {
                text.Append("Top ordinary damage race: TTK `").Append(Number(lastOrdinary.ExpectedTimeToKillSeconds))
                    .Append("s` vs survival `").Append(Number(lastOrdinary.PlayerSurvivalSeconds)).Append("s` — **")
                    .Append(lastOrdinary.WinsDamageRace ? "WIN" : "FAIL").AppendLine("**.").AppendLine();
            }
            text.AppendLine("| Rarity | Eligible from | Level 1 TTK | Level 25 TTK | Level 50 TTK | Level 75 TTK | Level 100 TTK |");
            text.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: | ---: |");

            foreach (IGrouping<string, DamageScalingRow> rarity in scenario.GroupBy(row => row.RarityCode))
            {
                DamageScalingRow rarityFirst = rarity.First();
                text.Append("| ").Append(rarityFirst.RarityName).Append(" | ")
                    .Append(rarity.Where(row => row.RarityEligible).Select(row => row.CreatureLevel).DefaultIfEmpty(0).Min()).Append(" | ")
                    .Append(TtkAt(rarity, 1)).Append(" | ")
                    .Append(TtkAt(rarity, 25)).Append(" | ")
                    .Append(TtkAt(rarity, 50)).Append(" | ")
                    .Append(TtkAt(rarity, 75)).Append(" | ")
                    .Append(TtkAt(rarity, 100)).AppendLine(" |");
            }

            text.AppendLine();
        }

        File.WriteAllText(path, text.ToString(), new UTF8Encoding(false));
        return path;
    }

    private static string TtkAt(IEnumerable<DamageScalingRow> rows, int level)
    {
        DamageScalingRow? row = rows.OrderBy(candidate => Math.Abs(candidate.CreatureLevel - level)).FirstOrDefault();
        return row == null ? "—" : Number(row.ExpectedTimeToKillSeconds) + "s";
    }

    private static void EnsureParent(string path)
    {
        string? parent = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrWhiteSpace(parent))
        {
            Directory.CreateDirectory(parent);
        }
    }

    private static string Csv(string value)
    {
        string escaped = value.Replace("\"", "\"\"");
        return escaped.IndexOfAny(new[] { ',', '\"', '\n', '\r' }) >= 0 ? "\"" + escaped + "\"" : escaped;
    }

    private static string Number(double value)
    {
        if (double.IsPositiveInfinity(value)) return "Infinity";
        if (double.IsNegativeInfinity(value)) return "-Infinity";
        if (double.IsNaN(value)) return "NaN";
        return value.ToString("0.####", CultureInfo.InvariantCulture);
    }

    private static string Range(double first, double last)
    {
        return Math.Abs(first - last) < 0.00005 ? Number(first) : Number(first) + "–" + Number(last);
    }
}
