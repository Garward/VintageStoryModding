using System;

namespace VRPG.Client.Visuals;

/// <summary>
/// Shared spatial rules for radius-bound particle effects. The event packet's
/// resolved radius is authoritative; authored particle values describe the
/// appearance at the reference radius and remain bounded as areas grow.
/// </summary>
public static class ParticleEffectGeometry
{
    public const float ReferenceRadius = 3f;

    public static float EffectRadius(float resolvedRadius)
    {
        return Math.Max(0.1f, resolvedRadius);
    }

    public static int RingSamples(
        float resolvedRadius,
        float authoredQuantity,
        float quantityScale,
        int minimum = 12,
        int maximum = 96)
    {
        if (authoredQuantity <= 0f || quantityScale <= 0f)
        {
            return 0;
        }

        float circumferenceScale = EffectRadius(resolvedRadius) / ReferenceRadius;
        int samples = (int)Math.Ceiling(authoredQuantity * circumferenceScale * quantityScale);
        return Math.Clamp(samples, minimum, maximum);
    }

    public static float RadialSpeed(
        float resolvedRadius,
        float intendedLifetimeSeconds,
        float radiusCoverage,
        float authoredSpeedScale)
    {
        float lifetime = Math.Max(0.08f, intendedLifetimeSeconds);
        float coverage = Math.Max(0f, radiusCoverage);
        float speedScale = Math.Max(0f, authoredSpeedScale);
        return EffectRadius(resolvedRadius) * coverage / lifetime * speedScale;
    }

    public static float InteriorExtent(
        float resolvedRadius,
        float coverage,
        float expansionSpeedScale,
        float particleDurationScale,
        out bool clamped)
    {
        float requestedRatio = Math.Max(0f, coverage)
            * Math.Max(0f, expansionSpeedScale)
            * Math.Max(0f, particleDurationScale);
        clamped = requestedRatio > 1f;
        return EffectRadius(resolvedRadius) * Math.Min(1f, requestedRatio);
    }

    public static float ProviderLifetime(float intendedRealSeconds, double calendarSpeed)
    {
        double speed = Math.Max(0.001, calendarSpeed);
        double engineMultiplier = 5d / Math.Sqrt(speed / 60d);
        return (float)(Math.Max(0f, intendedRealSeconds) / engineMultiplier);
    }

    public static float OriginSpread(
        float resolvedRadius,
        float radiusFraction,
        float minimum,
        float maximum)
    {
        return Math.Clamp(EffectRadius(resolvedRadius) * radiusFraction, minimum, maximum);
    }
}
