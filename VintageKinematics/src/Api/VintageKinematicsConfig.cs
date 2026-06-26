using System.Collections.Generic;
using System;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
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
        /// How VK should translate vanilla mechanical-power sources.
        /// "dynamic" keeps the current live torque tracking. "sampledStatic" samples the
        /// vanilla source capacity when the bridge node is built and then keeps that rating
        /// stable until the VK network is rebuilt. "fixed" gives every rotating vanilla bridge
        /// the configured <see cref="VanillaBridgeFixedSU"/> capacity. "disabled" prevents VK
        /// from bridging vanilla mechanical power.
        /// </summary>
        public string VanillaBridgeMode { get; set; } = "dynamic";

        /// <summary>
        /// Fixed stress capacity used when <see cref="VanillaBridgeMode"/> is "fixed".
        /// This is total SU capacity at <see cref="VanillaBridgeStableRPM"/>.
        /// </summary>
        public float VanillaBridgeFixedSU { get; set; } = 2000f;

        /// <summary>
        /// How often VK polls vanilla bridge sources. Dynamic mode benefits from the default
        /// 250 ms value; fixed/sample modes can safely be raised on large servers if delayed
        /// stop/start detection is acceptable.
        /// </summary>
        public int VanillaBridgePollIntervalMs { get; set; } = 250;

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
        /// If true, sieves roll the loaded vanilla BlockPan panning drops for sand/gravel-like
        /// blocks. If false, only explicit VK sieve recipes run. Useful when another mod heavily
        /// rewrites panning and a pack wants to keep that rewrite out of kinetic automation.
        /// </summary>
        public bool UseVanillaPanningDrops { get; set; } = true;

        /// <summary>
        /// Targets the Kinetic Activator must not trigger. Entries support wildcards and match
        /// block codes, block class names, block entity class names, and JSON entityClass names.
        /// Examples: <c>"game:commandblock-*"</c>, <c>"Ticker"</c>, <c>"BlockEntityTicker"</c>,
        /// <c>"Vintagestory.GameContent.BlockEntityCommands"</c>.
        /// </summary>
        public List<string> KineticActivatorTargetBlacklist { get; set; } = new List<string>
        {
            "game:commandblock-*",
            "game:initcommandblock-*",
            "game:tickerblock-*",
            "game:conditionalblock-*",
            "commandblock",
            "initcommandblock",
            "tickerblock",
            "conditionalblock",
            "GuiConfigurableCommands",
            "Commands",
            "Ticker",
            "Conditional",
            "BlockCommand",
            "BlockTicker",
            "BlockEntityGuiConfigurableCommands",
            "BlockEntityCommands",
            "BlockEntityTicker",
            "BlockEntityConditional",
            "Vintagestory.GameContent.BlockEntityGuiConfigurableCommands",
            "Vintagestory.GameContent.BlockEntityCommands",
            "Vintagestory.GameContent.BlockEntityTicker",
            "Vintagestory.GameContent.BlockEntityConditional"
        };

        /// <summary>
        /// Extra multiplier applied only to primitive-sieve drops produced from vanilla panning
        /// rolls. Multiplies with <see cref="SieveYieldMultiplier"/> and matching
        /// <see cref="SieveYieldOverrides"/>. Custom VK sieve recipes are unaffected.
        /// </summary>
        public float PrimitiveSievePanningYieldMultiplier { get; set; } = 1f;

        /// <summary>
        /// Extra multiplier applied only to kinetic-sieve drops produced from vanilla panning
        /// rolls. Multiplies with <see cref="SieveYieldMultiplier"/> and matching
        /// <see cref="SieveYieldOverrides"/>. Custom VK sieve recipes are unaffected.
        /// </summary>
        public float KineticSievePanningYieldMultiplier { get; set; } = 1f;

        /// <summary>
        /// Fuel burn-rate multiplier for the kinetic forge press. 1.0 = normal fuel duration,
        /// 2.0 = fuel burns twice as fast, 0.5 = fuel lasts twice as long.
        /// </summary>
        public float ForgePressFuelUsageSpeed { get; set; } = 1f;

        /// <summary>
        /// If true, the forge press may use an opt-in compatibility recipe that smelts
        /// non-vanilla <c>nugget-*</c> items from enabled mod domains by reading their vanilla
        /// combustible smelting data. Defaults false because some metallurgy mods intentionally
        /// route nuggets through custom processing chains.
        /// </summary>
        public bool ForgePressEnableModdedNuggetSmelting { get; set; } = false;

        /// <summary>
        /// Per-mod-domain gates for the modded nugget smelting compatibility recipe. The generated
        /// config seeds common metallurgy mod ids as false examples; set a domain to true after
        /// confirming that mod's nuggets have ordinary combustible smelting data and no special
        /// processing requirement.
        /// </summary>
        public Dictionary<string, bool> ForgePressModdedNuggetSmeltingMods { get; set; } = new Dictionary<string, bool>();

        /// <summary>
        /// Optional fine-grained allow-list for modded nugget smelting. Entries support wildcards
        /// against full item codes, e.g. <c>"improvedmetallurgy:nugget-*"</c>. This still requires
        /// <see cref="ForgePressEnableModdedNuggetSmelting"/> to be true.
        /// </summary>
        public List<string> ForgePressModdedNuggetSmeltingAllowedPatterns { get; set; } = new List<string>();

        /// <summary>
        /// Fuel burn-rate multiplier for the coal motor. 1.0 = normal fuel duration,
        /// 2.0 = fuel burns twice as fast, 0.5 = fuel lasts twice as long.
        /// </summary>
        public float CoalMotorFuelUsageSpeed { get; set; } = 1f;

        /// <summary>
        /// Enables titanium powered drill 3x3 and 5x5 mining modes. Set false on servers that
        /// want to avoid large accidental or unattended area-mining bursts; 1x1 drilling remains
        /// available.
        /// </summary>
        public bool EnableTitaniumDrillWideMining { get; set; } = true;

        /// <summary>
        /// Per-output-item overrides keyed by the dropped item's full code (wildcards supported,
        /// e.g. <c>"game:nugget-*"</c> or <c>"game:gem-*-rough"</c>). Final yield multiplier =
        /// <see cref="SieveYieldMultiplier"/> × first matching entry. If no entry matches, only the
        /// global applies.
        /// </summary>
        public Dictionary<string, float> SieveYieldOverrides { get; set; } = new Dictionary<string, float>();

        /// <summary>Per-consumer-block speed and stress-demand overrides, keyed by block code (first code part).</summary>
        public Dictionary<string, ConsumerOverride> Consumers { get; set; } = new Dictionary<string, ConsumerOverride>();

        /// <summary>Per-generator-block stress overrides, keyed by block code (first code part).</summary>
        public Dictionary<string, GeneratorOverride> Generators { get; set; } = new Dictionary<string, GeneratorOverride>();

        public class ConsumerOverride
        {
            /// <summary>Speed multiplier for this consumer block (combined multiplicatively with the global).</summary>
            public float SpeedMultiplier { get; set; } = 1f;

            /// <summary>Stress demand multiplier for this consumer block. 1.0 = JSON stressImpact.</summary>
            public float StressUnitMultiplier { get; set; } = 1f;
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

        /// <summary>Returns the effective stress-demand multiplier for a consumer block code.</summary>
        public float ResolveConsumerStress(string blockCode)
        {
            if (Consumers != null && blockCode != null && Consumers.TryGetValue(blockCode, out var o) && o != null)
                return ClampStressMultiplier(o.StressUnitMultiplier);
            return 1f;
        }

        /// <summary>Returns the effective stress-capacity multiplier for a generator block code. Falls back to the global when no per-block override exists.</summary>
        public float ResolveGeneratorStress(string blockCode)
        {
            if (Generators != null && blockCode != null && Generators.TryGetValue(blockCode, out var o) && o != null)
                return ClampStressMultiplier(StressUnitMultiplier * o.StressUnitMultiplier);
            return ClampStressMultiplier(StressUnitMultiplier);
        }

        /// <summary>
        /// Returns the effective sieve yield multiplier for a dropped item. Walks
        /// <see cref="SieveYieldOverrides"/> with wildcard matching and multiplies the first hit
        /// by the global. Null/empty code returns the global multiplier.
        /// </summary>
        public float ResolveSieveYield(AssetLocation droppedCode)
        {
            return ResolveSieveYield(droppedCode, 1f);
        }

        public float ResolveSieveYield(AssetLocation droppedCode, float sourceMultiplier)
        {
            if (droppedCode == null || SieveYieldOverrides == null || SieveYieldOverrides.Count == 0)
                return ClampYieldMultiplier(SieveYieldMultiplier * sourceMultiplier);
            string codeStr = droppedCode.ToString();
            foreach (var kvp in SieveYieldOverrides)
            {
                if (WildcardUtil.Match(kvp.Key, codeStr)) return ClampYieldMultiplier(SieveYieldMultiplier * kvp.Value * sourceMultiplier);
            }
            return ClampYieldMultiplier(SieveYieldMultiplier * sourceMultiplier);
        }

        public float ResolvePrimitiveSievePanningYield()
        {
            return ClampYieldMultiplier(PrimitiveSievePanningYieldMultiplier);
        }

        public float ResolveKineticSievePanningYield()
        {
            return ClampYieldMultiplier(KineticSievePanningYieldMultiplier);
        }

        public float ResolveForgePressFuelUsageSpeed()
        {
            return ClampFuelUsageSpeed(ForgePressFuelUsageSpeed);
        }

        public bool HasForgePressModdedNuggetSmeltingEnabled()
        {
            if (!ForgePressEnableModdedNuggetSmelting) return false;

            if (ForgePressModdedNuggetSmeltingMods != null)
            {
                foreach (var kvp in ForgePressModdedNuggetSmeltingMods)
                {
                    if (kvp.Value) return true;
                }
            }

            return ForgePressModdedNuggetSmeltingAllowedPatterns != null && ForgePressModdedNuggetSmeltingAllowedPatterns.Count > 0;
        }

        public bool IsForgePressModdedNuggetSmeltingAllowed(AssetLocation itemCode)
        {
            if (!ForgePressEnableModdedNuggetSmelting || itemCode == null || itemCode.Domain == "game") return false;

            if (ForgePressModdedNuggetSmeltingMods != null
                && ForgePressModdedNuggetSmeltingMods.TryGetValue(itemCode.Domain, out bool domainEnabled)
                && domainEnabled)
            {
                return true;
            }

            if (ForgePressModdedNuggetSmeltingAllowedPatterns != null)
            {
                string code = itemCode.ToString();
                foreach (string rawPattern in ForgePressModdedNuggetSmeltingAllowedPatterns)
                {
                    if (string.IsNullOrWhiteSpace(rawPattern)) continue;
                    if (WildcardUtil.Match(rawPattern.Trim(), code)) return true;
                }
            }

            return false;
        }

        public float ResolveCoalMotorFuelUsageSpeed()
        {
            return ClampFuelUsageSpeed(CoalMotorFuelUsageSpeed);
        }

        public bool IsTitaniumDrillWideMiningEnabled()
        {
            return EnableTitaniumDrillWideMining;
        }

        public bool IsKineticActivatorTargetBlacklisted(Block block, BlockEntity blockEntity)
        {
            if (KineticActivatorTargetBlacklist == null || KineticActivatorTargetBlacklist.Count == 0) return false;

            HashSet<string> candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (block != null)
            {
                AddCandidate(candidates, block.Code?.ToString());
                AddCandidate(candidates, block.Code?.Path);
                AddCandidate(candidates, block.Code?.FirstCodePart());
                AddCandidate(candidates, block.EntityClass);
                AddTypeCandidates(candidates, block.GetType());
            }
            if (blockEntity != null)
            {
                AddTypeCandidates(candidates, blockEntity.GetType());
            }

            foreach (string rawPattern in KineticActivatorTargetBlacklist)
            {
                if (string.IsNullOrWhiteSpace(rawPattern)) continue;
                string pattern = rawPattern.Trim();
                foreach (string candidate in candidates)
                {
                    if (WildcardUtil.Match(pattern, candidate)) return true;
                    if (WildcardUtil.Match(pattern.ToLowerInvariant(), candidate.ToLowerInvariant())) return true;
                }
            }

            return false;
        }

        private static void AddTypeCandidates(HashSet<string> candidates, Type type)
        {
            while (type != null && type != typeof(object))
            {
                AddCandidate(candidates, type.Name);
                AddCandidate(candidates, type.FullName);
                type = type.BaseType;
            }
        }

        private static void AddCandidate(HashSet<string> candidates, string value)
        {
            if (!string.IsNullOrWhiteSpace(value)) candidates.Add(value);
        }

        private static float ClampFuelUsageSpeed(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 1f;
            return GameMath.Clamp(value, 0.01f, 100f);
        }

        private static float ClampStressMultiplier(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 1f;
            return GameMath.Clamp(value, 0f, 100f);
        }

        private static float ClampYieldMultiplier(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 1f;
            return GameMath.Clamp(value, 0f, 100f);
        }
    }
}
