using VRPG.Client.Visuals;
using VRPG.Data.Definitions;
using Xunit;

namespace VRPG.Tests;

public sealed class VisualStyleResolverTests
{
    [Fact]
    public void ResolvesSkillColorAndParticles()
    {
        var skill = new SkillDefinition { Code = "vrpg:cinder", Color = "#f06a28", Radius = 3.2f };
        var resolver = new VisualStyleResolver(
            code => code == "vrpg:cinder" ? skill : null,
            _ => null);

        VisualStyle style = resolver.Resolve("vrpg:cinder", 0, radius: 0f);
        Assert.Equal(unchecked((int)0xfff06a28), style.ColorRgba);
        Assert.Same(skill.Particles, style.Particles);
        Assert.Equal(3.2f, style.Radius);
    }

    [Fact]
    public void FallsBackToStatusColor()
    {
        var status = new StatusEffectDefinition { Code = "vrpg:burn" };
        status.Visual.Color = "#b81c2e";
        var resolver = new VisualStyleResolver(_ => null, code => code == "vrpg:burn" ? status : null);

        VisualStyle style = resolver.Resolve("vrpg:burn", 0, radius: 1f);
        Assert.Equal(unchecked((int)0xffb81c2e), style.ColorRgba);
        Assert.Equal(1f, style.Radius);
    }

    [Fact]
    public void UnknownCodeUsesFallbackColorAndDefaultParticles()
    {
        var resolver = new VisualStyleResolver(_ => null, _ => null);
        VisualStyle style = resolver.Resolve("vrpg:not-authored-yet", unchecked((int)0x8a7cff66), radius: 2f);
        Assert.Equal(unchecked((int)0x8a7cff66), style.ColorRgba);
        Assert.NotNull(style.Particles);
    }

    [Fact]
    public void EmptyFallbackColorYieldsNeutralDefault()
    {
        var resolver = new VisualStyleResolver(_ => null, _ => null);
        VisualStyle style = resolver.Resolve("", 0, radius: 0.5f);
        Assert.NotEqual(0, style.ColorRgba);
    }
}
