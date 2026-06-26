using Vintagestory.API.MathTools;

namespace VintageKinematics.Api
{
    /// <summary>
    /// Settings for evaluating a kinetic-network condition. Threshold units depend on
    /// <see cref="Type"/>: percent conditions use 0..100, SU conditions use stress units,
    /// and RPM conditions use absolute RPM.
    /// </summary>
    public class KineticConditionSettings
    {
        public KineticConditionType Type { get; set; } = KineticConditionType.Overstressed;

        /// <summary>Main condition threshold. Percent values use 0..100.</summary>
        public float Threshold { get; set; } = 90f;

        /// <summary>
        /// Reset gap for threshold conditions. Percent values use percent points; SU/RPM values
        /// use the same unit as <see cref="Threshold"/>. Prevents flicker near the threshold.
        /// </summary>
        public float Hysteresis { get; set; } = 5f;

        public KineticConditionSettings() { }

        public KineticConditionSettings(KineticConditionType type, float threshold = 0f, float hysteresis = 0f)
        {
            Type = type;
            Threshold = threshold;
            Hysteresis = hysteresis;
        }

        public float ClampedHysteresis()
        {
            if (float.IsNaN(Hysteresis) || float.IsInfinity(Hysteresis)) return 0f;
            return GameMath.Max(0f, Hysteresis);
        }
    }
}
