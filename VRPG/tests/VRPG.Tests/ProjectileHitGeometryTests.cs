using VRPG.Modules.Rpg.Skills;
using Vintagestory.API.MathTools;
using Xunit;

namespace VRPG.Tests;

public sealed class ProjectileHitGeometryTests
{
    private static readonly Cuboidf CreatureBox = new Cuboidf(-0.3f, 0f, -0.3f, 0.3f, 1.8f, 0.3f);
    private static readonly Vec3d CreaturePosition = new Vec3d(2, 0, 0);

    [Fact]
    public void SweptSegmentCannotTunnelThroughCreature()
    {
        bool hit = ProjectileHitGeometry.Intersects(
            new Vec3d(0, 1, 0),
            new Vec3d(4, 1, 0),
            CreatureBox,
            CreaturePosition,
            0.2,
            out double at);

        Assert.True(hit);
        Assert.InRange(at, 0.37, 0.38);
    }

    [Fact]
    public void AuthoredRadiusMatchesChunkyVisibleProjectile()
    {
        Vec3d start = new Vec3d(0, 1, 0.8);
        Vec3d end = new Vec3d(4, 1, 0.8);

        Assert.False(ProjectileHitGeometry.Intersects(start, end, CreatureBox, CreaturePosition, 0.2, out _));
        Assert.True(ProjectileHitGeometry.Intersects(start, end, CreatureBox, CreaturePosition, 0.55, out _));
    }

    [Fact]
    public void SegmentOutsideExpandedHitboxStillMisses()
    {
        Assert.False(ProjectileHitGeometry.Intersects(
            new Vec3d(0, 2.6, 0),
            new Vec3d(4, 2.6, 0),
            CreatureBox,
            CreaturePosition,
            0.55,
            out _));
    }
}
