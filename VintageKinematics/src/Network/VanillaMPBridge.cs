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
    /// the network and flicker. Stress capacity, on the other hand, tracks reflected vanilla
    /// source potential so more sails and high-elevation wind genuinely add VK capacity.
    /// </summary>
    public static class VanillaMPBridge
    {
        public enum BridgeMode
        {
            Dynamic,
            SampledStatic,
            Fixed,
            Disabled
        }

        /// <summary>
        /// Capacity update mode for vanilla MP bridge nodes. Initialized to Dynamic to preserve
        /// existing behavior; <see cref="Api.KineticConfigSystem"/> overwrites this from config.
        /// </summary>
        public static BridgeMode Mode = BridgeMode.Dynamic;

        /// <summary>
        /// Fixed VK RPM the bridge publishes while the vanilla axle is rotating. Initialized to
        /// the default; <see cref="Api.KineticConfigSystem"/> overwrites this from the loaded
        /// config at mod start.
        /// </summary>
        public static float StableRPM = 16f;

        /// <summary>Total fixed SU capacity for each rotating vanilla bridge in Fixed mode.</summary>
        public static float FixedStressCapacity = 2000f;

        /// <summary>Server poll interval for vanilla bridge source changes.</summary>
        public static int PollIntervalMs = 250;

        /// <summary>Vanilla speeds with magnitude below this read as "stopped" (not rotating).</summary>
        private const float StoppedThreshold = 0.001f;

        /// <summary>
        /// SU capacity per unit of computed "source-potential torque" — the load-independent
        /// sum of <c>TorqueFactor × effective speed</c> across rotor nodes on the upstream vanilla
        /// network. Effective speed follows vanilla <c>TargetSpeed</c>, but wind rotors with raw
        /// wind above 100% scale past vanilla's target-speed cap. Default 3333 puts a 4-sail
        /// 100%-wind windmill (potential ≈ 0.6) near 2000 SU. Scales linearly: a 16-sail
        /// vertical mega-rotor at 100% wind ≈ 8000 SU. Overwritten
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
            public PropertyInfo WindSpeedProperty;
            public FieldInfo WindSpeedField;
            public bool IsRotor;
        }
        private static readonly System.Collections.Generic.Dictionary<System.Type, RotorPropPair> rotorPropCache = new System.Collections.Generic.Dictionary<System.Type, RotorPropPair>();
        private static readonly System.Collections.Generic.Dictionary<System.Type, MethodInfo> rotationReversedMethodCache = new System.Collections.Generic.Dictionary<System.Type, MethodInfo>();

        /// <summary>Lower bound on |torque| used when computing StressImpact. Stops the very
        /// first network tick (TotalAvailableTorque still 0) from showing flat-zero capacity
        /// while the rotor spins up.</summary>
        private const float MinTorqueFloor = 0.05f;

        public static BridgeMode ParseMode(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return BridgeMode.Dynamic;
            string normalized = value.Trim().Replace("-", "").Replace("_", "").ToLowerInvariant();
            return normalized switch
            {
                "dynamic" => BridgeMode.Dynamic,
                "sampledstatic" => BridgeMode.SampledStatic,
                "sampled" => BridgeMode.SampledStatic,
                "static" => BridgeMode.SampledStatic,
                "fixed" => BridgeMode.Fixed,
                "off" => BridgeMode.Disabled,
                "disabled" => BridgeMode.Disabled,
                "none" => BridgeMode.Disabled,
                _ => BridgeMode.Dynamic
            };
        }

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
            if (Mode == BridgeMode.Disabled) return false;

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

            // Prefer load-independent source potential (TorqueFactor × effective speed across rotors)
            // over Network.TotalAvailableTorque. TotalAvailableTorque decays whenever the vanilla
            // network has unused power — for a windmill driving only VK-side consumers, the
            // vanilla network sees no resistance and TotalAvailableTorque collapses, giving the
            // bridge a tiny capacity reading despite a big windmill upstream. The reflection sum
            // matches what players intuit: more sails / higher wind = more SU, regardless of load.
            float potential = ComputeSourcePotentialTorque(mp.Network);
            float totalAvailableTorque = mp.Network?.TotalAvailableTorque ?? 0f;
            networkTorque = potential != 0f ? potential : totalAvailableTorque;

            // Vanilla MP can leave a disconnected clutch/transmission segment with stale nonzero
            // Network.Speed for a short window. Do not let that cached motion drive VK unless the
            // vanilla network still has detectable source potential behind it.
            bool hasSourcePotential = System.MathF.Abs(potential) > 0.0001f || System.MathF.Abs(totalAvailableTorque) > 0.0001f;

            // VK's signed RPM convention is opposite vanilla's local MP angle convention.
            // Normalize once at the source boundary so every coaxial axle/shaft bridge can
            // pass direction through unchanged.
            float vanillaSpeed = hasSourcePotential ? -ReadLocalSignedSpeed(mp, axis) : 0f;
            if (System.MathF.Abs(vanillaSpeed) < StoppedThreshold)
            {
                signedRPM = 0f;
            }
            else
            {
                signedRPM = vanillaSpeed >= 0f ? StableRPM : -StableRPM;
            }

            vanillaNetworkId = mp.Network?.networkId ?? 0L;
            return true;
        }

        public static float InitialStressImpact(float networkTorque)
        {
            return Mode == BridgeMode.Fixed
                ? ComputeFixedStressImpact()
                : ComputeStressImpact(networkTorque);
        }

        public static float ComputeFixedStressImpact()
        {
            float rpm = System.MathF.Max(0.001f, System.MathF.Abs(StableRPM));
            return -System.MathF.Max(0f, FixedStressCapacity) / rpm;
        }

        private static float ReadLocalSignedSpeed(BEBehaviorMPBase mp, EnumKineticAxis axis)
        {
            float speed = (mp.Network?.Speed ?? 0f) * mp.GearedRatio;

            // Vanilla renders each MP device from its local AngleRad, which applies this
            // reversal before the renderer multiplies by AxisSign. Mirror that public API
            // path so VK bridge direction matches the vanilla axle's actual visual rotation.
            if (IsRotationReversed(mp))
            {
                speed = -speed;
            }

            int axisSign = AxisSignFor(axis, mp.AxisSign);
            return axisSign == 0 ? speed : speed * axisSign;
        }

        private static int AxisSignFor(EnumKineticAxis axis, int[] signs)
        {
            if (signs == null || signs.Length < 3) return 0;
            return axis switch
            {
                EnumKineticAxis.X => signs[0],
                EnumKineticAxis.Y => signs[1],
                EnumKineticAxis.Z => signs[2],
                _ => 0
            };
        }

        private static bool IsRotationReversed(BEBehaviorMPBase mp)
        {
            if (mp == null) return false;
            System.Type type = mp.GetType();
            if (!rotationReversedMethodCache.TryGetValue(type, out MethodInfo method))
            {
                method = type.GetMethod("isRotationReversed", RotorPropFlags);
                rotationReversedMethodCache[type] = method;
            }
            if (method == null || method.ReturnType != typeof(bool)) return false;
            try
            {
                return (bool)method.Invoke(mp, null);
            }
            catch
            {
                return false;
            }
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
                    WindSpeedProperty = FindPropertyInHierarchy(type, "WindSpeed") ?? FindPropertyInHierarchy(type, "windSpeed"),
                    WindSpeedField = FindFieldInHierarchy(type, "windSpeed") ?? FindFieldInHierarchy(type, "WindSpeed"),
                    IsRotor = tf != null && ts != null && tf.PropertyType == typeof(float) && ts.PropertyType == typeof(float)
                };
                rotorPropCache[type] = pair;
            }
            if (!pair.IsRotor) return 0f;
            try
            {
                float tfv = (float)(pair.TorqueFactor.GetValue(node) ?? 0f);
                float tsv = (float)(pair.TargetSpeed.GetValue(node) ?? 0f);
                float windSpeed = ReadWindSpeed(node, pair);
                if (windSpeed > 1f && tsv > 0f && tsv <= 0.6001f)
                {
                    tsv *= windSpeed;
                }
                return tfv * tsv;
            }
            catch
            {
                return 0f;
            }
        }

        private static PropertyInfo FindPropertyInHierarchy(System.Type type, string name)
        {
            for (System.Type current = type; current != null; current = current.BaseType)
            {
                PropertyInfo prop = current.GetProperty(name, RotorPropFlags | BindingFlags.DeclaredOnly);
                if (prop != null) return prop;
            }
            return null;
        }

        private static FieldInfo FindFieldInHierarchy(System.Type type, string name)
        {
            for (System.Type current = type; current != null; current = current.BaseType)
            {
                FieldInfo field = current.GetField(name, RotorPropFlags | BindingFlags.DeclaredOnly);
                if (field != null) return field;
            }
            return null;
        }

        private static float ReadWindSpeed(object node, RotorPropPair pair)
        {
            object value = null;
            if (pair.WindSpeedProperty != null)
            {
                value = pair.WindSpeedProperty.GetValue(node);
            }
            else if (pair.WindSpeedField != null)
            {
                value = pair.WindSpeedField.GetValue(node);
            }

            if (value == null) return 0f;
            try
            {
                return (float)System.Convert.ToDouble(value);
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
