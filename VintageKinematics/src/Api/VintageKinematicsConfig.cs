using System.Collections.Generic;

namespace VintageKinematics.Api
{
    /// <summary>
    /// Server-owner-facing tunables. Loaded once by <see cref="KineticConfigSystem"/> from
    /// <c>ModConfig/vintagekinematics.json</c>. Two global dials — one for consumer speed and
    /// one for generator stress capacity — apply across every kinetic block. Per-block entries
    /// in <see cref="Consumers"/> and <see cref="Generators"/> override the global on a single
    /// block code (matched against <c>Block.Code.FirstCodePart()</c>, e.g. <c>"kineticquern"</c>
    /// or <c>"handcrank"</c>). Final multiplier = global × per-block (or just global when no
    /// per-block entry exists).
    /// </summary>
    public class VintageKinematicsConfig
    {
        /// <summary>Master speed multiplier applied to every consumer (worker / quern). 1.0 = vanilla.</summary>
        public float SpeedMultiplier { get; set; } = 1f;

        /// <summary>Master stress-unit multiplier applied to every generator's source capacity. 1.0 = vanilla.</summary>
        public float StressUnitMultiplier { get; set; } = 1f;

        /// <summary>Per-consumer-block speed overrides, keyed by block code (first code part).</summary>
        public Dictionary<string, ConsumerOverride> Consumers { get; set; } = new Dictionary<string, ConsumerOverride>();

        /// <summary>Per-generator-block stress overrides, keyed by block code (first code part).</summary>
        public Dictionary<string, GeneratorOverride> Generators { get; set; } = new Dictionary<string, GeneratorOverride>();

        public class ConsumerOverride
        {
            /// <summary>Speed multiplier for this consumer block (combined multiplicatively with the global).</summary>
            public float SpeedMultiplier { get; set; } = 1f;
        }

        public class GeneratorOverride
        {
            /// <summary>Stress capacity multiplier for this generator block (combined multiplicatively with the global).</summary>
            public float StressUnitMultiplier { get; set; } = 1f;
        }

        /// <summary>Returns the effective speed multiplier for a consumer block code. Falls back to the global when no per-block override exists.</summary>
        public float ResolveConsumerSpeed(string blockCode)
        {
            if (Consumers != null && blockCode != null && Consumers.TryGetValue(blockCode, out var o) && o != null)
                return SpeedMultiplier * o.SpeedMultiplier;
            return SpeedMultiplier;
        }

        /// <summary>Returns the effective stress-capacity multiplier for a generator block code. Falls back to the global when no per-block override exists.</summary>
        public float ResolveGeneratorStress(string blockCode)
        {
            if (Generators != null && blockCode != null && Generators.TryGetValue(blockCode, out var o) && o != null)
                return StressUnitMultiplier * o.StressUnitMultiplier;
            return StressUnitMultiplier;
        }
    }
}
