using Vintagestory.API.MathTools;

namespace VRPG.Client.Visuals;

/// <summary>Contact test shared by dropped and ballistic visual carriers.</summary>
public static class ImpactCarrierGeometry
{
    public const double EffectVerticalOffset = 0.12;
    public const double ContactTolerance = 0.55;

    public static bool ReachedImpact(Vec3d carrierPosition, Vec3d effectPosition)
    {
        double dx = carrierPosition.X - effectPosition.X;
        double dy = carrierPosition.Y - (effectPosition.Y - EffectVerticalOffset);
        double dz = carrierPosition.Z - effectPosition.Z;
        return dx * dx + dy * dy + dz * dz <= ContactTolerance * ContactTolerance;
    }
}
