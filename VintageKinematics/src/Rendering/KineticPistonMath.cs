using System;

namespace VintageKinematics.Rendering
{
    /// <summary>
    /// Pure-math piston displacement helpers. Oscillating modes are a closed-form
    /// expression of world time + RPM (no state); directional mode integrates
    /// position over time and clamps to [0, travel].
    /// </summary>
    public static class KineticPistonMath
    {
        /// <summary>
        /// Sinusoidal piston: rest at 0, peaks at <paramref name="travel"/> mid-cycle.
        /// One full cycle per shaft revolution at ratio=1. Returns offset along the axis in 1/16ths.
        /// </summary>
        public static float OscillateSine(float timeSeconds, float rpm, float ratio, float phaseOffset, float travel)
        {
            float freqHz = MathF.Abs(rpm) * ratio / 60f;
            float angle = 2f * MathF.PI * freqHz * timeSeconds + phaseOffset;
            return travel * 0.5f * (1f - MathF.Cos(angle));
        }

        /// <summary>
        /// Triangle-wave piston: rest at 0, linear ramp to <paramref name="travel"/> at half-cycle, linear back to 0.
        /// One full cycle per shaft revolution at ratio=1.
        /// </summary>
        public static float OscillateTriangle(float timeSeconds, float rpm, float ratio, float phaseOffset, float travel)
        {
            float freqHz = MathF.Abs(rpm) * ratio / 60f;
            float phase = freqHz * timeSeconds + phaseOffset / (2f * MathF.PI);
            phase -= MathF.Floor(phase);
            return phase < 0.5f ? travel * 2f * phase : travel * 2f * (1f - phase);
        }

        /// <summary>
        /// Integrates directional piston position by <c>rpm·ratio/60·dt</c>, clamped to [0, travel].
        /// Positive RPM extends; negative retracts; <paramref name="invert"/> flips the convention.
        /// </summary>
        public static float AdvanceDirectional(float currentPos, float rpm, float ratio, float dtSeconds, float travel, bool invert)
        {
            float delta = rpm * ratio / 60f * dtSeconds;
            if (invert) delta = -delta;
            float next = currentPos + delta;
            if (next < 0f) next = 0f;
            if (next > travel) next = travel;
            return next;
        }
    }
}
