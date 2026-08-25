using System;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace VRPG.Modules.Rpg.Skills;

/// <summary>Shared launch and preview math for passive-physics projectiles.</summary>
public static class BallisticTrajectory
{
    public const double PhysicsStepsPerSecond = 60d;

    public static BallisticSolution Solve(
        Vec3d start,
        Vec3d target,
        float horizontalMotion,
        float minimumFlightSeconds)
    {
        double dx = target.X - start.X;
        double dy = target.Y - start.Y;
        double dz = target.Z - start.Z;
        double horizontalDistance = Math.Sqrt(dx * dx + dz * dz);
        double speed = Math.Max(0.001, horizontalMotion);
        double flightSeconds = Math.Max(
            Math.Max(0.2f, minimumFlightSeconds),
            horizontalDistance / (speed * PhysicsStepsPerSecond));
        double divisor = PhysicsStepsPerSecond * flightSeconds;
        var motion = new Vec3d(
            dx / divisor,
            dy / divisor + 0.5d * GlobalConstants.GravityPerSecond * flightSeconds,
            dz / divisor);
        return new BallisticSolution(motion, (float)flightSeconds);
    }

    public static Vec3d Position(Vec3d start, Vec3d initialMotion, double seconds)
    {
        double t = Math.Max(0d, seconds);
        return new Vec3d(
            start.X + initialMotion.X * PhysicsStepsPerSecond * t,
            start.Y + initialMotion.Y * PhysicsStepsPerSecond * t
                - 0.5d * GlobalConstants.GravityPerSecond * PhysicsStepsPerSecond * t * t,
            start.Z + initialMotion.Z * PhysicsStepsPerSecond * t);
    }
}

public readonly record struct BallisticSolution(Vec3d InitialMotion, float FlightSeconds);
