using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using VRPG.Data;
using VRPG.Data.Definitions;
using VRPG.Modules.Dungeons.Layout;
using Xunit;

namespace VRPG.Tests;

public sealed class DungeonAssetTests
{
    [Fact]
    public void ShippedGranitePoolDeserializesValidatesAndGenerates()
    {
        string projectRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        DungeonThemeDefinition theme = Read<DungeonThemeDefinition>(
            Path.Combine(projectRoot, "assets/vrpg/vrpg/dungeons/granite_halls.json"));
        List<DungeonRoomDefinition> rooms = ReadDirectory<DungeonRoomDefinition>(
            Path.Combine(projectRoot, "assets/vrpg/vrpg/rooms"));
        List<DungeonEncounterDefinition> encounters = ReadDirectory<DungeonEncounterDefinition>(
            Path.Combine(projectRoot, "assets/vrpg/vrpg/encounters"));

        Assert.Empty(DungeonContentValidator.CollectErrors(new[] { theme }, rooms, encounters));

        var generator = new DungeonLayoutGenerator();
        for (ulong seed = 1; seed <= 100; seed++)
        {
            DungeonLayout layout = generator.Generate(theme, rooms, seed);
            Assert.Empty(DungeonLayoutValidator.CollectErrors(theme, layout));
        }
    }

    private static List<T> ReadDirectory<T>(string path)
    {
        var result = new List<T>();
        foreach (string file in Directory.GetFiles(path, "*.json"))
        {
            result.Add(Read<T>(file));
        }

        return result;
    }

    private static T Read<T>(string path)
    {
        string json = File.ReadAllText(path);
        return JsonConvert.DeserializeObject<T>(json)
            ?? throw new InvalidDataException($"Could not deserialize {path}.");
    }
}
