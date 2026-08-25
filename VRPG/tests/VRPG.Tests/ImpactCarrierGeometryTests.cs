using VRPG.Client.Visuals;
using Vintagestory.API.MathTools;
using Xunit;

namespace VRPG.Tests;

public sealed class ImpactCarrierGeometryTests
{
    [Fact]
    public void AirborneCarrierCannotReleaseGroundImpactEarly()
    {
        var effect = new Vec3d(12, 3.12, -4);

        Assert.False(ImpactCarrierGeometry.ReachedImpact(new Vec3d(7, 8, -4), effect));
        Assert.False(ImpactCarrierGeometry.ReachedImpact(new Vec3d(10.5, 3, -4), effect));
    }

    [Fact]
    public void CarrierAtVisualContactReleasesImpact()
    {
        var effect = new Vec3d(12, 3.12, -4);

        Assert.True(ImpactCarrierGeometry.ReachedImpact(new Vec3d(12, 3, -4), effect));
        Assert.True(ImpactCarrierGeometry.ReachedImpact(new Vec3d(12.3, 3.1, -4.2), effect));
    }
}
