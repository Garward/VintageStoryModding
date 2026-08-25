using System.Collections.Generic;
using VRPG.Client.Visuals;
using VRPG.Data.Definitions;
using Xunit;

namespace VRPG.Tests;

public sealed class FxLayerResolverTests
{
    private static readonly int SkillColor = unchecked((int)0xff112233);
    private static readonly int GroundColor = unchecked((int)0xff667788);

    [Fact]
    public void PresetOverrideMergesFieldWiseByRole()
    {
        SkillFxPresetDefinition preset = Preset();
        var effect = new SkillImpactVisualDefinition
        {
            Enabled = true,
            Preset = preset.Code,
            Overrides = new Dictionary<string, SkillFxLayerOverrideDefinition>
            {
                ["dust"] = new SkillFxLayerOverrideDefinition
                {
                    Quantity = 42f,
                    Color = "#88ccffff",
                    OriginCoverage = 0.4f
                }
            }
        };

        ResolvedFxLayer dust = Resolve(effect, preset).Layers[0];
        Assert.Equal(42f, dust.Quantity);
        Assert.Equal(0.5f, dust.LifetimeSeconds);
        Assert.Equal(0.4f, dust.OriginCoverage);
        Assert.Equal(unchecked((int)0xff88ccff), dust.ColorRgba);
    }

    [Fact]
    public void DirectLayersReplacePresetList()
    {
        SkillFxPresetDefinition preset = Preset();
        var direct = new SkillFxLayerDefinition { Role = "rim", Quantity = 18f };
        var effect = new SkillImpactVisualDefinition
        {
            Enabled = true,
            Preset = preset.Code,
            Layers = new[] { direct }
        };

        ResolvedFxImpact result = Resolve(effect, preset);
        Assert.Single(result.Layers);
        Assert.Equal("rim", result.Layers[0].Role);
    }

    [Fact]
    public void ColorTokensAndLiteralResolveAtImpact()
    {
        SkillFxPresetDefinition preset = Preset();
        preset.Layers = new[]
        {
            Layer("dust", "$ground"),
            Layer("sparks", "$skill"),
            Layer("custom", "#01020304")
        };
        var effect = new SkillImpactVisualDefinition { Enabled = true, Preset = preset.Code };

        ResolvedFxImpact result = Resolve(effect, preset);
        Assert.Equal(GroundColor, result.Layers[0].ColorRgba);
        Assert.True(result.Layers[0].ColorByGround);
        Assert.Equal(SkillColor, result.Layers[1].ColorRgba);
        Assert.Equal(unchecked((int)0x04010203), result.Layers[2].ColorRgba);
    }

    [Fact]
    public void RimIsExactImmediateAndCriticalInformation()
    {
        SkillFxPresetDefinition preset = Preset();
        preset.Layers = new[]
        {
            new SkillFxLayerDefinition
            {
                Role = "rim",
                Quantity = 24f,
                Coverage = 3f,
                DelaySeconds = 0.5f,
                Informative = false
            }
        };
        var effect = new SkillImpactVisualDefinition
        {
            Enabled = true,
            Preset = preset.Code,
            ExpansionSpeedScale = 3f,
            ParticleDurationScale = 3f
        };

        ResolvedFxLayer rim = Resolve(effect, preset, 7f).Layers[0];
        Assert.Equal(7f, rim.Extent);
        Assert.Equal(0f, rim.DelaySeconds);
        Assert.True(rim.Informative);
        Assert.False(rim.ExtentClamped);
    }

    [Fact]
    public void InteriorClampsAndLateInformationDemotesToDecoration()
    {
        SkillFxPresetDefinition preset = Preset();
        preset.Layers = new[]
        {
            new SkillFxLayerDefinition
            {
                Role = "dust",
                Quantity = 24f,
                Coverage = 0.8f,
                DelaySeconds = 0.3f,
                Informative = true
            }
        };
        var effect = new SkillImpactVisualDefinition
        {
            Enabled = true,
            Preset = preset.Code,
            ExpansionSpeedScale = 2f
        };

        ResolvedFxLayer layer = Resolve(effect, preset, 4f).Layers[0];
        Assert.Equal(4f, layer.Extent);
        Assert.True(layer.ExtentClamped);
        Assert.False(layer.Informative);
        Assert.Equal(0.3f, layer.DelaySeconds);
    }

    private static ResolvedFxImpact Resolve(
        SkillImpactVisualDefinition effect,
        SkillFxPresetDefinition preset,
        float radius = 3f)
    {
        return new FxLayerResolver(code => code == preset.Code ? preset : null)
            .Resolve(effect, radius, SkillColor, GroundColor);
    }

    private static SkillFxPresetDefinition Preset()
    {
        return new SkillFxPresetDefinition
        {
            Code = "vrpg:test",
            Layers = new[]
            {
                new SkillFxLayerDefinition
                {
                    Role = "dust",
                    Model = "quad",
                    Color = "$ground",
                    Quantity = 24f,
                    LifetimeSeconds = 0.5f,
                    Coverage = 0.65f
                }
            }
        };
    }

    private static SkillFxLayerDefinition Layer(string role, string color)
    {
        return new SkillFxLayerDefinition
        {
            Role = role,
            Model = "quad",
            Color = color,
            Quantity = 1f,
            LifetimeSeconds = 0.5f
        };
    }
}
