using Vintagestory.API.MathTools;

namespace VintageKinematics.Api
{
    /// <summary>
    /// Read-only snapshot of a kinetic node's identity. Used by
    /// <see cref="IKineticConnector"/> implementations so they can decide
    /// whether two blocks should connect, without touching the mutable
    /// internal node struct.
    /// </summary>
    public readonly struct KineticNodeInfo
    {
        /// <summary>World position of the node.</summary>
        public BlockPos Pos              { get; }
        /// <summary>Shaft axis the node presents.</summary>
        public EnumKineticAxis Axis      { get; }
        /// <summary>Structural role — drives default connection rules.</summary>
        public EnumKineticRole Role      { get; }
        /// <summary>Gear ratio relative to the network source. Source itself is 1.0.</summary>
        public float Ratio               { get; }
        /// <summary>Rotation direction relative to source: +1 same, -1 reversed.</summary>
        public int Direction             { get; }
        /// <summary>Resolved block code path, e.g. <c>belt-n</c>. Empty when unavailable.</summary>
        public string BlockCode          { get; }

        /// <summary>Constructs an immutable node snapshot.</summary>
        public KineticNodeInfo(BlockPos pos, EnumKineticAxis axis, EnumKineticRole role, float ratio, int direction, string blockCode = null)
        {
            Pos = pos;
            Axis = axis;
            Role = role;
            Ratio = ratio;
            Direction = direction;
            BlockCode = blockCode ?? "";
        }

        /// <summary>True iff the node is a small or large cogwheel.</summary>
        public bool IsCogwheel => Role == EnumKineticRole.SmallCogwheel || Role == EnumKineticRole.LargeCogwheel;
    }
}
