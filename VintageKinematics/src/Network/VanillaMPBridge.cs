using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent.Mechanics;
using VintageKinematics.Api;

namespace VintageKinematics.Network
{
    /// <summary>
    /// Detects and reads vanilla mechanical-power blocks (axles, angled gears, windmill rotors,
    /// brakes, transmissions) so a VK kinetic node can treat them as a power source. Vanilla
    /// MP speed fluctuates with wind/water/load; we deliberately ignore the magnitude and
    /// publish a stable VK RPM whenever the vanilla axle is rotating at all, so adjacent VK
    /// machines run at a predictable speed regardless of upstream weather conditions.
    /// </summary>
    public static class VanillaMPBridge
    {
        /// <summary>Fixed RPM the bridge publishes when the vanilla axle is rotating either way.</summary>
        public const float StableRPM = 16f;

        /// <summary>Vanilla speeds with magnitude below this read as "stopped" (not rotating).</summary>
        private const float StoppedThreshold = 0.001f;

        /// <summary>Stress-capacity multiplier for vanilla bridge sources — 100× a regular VK source.</summary>
        public const float StressImpact = -100f;

        /// <summary>True iff <paramref name="pos"/> hosts a block entity with a vanilla MP behavior.</summary>
        public static bool IsVanillaMP(IWorldAccessor world, BlockPos pos)
        {
            BlockEntity be = world.BlockAccessor.GetBlockEntity(pos);
            return be?.GetBehavior<BEBehaviorMPBase>() != null;
        }

        /// <summary>
        /// Reads the vanilla MP behavior at <paramref name="pos"/> and returns its rotation axis and
        /// signed RPM (sign matches the vanilla network's speed sign). Returns false when the
        /// position has no MP behavior or the axis can't be resolved.
        /// </summary>
        public static bool TryGetState(IWorldAccessor world, BlockPos pos, out EnumKineticAxis axis, out float signedRPM)
        {
            axis = default;
            signedRPM = 0f;

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
            return true;
        }
    }
}
