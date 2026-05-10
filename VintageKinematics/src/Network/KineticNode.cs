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
        public string Tier;
        public float TierMaxRPM;
        public bool IsCogwheel { get { return Role == EnumKineticRole.SmallCogwheel || Role == EnumKineticRole.LargeCogwheel; } }
    }
}
