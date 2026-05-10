using System;
using Vintagestory.API.MathTools;

namespace VintageKinematics.Api
{
    /// <summary>
    /// World-aligned shaft axis a kinetic block presents.
    /// X / Y / Z are mutually perpendicular world axes; Y is "up".
    /// </summary>
    public enum EnumKineticAxis : byte
    {
        /// <summary>East-west.</summary>
        X = 0,
        /// <summary>Vertical.</summary>
        Y = 1,
        /// <summary>North-south.</summary>
        Z = 2
    }

    /// <summary>Helpers for converting between <see cref="EnumKineticAxis"/> and unit vectors.</summary>
    public static class EnumKineticAxisExtensions
    {
        /// <summary>Returns the unit vector pointing along <paramref name="axis"/>.</summary>
        public static Vec3i UnitVector(EnumKineticAxis axis)
        {
            switch (axis)
            {
                case EnumKineticAxis.X: return new Vec3i(1, 0, 0);
                case EnumKineticAxis.Y: return new Vec3i(0, 1, 0);
                case EnumKineticAxis.Z: return new Vec3i(0, 0, 1);
                default: throw new ArgumentOutOfRangeException(nameof(axis));
            }
        }

        /// <summary>True iff <paramref name="a"/> and <paramref name="b"/> are different axes.</summary>
        public static bool IsPerpendicular(EnumKineticAxis a, EnumKineticAxis b)
        {
            return a != b;
        }

        /// <summary>Returns the axis perpendicular to both <paramref name="a"/> and <paramref name="b"/>.</summary>
        public static EnumKineticAxis ThirdAxis(EnumKineticAxis a, EnumKineticAxis b)
        {
            if (a == b) throw new ArgumentException("Axes must be perpendicular");
            int sum = (int)a + (int)b;
            return (EnumKineticAxis)(3 - sum);
        }

        /// <summary>Resolves an axis-aligned unit vector to its <see cref="EnumKineticAxis"/>.</summary>
        public static EnumKineticAxis FromVec(Vec3i v)
        {
            if (v.X != 0 && v.Y == 0 && v.Z == 0) return EnumKineticAxis.X;
            if (v.X == 0 && v.Y != 0 && v.Z == 0) return EnumKineticAxis.Y;
            if (v.X == 0 && v.Y == 0 && v.Z != 0) return EnumKineticAxis.Z;
            throw new ArgumentException($"Vec is not axis-aligned: {v}");
        }
    }
}
