using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent.Mechanics;
using VintageKinematics.Api;

namespace VintageKinematics.Network
{
    /// <summary>
    /// Detects and reads vanilla mechanical-power blocks (axles, angled gears, windmill rotors,
    /// brakes, transmissions) so a VK kinetic node can treat them as a power source. The VK
    /// published RPM is fixed at <see cref="StableRPM"/> whenever the vanilla axle is rotating
    /// (sign matches vanilla direction): vanilla speed jitters with wind/water/load every tick,
    /// and forwarding that into VK would cause MaxRPM-bound consumers to constantly re-evaluate
    /// the network and flicker. Stress capacity, on the other hand, tracks live vanilla
    /// <c>TotalAvailableTorque</c> so a 4-sail windmill genuinely outpowers a 1-sail.
    /// </summary>
    public static class VanillaMPBridge
    {
        /// <summary>
        /// Fixed VK RPM the bridge publishes while the vanilla axle is rotating. Initialized to
        /// the default; <see cref="Api.KineticConfigSystem"/> overwrites this from the loaded
        /// config at mod start.
        /// </summary>
        public static float StableRPM = 16f;

        /// <summary>Vanilla speeds with magnitude below this read as "stopped" (not rotating).</summary>
        private const float StoppedThreshold = 0.001f;

        /// <summary>
        /// SU capacity per unit of computed "source-potential torque" — the load-independent
        /// sum of <c>TorqueFactor × TargetSpeed</c> across rotor nodes on the upstream vanilla
        /// network. Default 3333 puts a 4-sail max-wind windmill (potential ≈ 0.6) near 2000 SU.
        /// Scales linearly: a 16-sail vertical mega-rotor at full wind ≈ 8000 SU. Overwritten
        /// at mod start from <see cref="Api.VintageKinematicsConfig.VanillaBridgeCapacityPerTorque"/>.
        /// </summary>
        public static float CapacityPerTorque = 3333f;

        /// <summary>
        /// EMA factor (0-1] used by the bridge poll to smooth tick-to-tick torque readings.
        /// Overwritten at mod start from <see cref="Api.VintageKinematicsConfig.VanillaBridgeTorqueSmoothing"/>.
        /// </summary>
        public static float TorqueSmoothing = 0.15f;

        // Reflection cache for rotor TorqueFactor/TargetSpeed properties. We duck-type on the
        // property names rather than the base class because mods (e.g. Millwright's vertical
        // windmill BEBehaviorMPRotorUD) implement their own rotor base that extends
        // BEBehaviorMPBase directly and never derives from BEBehaviorMPRotor — but they follow
        // the same protected-virtual property contract. Cached per concrete Type since each
        // class can declare the props at its own level. Null entry in the cache means "this
        // type isn't a rotor"; we still cache so the lookup doesn't repeat each tick.
        private static readonly BindingFlags RotorPropFlags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        private struct RotorPropPair
        {
            public PropertyInfo TorqueFactor;
            public PropertyInfo TargetSpeed;
            public bool IsRotor;
        }
        private static readonly System.Collections.Generic.Dictionary<System.Type, RotorPropPair> rotorPropCache = new System.Collections.Generic.Dictionary<System.Type, RotorPropPair>();

        /// <summary>Lower bound on |torque| used when computing StressImpact. Stops the very
        /// first network tick (TotalAvailableTorque still 0) from showing flat-zero capacity
        /// while the rotor spins up.</summary>
        private const float MinTorqueFloor = 0.05f;

        /// <summary>True iff <paramref name="pos"/> hosts a block entity with a vanilla MP behavior.</summary>
        public static bool IsVanillaMP(IWorldAccessor world, BlockPos pos)
        {
            BlockEntity be = world.BlockAccessor.GetBlockEntity(pos);
            return be?.GetBehavior<BEBehaviorMPBase>() != null;
        }

