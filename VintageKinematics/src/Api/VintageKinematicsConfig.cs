using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Util;

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

        /// <summary>
        /// Fixed VK RPM published by the vanilla MP bridge whenever the upstream axle is rotating
        /// (sign tracks vanilla direction). Static by design: vanilla MP speed jitters per tick
        /// with wind/water/load, and forwarding that magnitude would make MaxRPM-bound consumers
        /// constantly re-evaluate. Default 16.
        /// </summary>
        public float VanillaBridgeStableRPM { get; set; } = 16f;

        /// <summary>
        /// SU capacity per unit of upstream "source-potential torque" — the load-independent
        /// sum of <c>TorqueFactor × TargetSpeed</c> across rotor nodes on the connected vanilla
        /// network. Default 3333 puts a 4-sail max-wind windmill (potential ≈ 0.6) near 2000 SU
        /// and a 16-sail vertical mega-rotor at full wind near 8000 SU. Raise for a more
        /// vanilla-MP-dominant economy, lower to favor VK sources.
        /// </summary>
        public float VanillaBridgeCapacityPerTorque { get; set; } = 3333f;

        /// <summary>
        /// EMA smoothing factor for the bridge's per-tick torque reading. 0.15 ≈ 1.7s response
        /// time at the 4 Hz poll rate. Vanilla wind speed jitters every tick, which would
        /// otherwise swing displayed SU several-fold per second. Lower = smoother but slower to
        /// react to wind changes; higher = jumpier. Clamped to (0,1]; 1.0 disables smoothing.
        /// </summary>
        public float VanillaBridgeTorqueSmoothing { get; set; } = 0.15f;

        /// <summary>
        /// Master multiplier on every kinetic-sieve drop's stack size. Applies to both vanilla
        /// pannable rolls and custom <c>KineticSieveRecipe</c> outputs. 1.0 = vanilla. Fractional
        /// multipliers are resolved probabilistically so the long-run average matches: a yield of
        /// 1 with a 2.5x multiplier produces 3 items 50% of the time and 2 items 50% of the time.
        /// </summary>
        public float SieveYieldMultiplier { get; set; } = 1f;

        /// <summary>
        /// Per-output-item overrides keyed by the dropped item's full code (wildcards supported,
        /// e.g. <c>"game:nugget-*"</c> or <c>"game:gem-*-rough"</c>). Final yield multiplier =
        /// <see cref="SieveYieldMultiplier"/> × first matching entry. If no entry matches, only the
        /// global applies.
        /// </summary>
        public Dictionary<string, float> SieveYieldOverrides { get; set; } = new Dictionary<string, float>();

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

        /// <summary>
        /// Returns the effective sieve yield multiplier for a dropped item. Walks
        /// <see cref="SieveYieldOverrides"/> with wildcard matching and multiplies the first hit
        /// by the global. Null/empty code returns the global multiplier.
        /// </summary>
        public float ResolveSieveYield(AssetLocation droppedCode)
        {
            if (droppedCode == null || SieveYieldOverrides == null || SieveYieldOverrides.Count == 0)
                return SieveYieldMultiplier;
            string codeStr = droppedCode.ToString();
            foreach (var kvp in SieveYieldOverrides)
            {
                if (WildcardUtil.Match(kvp.Key, codeStr)) return SieveYieldMultiplier * kvp.Value;
            }
            return SieveYieldMultiplier;
        }
    }
}
