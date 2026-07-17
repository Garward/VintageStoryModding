using System;

namespace VRPG.Modules.Rpg.Skills;

public static class MeleeTargetingMath
{
    public static bool IsWithinArc(
        double offsetX,
        double offsetZ,
        double forwardX,
        double forwardZ,
        double range,
        double targetRadius,
        double arcDegrees)
    {
        double distanceSquared = offsetX * offsetX + offsetZ * offsetZ;
        double allowedRange = Math.Max(0d, range) + Math.Max(0d, targetRadius);
        if (distanceSquared > allowedRange * allowedRange)
        {
            return false;
        }

        if (distanceSquared <= 0.000001d || arcDegrees >= 359.9d)
        {
            return true;
        }

        Normalize(ref forwardX, ref forwardZ);
        double inverseDistance = 1d / Math.Sqrt(distanceSquared);
        double dot = (offsetX * forwardX + offsetZ * forwardZ) * inverseDistance;
        return dot >= Math.Cos(Math.Clamp(arcDegrees, 0.1d, 360d) * Math.PI / 360d);
    }

    public static bool IsWithinLine(
        double offsetX,
        double offsetZ,
        double forwardX,
        double forwardZ,
        double range,
        double halfWidth,
        double targetRadius,
        out double projection,
        out double lateralDistanceSquared)
    {
        Normalize(ref forwardX, ref forwardZ);
        projection = offsetX * forwardX + offsetZ * forwardZ;
        double radius = Math.Max(0d, targetRadius);
        if (projection < -radius || projection > Math.Max(0d, range) + radius)
        {
            lateralDistanceSquared = double.MaxValue;
            return false;
        }

        double lateralX = offsetX - forwardX * projection;
        double lateralZ = offsetZ - forwardZ * projection;
        lateralDistanceSquared = lateralX * lateralX + lateralZ * lateralZ;
        double allowedWidth = Math.Max(0d, halfWidth) + radius;
        return lateralDistanceSquared <= allowedWidth * allowedWidth;
    }

    private static void Normalize(ref double x, ref double z)
    {
        double length = Math.Sqrt(x * x + z * z);
        if (length <= 0.000001d)
        {
            x = 0d;
            z = 1d;
            return;
        }

        x /= length;
        z /= length;
    }
}
