using VRPG.Modules.Rpg.Skills;
using Xunit;

namespace VRPG.Tests;

public sealed class MeleeTargetingMathTests
{
    [Fact]
    public void ForwardArcRejectsTargetsOutsideAuthoredAngle()
    {
        Assert.True(MeleeTargetingMath.IsWithinArc(0d, 2d, 0d, 1d, 2.5d, 0.3d, 70d));
        Assert.False(MeleeTargetingMath.IsWithinArc(2d, 0d, 0d, 1d, 2.5d, 0.3d, 70d));
        Assert.False(MeleeTargetingMath.IsWithinArc(0d, -1d, 0d, 1d, 2.5d, 0.3d, 70d));
    }

    [Fact]
    public void ArcRangeAccountsForTargetCollisionRadius()
    {
        Assert.True(MeleeTargetingMath.IsWithinArc(0d, 2.7d, 0d, 1d, 2.5d, 0.25d, 70d));
        Assert.False(MeleeTargetingMath.IsWithinArc(0d, 2.8d, 0d, 1d, 2.5d, 0.25d, 70d));
    }

    [Fact]
    public void ForwardLineUsesAuthoredLengthAndWidth()
    {
        Assert.True(MeleeTargetingMath.IsWithinLine(
            0.5d, 4d, 0d, 1d, 5d, 0.6d, 0.2d, out double projection, out _));
        Assert.Equal(4d, projection, 6);

        Assert.False(MeleeTargetingMath.IsWithinLine(
            1d, 4d, 0d, 1d, 5d, 0.6d, 0.2d, out _, out _));
        Assert.False(MeleeTargetingMath.IsWithinLine(
            0d, -1d, 0d, 1d, 5d, 0.6d, 0.2d, out _, out _));
    }
}
