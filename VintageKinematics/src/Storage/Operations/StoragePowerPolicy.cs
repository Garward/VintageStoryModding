using System;

namespace VintageKinematics.Storage.Operations
{
    /// <summary>Pure warehouse power threshold shared by runtime code and tests.</summary>
    public static class StoragePowerPolicy
    {
        public static float NormalizeMinimumRPM(float configured)
        {
            return float.IsFinite(configured) ? MathF.Max(0f, configured) : 16f;
        }

        public static bool IsPowered(bool required, float minimumRPM, params float[] driveRPMs)
        {
            if (!required) return true;
            float threshold = NormalizeMinimumRPM(minimumRPM);
            if (driveRPMs == null) return false;
            foreach (float rpm in driveRPMs)
            {
                if (float.IsFinite(rpm) && MathF.Abs(rpm) >= threshold) return true;
            }
            return false;
        }

        public static float CalculateStressImpact(
            bool required,
            float baseImpact,
            float impactPerCell,
            int capacityCellCount,
            float multiplier = 1f)
        {
            if (!required) return 0f;
            float safeBase = float.IsFinite(baseImpact) ? MathF.Max(0f, baseImpact) : 16f;
            float safePerCell = float.IsFinite(impactPerCell) ? MathF.Max(0f, impactPerCell) : 0.25f;
            float safeMultiplier = float.IsFinite(multiplier) ? MathF.Max(0f, multiplier) : 1f;
            return (safeBase + safePerCell * Math.Max(0, capacityCellCount)) * safeMultiplier;
        }
    }
}
