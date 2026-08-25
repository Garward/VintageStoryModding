using System;
using System.IO;
using Newtonsoft.Json.Linq;
using Xunit;

namespace VRPG.Tests;

public sealed class EntityAssetTests
{
    [Fact]
    public void ShippedEntitySidesDeclareBehaviorArrays()
    {
        string projectRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        string entityDirectory = Path.Combine(projectRoot, "assets/vrpg/entities");

        foreach (string file in Directory.GetFiles(entityDirectory, "*.json"))
        {
            var entity = JObject.Parse(File.ReadAllText(file));

            AssertBehaviorArray(entity, file, "client");
            AssertBehaviorArray(entity, file, "server");
        }
    }

    private static void AssertBehaviorArray(JObject entity, string file, string side)
    {
        JToken? sideDefinition = entity[side];
        if (sideDefinition is null)
        {
            return;
        }

        Assert.True(
            sideDefinition["behaviors"] is JArray,
            $"Entity asset {Path.GetFileName(file)} must declare {side}.behaviors as an array, even when empty.");
    }
}
