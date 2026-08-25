using System;
using Vintagestory.API.MathTools;

namespace VRPG.Modules.Rpg.Skills;

/// <summary>Continuous projectile intersection against an expanded entity hitbox.</summary>
public static class ProjectileHitGeometry
{
    public static bool Intersects(
        Vec3d start,
        Vec3d end,
        Cuboidf relativeTargetBox,
        Vec3d targetPosition,
        double projectileRadius,
        out double at)
    {
        double radius = Math.Max(0.0, projectileRadius);
        return SegmentIntersectsBox(
            start,
            end,
            targetPosition.X + relativeTargetBox.X1 - radius,
            targetPosition.Y + relativeTargetBox.Y1 - radius,
            targetPosition.Z + relativeTargetBox.Z1 - radius,
            targetPosition.X + relativeTargetBox.X2 + radius,
            targetPosition.Y + relativeTargetBox.Y2 + radius,
            targetPosition.Z + relativeTargetBox.Z2 + radius,
            out at);
    }

    private static bool SegmentIntersectsBox(
        Vec3d start,
        Vec3d end,
        double minX,
        double minY,
        double minZ,
        double maxX,
        double maxY,
        double maxZ,
        out double at)
    {
        double enter = 0.0;
        double exit = 1.0;
        if (!ClipAxis(start.X, end.X - start.X, minX, maxX, ref enter, ref exit)
            || !ClipAxis(start.Y, end.Y - start.Y, minY, maxY, ref enter, ref exit)
            || !ClipAxis(start.Z, end.Z - start.Z, minZ, maxZ, ref enter, ref exit))
        {
            at = 0.0;
            return false;
        }

        at = enter;
        return true;
    }

    private static bool ClipAxis(double origin, double delta, double minimum, double maximum, ref double enter, ref double exit)
    {
        if (Math.Abs(delta) < 0.000001)
        {
            return origin >= minimum && origin <= maximum;
        }

        double first = (minimum - origin) / delta;
        double second = (maximum - origin) / delta;
        if (first > second) (first, second) = (second, first);
        enter = Math.Max(enter, first);
        exit = Math.Min(exit, second);
        return enter <= exit;
    }
}
