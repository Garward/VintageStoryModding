using System;
using VRPG.Network;

namespace VRPG.Modules.Rpg.Movement;

public readonly record struct MovementVector(double X, double Y, double Z);

public static class MovementEffectMath
{
    public static MovementVector Apply(
        MovementVector current,
        MovementVector requested,
        MovementEffectMode mode,
        MovementEffectAxes axes,
        float responsePerSecond,
        float deltaSeconds)
    {
        double x = Resolve(current.X, requested.X, mode, axes.HasFlag(MovementEffectAxes.X), responsePerSecond, deltaSeconds);
        double y = Resolve(current.Y, requested.Y, mode, axes.HasFlag(MovementEffectAxes.Y), responsePerSecond, deltaSeconds);
        double z = Resolve(current.Z, requested.Z, mode, axes.HasFlag(MovementEffectAxes.Z), responsePerSecond, deltaSeconds);
        return new MovementVector(x, y, z);
    }

    private static double Resolve(
        double current,
        double requested,
        MovementEffectMode mode,
        bool ownsAxis,
        float responsePerSecond,
        float deltaSeconds)
    {
        if (!ownsAxis)
        {
            return current;
        }

        return mode switch
        {
            MovementEffectMode.SetVelocity => requested,
            MovementEffectMode.AddImpulse => current + requested,
            MovementEffectMode.BlendVelocity => current + (requested - current)
                * (1d - Math.Exp(-Math.Clamp(responsePerSecond, 0.01f, 100f) * Math.Max(0f, deltaSeconds))),
            _ => current
        };
    }
}
