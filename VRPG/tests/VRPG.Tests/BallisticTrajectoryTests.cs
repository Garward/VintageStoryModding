using VRPG.Modules.Rpg.Skills;
using Vintagestory.API.MathTools;
using Xunit;

namespace VRPG.Tests;

public sealed class BallisticTrajectoryTests
{
    [Fact]
    public void SolvedTrajectoryEndsAtAuthoredTarget()
    {
        var start = new Vec3d(2.5, 4.25, -3.0);
        var target = new Vec3d(20.0, 1.0, 8.5);

        BallisticSolution solution = BallisticTrajectory.Solve(start, target, 0.28f, 0.5f);
        Vec3d end = BallisticTrajectory.Position(start, solution.InitialMotion, solution.FlightSeconds);

        Assert.Equal(target.X, end.X, 6);
        Assert.Equal(target.Y, end.Y, 6);
        Assert.Equal(target.Z, end.Z, 6);
    }

    [Fact]
    public void MinimumFlightTimeKeepsCloseThrowsReadable()
    {
        var start = new Vec3d(0, 2, 0);
        var target = new Vec3d(1, 0, 0);

        BallisticSolution solution = BallisticTrajectory.Solve(start, target, 0.3f, 0.65f);

        Assert.Equal(0.65f, solution.FlightSeconds, 3);
        Assert.True(BallisticTrajectory.Position(start, solution.InitialMotion, 0.325).Y > target.Y);
    }
}
