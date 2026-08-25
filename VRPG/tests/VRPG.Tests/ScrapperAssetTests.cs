using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VRPG.Client.Visuals;
using VRPG.Data.Definitions;
using Xunit;

namespace VRPG.Tests;

public sealed class ScrapperAssetTests
{
    private static readonly string ProjectRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "../../../../../"));

    [Fact]
    public void ScrapperShipsTwoHeldBallisticSkills()
    {
        SkillDefinition junk = ReadSkill("junk_toss.json");
        SkillDefinition bomb = ReadSkill("scrap_bomb.json");

        Assert.Equal("vrpg:scrapper", junk.ClassCode);
        Assert.Equal("vrpg:scrapper", bomb.ClassCode);
        Assert.All(new[] { junk, bomb }, skill =>
        {
            Assert.Equal("projectile_aoe", skill.Delivery);
            Assert.Equal("targeted_release", skill.Timing.Mode);
            Assert.True(skill.Projectile.Ballistic);
            Assert.Equal("either", skill.Projectile.ImpactMode);
        });

        Assert.Equal(4, junk.Projectile.ModelVariants.Length);
        Assert.Equal(0.55f, junk.Projectile.CreatureCollisionRadius);
        Assert.Equal(0.65f, bomb.Projectile.CreatureCollisionRadius);
        Assert.Equal(5, junk.Charges.Maximum);
        Assert.True(junk.Timing.RepeatWhileHeld);
        Assert.Equal(0.35f, junk.Timing.HoldRepeatDelaySeconds);
        Assert.Equal(0.3f, junk.Timing.HoldRepeatIntervalSeconds);
        Assert.True(
            junk.Timing.HoldRepeatIntervalSeconds * (junk.Charges.Maximum - 1) < junk.CooldownSeconds,
            "The complete stored Junk Toss burst must finish before its first charge recovers.");
        Assert.All(junk.Projectile.ModelVariants, model => Assert.StartsWith("game:", model));
        Assert.Empty(bomb.Projectile.ModelVariants);
    }

    [Fact]
    public void ScrapBombBlastFillsItsFourMeterGameplayRadius()
    {
        SkillDefinition bomb = ReadSkill("scrap_bomb.json");
        SkillDefinition junk = ReadSkill("junk_toss.json");
        string presetPath = Path.Combine(ProjectRoot, "assets/vrpg/vrpg/fx/impact/scrap_blast.json");
        SkillFxPresetDefinition preset = JsonConvert.DeserializeObject<SkillFxPresetDefinition>(File.ReadAllText(presetPath))
            ?? throw new InvalidDataException("Could not deserialize " + presetPath);
        var resolver = new FxLayerResolver(code => code == preset.Code ? preset : null);

        ResolvedFxImpact bombImpact = resolver.Resolve(bomb.ImpactVisual, bomb.Radius, 1, 2);
        Assert.All(
            bombImpact.Layers.Where(layer => layer.Role is "fire" or "sparks" or "debris" or "dust"),
            layer => Assert.Equal(bomb.Radius, layer.Extent));
        Assert.True(bombImpact.Layers.Single(layer => layer.Role == "dust").OriginCoverage >= 0.45f);

        ResolvedFxImpact junkImpact = resolver.Resolve(junk.ImpactVisual, junk.Radius, 1, 2);
        Assert.All(
            junkImpact.Layers.Where(layer => layer.Role is "fire" or "sparks" or "debris" or "dust"),
            layer => Assert.True(layer.Extent < junk.Radius));
    }

    [Fact]
    public void GeneratedScrapBombIsRoundedAndSelfContained()
    {
        string path = Path.Combine(ProjectRoot, "assets/vrpg/shapes/entity/skill/scrap-bomb.json");
        var shape = JObject.Parse(File.ReadAllText(path));
        var elements = Assert.IsType<JArray>(shape["elements"]);
        var textures = Assert.IsType<JObject>(shape["textures"]);

        Assert.True(elements.Count >= 100);
        Assert.All(textures.Properties(), texture => Assert.StartsWith("game:", texture.Value.Value<string>()));
        Assert.Contains(elements, element => element?["name"]?.Value<string>()?.StartsWith("middle-shell-") == true);
        Assert.Contains(elements, element => element?["name"]?.Value<string>()?.StartsWith("rivet-") == true);
        Assert.Contains(elements, element => element?["name"]?.Value<string>()?.StartsWith("top-shoulder-") == true);
        Assert.Contains(elements, element => element?["name"]?.Value<string>()?.StartsWith("bottom-shoulder-") == true);
    }

    [Fact]
    public void GeneratedCircularBandsStaggerOverlappingCapPlanes()
    {
        string path = Path.Combine(ProjectRoot, "assets/vrpg/shapes/entity/skill/scrap-bomb.json");
        var shape = JObject.Parse(File.ReadAllText(path));
        var elements = Assert.IsType<JArray>(shape["elements"]);

        foreach (string prefix in new[]
                 {
                     "lower-shell-", "middle-shell-", "upper-shell-",
                     "bottom-shoulder-", "top-shoulder-",
                     "lower-copper-band-", "upper-copper-band-", "fuse-collar-"
                 })
        {
            JToken[] band = elements
                .Where(element => element?["name"]?.Value<string>()?.StartsWith(prefix) == true)
                .ToArray();
            Assert.NotEmpty(band);
            Assert.Equal(band.Length, band.Select(element => element["from"]?[1]?.Value<double>()).Distinct().Count());
            Assert.Equal(band.Length, band.Select(element => element["to"]?[1]?.Value<double>()).Distinct().Count());
        }
    }

    [Fact]
    public void BallisticCarrierUsesGravityOnBothSides()
    {
        string path = Path.Combine(ProjectRoot, "assets/vrpg/entities/skillballisticprojectile.json");
        var entity = JObject.Parse(File.ReadAllText(path));

        Assert.Equal(1f, GravityFactor(entity, "client"));
        Assert.Equal(1f, GravityFactor(entity, "server"));
    }

    private static float GravityFactor(JObject entity, string side)
    {
        JArray behaviors = Assert.IsType<JArray>(entity[side]?["behaviors"]);
        JToken physics = behaviors.Single(entry => entry?["code"]?.Value<string>() == "passivephysics");
        return physics["gravityFactor"]?.Value<float>() ?? -1f;
    }

    private static SkillDefinition ReadSkill(string name)
    {
        string path = Path.Combine(ProjectRoot, "assets/vrpg/vrpg/skills", name);
        return JsonConvert.DeserializeObject<SkillDefinition>(File.ReadAllText(path))
            ?? throw new InvalidDataException("Could not deserialize " + path);
    }
}
