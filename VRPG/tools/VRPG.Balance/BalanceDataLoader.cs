using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using VRPG.Data.Definitions;

namespace VRPG.Balance;

internal static class BalanceDataLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    public static string FindAssetsRoot(string? requested)
    {
        if (!string.IsNullOrWhiteSpace(requested))
        {
            return ValidateAssetsRoot(Path.GetFullPath(requested));
        }

        foreach (string start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            DirectoryInfo? directory = new DirectoryInfo(start);
            while (directory != null)
            {
                foreach (string candidate in new[]
                {
                    Path.Combine(directory.FullName, "assets", "vrpg", "vrpg"),
                    Path.Combine(directory.FullName, "VRPG", "assets", "vrpg", "vrpg")
                })
                {
                    if (File.Exists(Path.Combine(candidate, "scaling", "default.json")))
                    {
                        return candidate;
                    }
                }

                directory = directory.Parent;
            }
        }

        throw new DirectoryNotFoundException("Could not locate VRPG assets. Pass --assets /path/to/VRPG/assets/vrpg/vrpg.");
    }

    public static ScalingDefinition LoadScaling(string assetsRoot)
    {
        string path = Path.Combine(assetsRoot, "scaling", "default.json");
        return Deserialize<ScalingDefinition>(path);
    }

    public static IReadOnlyList<SkillDefinition> LoadSkills(string assetsRoot)
    {
        string directory = Path.Combine(assetsRoot, "skills");
        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException($"Skill directory not found: {directory}");
        }

        return Directory.GetFiles(directory, "*.json", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(Deserialize<SkillDefinition>)
            .ToArray();
    }

    public static IReadOnlyList<GearRarityDefinition> LoadGearRarities(string assetsRoot)
    {
        string directory = Path.Combine(assetsRoot, "rarities");
        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException($"Gear rarity directory not found: {directory}");
        }

        return Directory.GetFiles(directory, "*.json", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(Deserialize<GearRarityDefinition>)
            .ToArray();
    }

    public static string DefaultOutputPath(string assetsRoot)
    {
        DirectoryInfo? projectRoot = Directory.GetParent(assetsRoot)?.Parent?.Parent;
        string root = projectRoot?.FullName ?? Directory.GetCurrentDirectory();
        return Path.Combine(root, "balance-reports", "damage-scaling.csv");
    }

    private static string ValidateAssetsRoot(string path)
    {
        if (!File.Exists(Path.Combine(path, "scaling", "default.json")))
        {
            throw new DirectoryNotFoundException($"VRPG scaling/default.json was not found below '{path}'.");
        }

        return path;
    }

    private static T Deserialize<T>(string path)
    {
        string json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<T>(json, JsonOptions)
            ?? throw new InvalidDataException($"'{path}' did not deserialize to {typeof(T).Name}.");
    }
}