        /// <summary>
        /// Reads the vanilla MP behavior at <paramref name="pos"/> and returns its rotation axis,
        /// signed VK RPM (StableRPM with vanilla direction sign), and the upstream vanilla
        /// network's TotalAvailableTorque (signed; magnitude is what callers use for capacity).
        /// Returns false when the position has no MP behavior or the axis can't be resolved.
        /// </summary>
        public static bool TryGetState(IWorldAccessor world, BlockPos pos, out EnumKineticAxis axis, out float signedRPM, out float networkTorque, out long vanillaNetworkId)
        {
            axis = default;
            signedRPM = 0f;
            networkTorque = 0f;
            vanillaNetworkId = 0L;

            BlockEntity be = world.BlockAccessor.GetBlockEntity(pos);
            BEBehaviorMPBase mp = be?.GetBehavior<BEBehaviorMPBase>();
            if (mp == null) return false;

            // Most vanilla MP blocks expose orientation via the "rotation" variant. Fall back to
            // AxisSign for blocks (windmill rotor, creative rotor) that store axis differently.
            string rot = be.Block?.Variant?["rotation"];
            switch (rot)
            {
                case "we": axis = EnumKineticAxis.X; break;
                case "ud": axis = EnumKineticAxis.Y; break;
                case "ns": axis = EnumKineticAxis.Z; break;
                default:
                    int[] sign = mp.AxisSign;
                    if (sign == null || sign.Length < 3) return false;
                    if (sign[0] != 0) axis = EnumKineticAxis.X;
                    else if (sign[1] != 0) axis = EnumKineticAxis.Y;
                    else if (sign[2] != 0) axis = EnumKineticAxis.Z;
                    else return false;
                    break;
            }

            float vanillaSpeed = mp.Network?.Speed ?? 0f;
            if (System.MathF.Abs(vanillaSpeed) < StoppedThreshold)
            {
                signedRPM = 0f;
            }
            else
            {
                signedRPM = vanillaSpeed >= 0f ? StableRPM : -StableRPM;
            }

            // Prefer load-independent source potential (TorqueFactor × TargetSpeed across rotors)
            // over Network.TotalAvailableTorque. TotalAvailableTorque decays whenever the vanilla
            // network has unused power — for a windmill driving only VK-side consumers, the
            // vanilla network sees no resistance and TotalAvailableTorque collapses, giving the
            // bridge a tiny capacity reading despite a big windmill upstream. The reflection sum
            // matches what players intuit: more sails / higher wind = more SU, regardless of load.
            float potential = ComputeSourcePotentialTorque(mp.Network);
            networkTorque = potential != 0f ? potential : (mp.Network?.TotalAvailableTorque ?? 0f);
            vanillaNetworkId = mp.Network?.networkId ?? 0L;
            return true;
        }

        private static float ComputeSourcePotentialTorque(MechanicalNetwork network)
        {
            if (network?.nodes == null) return 0f;
            float total = 0f;
            foreach (var kvp in network.nodes)
            {
                total += System.MathF.Abs(ReadRotorPotential(kvp.Value));
            }
            return total;
        }

        private static float ReadRotorPotential(object node)
        {
            if (node == null) return 0f;
            System.Type type = node.GetType();
            if (!rotorPropCache.TryGetValue(type, out RotorPropPair pair))
            {
                // GetProperty walks the inheritance chain, so an override at any level is honored.
                PropertyInfo tf = type.GetProperty("TorqueFactor", RotorPropFlags);
                PropertyInfo ts = type.GetProperty("TargetSpeed", RotorPropFlags);
                pair = new RotorPropPair
                {
                    TorqueFactor = tf,
                    TargetSpeed = ts,
                    IsRotor = tf != null && ts != null && tf.PropertyType == typeof(float) && ts.PropertyType == typeof(float)
                };
                rotorPropCache[type] = pair;
            }
            if (!pair.IsRotor) return 0f;
            try
            {
                float tfv = (float)(pair.TorqueFactor.GetValue(node) ?? 0f);
                float tsv = (float)(pair.TargetSpeed.GetValue(node) ?? 0f);
                return tfv * tsv;
            }
            catch
            {
                return 0f;
            }
        }

        /// <summary>
        /// Computes the bridge node's StressImpact such that the network capacity contribution
        /// (<c>-StressImpact × RatedRPM</c>, with RatedRPM=StableRPM) equals
        /// <c>CapacityPerTorque × |torque|</c>. A small torque floor prevents flat-zero capacity
        /// during rotor spin-up.
        /// </summary>
        public static float ComputeStressImpact(float networkTorque)
        {
            float absTorque = System.MathF.Max(System.MathF.Abs(networkTorque), MinTorqueFloor);
            return -CapacityPerTorque * absTorque / StableRPM;
        }
    }
}
