using Vintagestory.API.MathTools;
using VintageKinematics.Api;

namespace VintageKinematics.Network
{
    public struct KineticNode
    {
        public BlockPos Pos;
        public EnumKineticAxis Axis;
        public EnumKineticRole Role;
        public float Ratio;
        public int Direction;
        public float PhaseOffset;
        public float StressImpact;
        // Source nodes (StressImpact < 0): rated RPM the source is designed to spin at.
        // Used for fixed source-capacity calc so displayed Su doesn't fluctuate with the
        // network's effective sourceRPM. Zero/unused for consumers and passive nodes.
        public float RatedRPM;
        public long NetworkId;
        // Set by WorldNodeProvider for synthesized vanilla MP bridge nodes. Persists in net.Nodes
        // after the source BE is destroyed (vanilla axle break), so the bridge can still be
        // identified for cleanup polling. StressImpact varies per bridge to scale capacity with
        // live vanilla torque, so it can't double as a sentinel.
        public bool IsVanillaBridge;
        // Vanilla MP network id this bridge reads from. Multiple VK bridges touching the same
        // vanilla network (player runs an axle chain past a kinetic shaft) would otherwise each
        // contribute full CapacityPerTorque × potential to StressCapacity, multiplying SU per
        // connected axle. RecomputeStressForRPM dedupes by this id so only one bridge per
        // vanilla network counts toward capacity. Zero for non-bridge nodes.
        public long VanillaNetworkId;
        // EMA-smoothed torque reading used to drive StressImpact. Raw vanilla rotor potential
        // (TargetSpeed × TorqueFactor) jitters sharply because wind speed updates per tick, so
        // displayed SU would otherwise swing several-fold per second. Bridge poll blends each
        // fresh reading into this field, then derives StressImpact from the smoothed value.
        // Zero for non-bridge nodes.
        public float SmoothedTorque;
        public bool IsCogwheel { get { return Role == EnumKineticRole.SmallCogwheel || Role == EnumKineticRole.LargeCogwheel; } }
    }
}
