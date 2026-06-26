using System;
using Vintagestory.API.MathTools;
using VintageKinematics.Network;

namespace VintageKinematics.Api
{
    /// <summary>
    /// Shared evaluator for kinetic automation conditions. It intentionally contains no block
    /// or packet logic so sensors, clutches, activators, and JSON-backed machines can all reuse
    /// the same rules.
    /// </summary>
    public static class KineticConditionEvaluator
    {
        public static bool Evaluate(IKineticNetworkInfo network, KineticConditionSettings settings, bool previousState = false)
        {
            settings ??= new KineticConditionSettings();

            return settings.Type switch
            {
                KineticConditionType.HasPower => HasPower(network),
                KineticConditionType.NoPower => !HasPower(network),
                KineticConditionType.Overstressed => network?.IsOverstressed ?? false,
                KineticConditionType.Conflicted => network?.IsConflicted ?? false,
                KineticConditionType.Blocked => network?.IsOverstressed == true || network?.IsConflicted == true,
                KineticConditionType.StressAbovePercent => EvaluateAbove(StressPercent(network), settings.Threshold, settings.ClampedHysteresis(), previousState),
                KineticConditionType.StressBelowPercent => EvaluateBelow(StressPercent(network), settings.Threshold, settings.ClampedHysteresis(), previousState),
                KineticConditionType.StressAboveSu => EvaluateAbove(network?.StressTotal ?? 0f, settings.Threshold, settings.ClampedHysteresis(), previousState),
                KineticConditionType.StressBelowSu => EvaluateBelow(network?.StressTotal ?? 0f, settings.Threshold, settings.ClampedHysteresis(), previousState),
                KineticConditionType.CapacityAboveSu => EvaluateAbove(network?.StressCapacity ?? 0f, settings.Threshold, settings.ClampedHysteresis(), previousState),
                KineticConditionType.CapacityBelowSu => EvaluateBelow(network?.StressCapacity ?? 0f, settings.Threshold, settings.ClampedHysteresis(), previousState),
                KineticConditionType.RpmAbove => EvaluateAbove(AbsRpm(network), settings.Threshold, settings.ClampedHysteresis(), previousState),
                KineticConditionType.RpmBelow => EvaluateBelow(AbsRpm(network), settings.Threshold, settings.ClampedHysteresis(), previousState),
                _ => false
            };
        }

        public static float StressPercent(IKineticNetworkInfo network)
        {
            if (network == null || network.StressCapacity <= 0f) return network?.StressTotal > 0f ? float.PositiveInfinity : 0f;
            return network.StressTotal / network.StressCapacity * 100f;
        }

        public static bool HasPower(IKineticNetworkInfo network)
        {
            return network != null
                && !network.IsConflicted
                && !network.IsOverstressed
                && MathF.Abs(network.SourceRPM) >= KineticNetwork.MinAbsRPM;
        }

        private static float AbsRpm(IKineticNetworkInfo network)
        {
            return MathF.Abs(network?.SourceRPM ?? 0f);
        }

        private static bool EvaluateAbove(float value, float threshold, float hysteresis, bool previousState)
        {
            if (float.IsNaN(value)) value = 0f;
            if (!previousState) return value >= threshold;
            return value >= threshold - hysteresis;
        }

        private static bool EvaluateBelow(float value, float threshold, float hysteresis, bool previousState)
        {
            if (float.IsNaN(value)) value = 0f;
            if (!previousState) return value <= threshold;
            return value <= threshold + hysteresis;
        }
    }
}
