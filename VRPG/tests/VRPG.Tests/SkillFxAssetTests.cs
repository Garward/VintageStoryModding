using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using VRPG.Client.Visuals;
using VRPG.Data.Definitions;
using Xunit;

namespace VRPG.Tests;

public sealed class SkillFxAssetTests
{
    [Fact]
    public void MigratedSkyfallPresetKeepsImpactImmediateAndAddsSubtleTruthfulRim()
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        SkillDefinition skill = Read<SkillDefinition>(Path.Combine(root, "assets/vrpg/vrpg/skills/skyfall_anvil.json"));
        SkillFxPresetDefinition preset = Read<SkillFxPresetDefinition>(Path.Combine(root, "assets/vrpg/vrpg/fx/impact/stone_slam.json"));
        var resolver = new FxLayerResolver(code => code == preset.Code ? preset : null);

        ResolvedFxImpact impact = resolver.Resolve(skill.ImpactVisual, skill.Radius, 1, 2);
        Assert.Equal(84f, impact.Layers.Single(layer => layer.Role == "dust").Quantity);
        Assert.Equal(48f, impact.Layers.Single(layer => layer.Role == "debris").Quantity);
        Assert.Equal(32f, impact.Layers.Single(layer => layer.Role == "sparks").Quantity);
        Assert.Equal(34f, impact.Layers.Single(layer => layer.Role == "fire").Quantity);
        Assert.Equal(0f, impact.Layers.Single(layer => layer.Role == "dust").DelaySeconds);

        ResolvedFxLayer rim = impact.Layers.Single(layer => layer.Role == "rim");
        Assert.Equal(skill.Radius, rim.Extent);
        Assert.Equal(32f, rim.Quantity);
        Assert.Equal(0.135f, rim.LifetimeSeconds, 3);
        Assert.Equal(0x70, (rim.ColorRgba >> 24) & 0xff);
        Assert.False(skill.ImpactVisual.Shockwave);
        Assert.All(impact.Layers.Where(layer => layer.Role != "rim"), layer => Assert.True(layer.Extent <= skill.Radius));
    }

    private static T Read<T>(string path)
    {
        return JsonConvert.DeserializeObject<T>(File.ReadAllText(path))
            ?? throw new InvalidDataException("Could not deserialize " + path);
    }
}
