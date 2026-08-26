using System;

namespace VintageKinematics.Api
{
    internal static class QuernSpeedPolicy
    {
        private const float ReferenceRpm = 32f;
        internal const float MaxClientVisualSpeed = 2f;

        internal static float ProcessingSpeed(float rpm, float speedMultiplier)
        {
            return MathF.Max(0f, rpm) / ReferenceRpm * MathF.Max(0f, speedMultiplier);
        }

        internal static float ClientVisualSpeed(float processingSpeed)
        {
            return MathF.Min(MathF.Max(0f, processingSpeed), MaxClientVisualSpeed);
        }
    }
}
