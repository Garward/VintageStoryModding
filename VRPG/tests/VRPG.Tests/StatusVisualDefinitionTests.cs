using VRPG.Data.Definitions;
using Xunit;

namespace VRPG.Tests;

public sealed class StatusVisualDefinitionTests
{
    [Fact]
    public void StatusEffectDefinitionHasNonNullVisualDefaults()
    {
        var definition = new StatusEffectDefinition();
        Assert.NotNull(definition.Visual);
        Assert.Equal("", definition.Visual.Icon);
        Assert.Equal("", definition.Visual.Aura);
        Assert.True(definition.Visual.ShowStacks);
        Assert.Equal(1f, definition.Visual.AuraIntensityPerStack);
        Assert.Null(definition.Visual.Buildup);
    }

    [Fact]
    public void BuildupDefaultsSupportThresholdDisplay()
    {
        var buildup = new StatusBuildupVisualDefinition();
        Assert.True(buildup.ShowBar);
        Assert.Equal(100f, buildup.Threshold);
        Assert.True(buildup.FlashAtThreshold);
    }
}
