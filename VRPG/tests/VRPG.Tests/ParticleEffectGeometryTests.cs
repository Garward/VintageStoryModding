using VRPG.Client.Visuals;
using Xunit;

namespace VRPG.Tests;

public sealed class ParticleEffectGeometryTests
{
    [Fact]
    public void RingSamplesScaleWithEffectCircumference()
    {
        int reference = ParticleEffectGeometry.RingSamples(3f, 24f, 1f);
        int doubled = ParticleEffectGeometry.RingSamples(6f, 24f, 1f);

        Assert.Equal(24, reference);
        Assert.Equal(48, doubled);
    }

    [Fact]
    public void RingSamplesRespectVisualBudgetAndHardCap()
    {
        Assert.Equal(0, ParticleEffectGeometry.RingSamples(4f, 24f, 0f));
        Assert.Equal(96, ParticleEffectGeometry.RingSamples(32f, 300f, 1f));
    }

    [Fact]
    public void RadialTravelSpeedScalesLinearlyWithResolvedRadius()
    {
        float small = ParticleEffectGeometry.RadialSpeed(2f, 0.5f, 0.75f, 1f);
        float large = ParticleEffectGeometry.RadialSpeed(8f, 0.5f, 0.75f, 1f);

        Assert.Equal(small * 4f, large, 4);
    }

    [Fact]
    public void InteriorExtentCannotCrossGameplayRim()
    {
        float extent = ParticleEffectGeometry.InteriorExtent(2.8f, 0.75f, 1.35f, 0.9f, out bool clamped);
        float excessive = ParticleEffectGeometry.InteriorExtent(2.8f, 1f, 3f, 3f, out bool excessiveClamped);

        Assert.Equal(2.5515f, extent, 4);
        Assert.False(clamped);
        Assert.Equal(2.8f, excessive, 4);
        Assert.True(excessiveClamped);
    }

    [Fact]
    public void ProviderLifetimeUsesActualCalendarSpeed()
    {
        Assert.Equal(0.2f, ParticleEffectGeometry.ProviderLifetime(1f, 60), 4);
        Assert.Equal(0.4f, ParticleEffectGeometry.ProviderLifetime(1f, 240), 4);
    }
}
