using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using VintageKinematics.Rendering;

namespace VintageKinematics.BlockEntities
{
    /// <summary>
    /// Position of a belt segment within its chain. <c>Solo</c> = standalone single-segment belt.
    /// </summary>
    public enum EnumBeltPart { Solo, Start, Middle, End }

    /// <summary>
    /// Belt segment block entity.
    /// Stores a back-pointer to the chain controller (the <see cref="EnumBeltPart.Start"/> segment),
    /// the segment's index in the chain, and total chain length. The controller is authoritative
    /// for shared state; non-controller segments delegate item queries up.
    /// </summary>
    public partial class BEBelt : BlockEntity
    {
        public const int MaxChainLength = 64;

        /// <summary>Position of the controller (Start segment). Equals own Pos if this is the controller.</summary>
        public BlockPos ControllerPos { get; private set; }
        /// <summary>0-based index in the chain.</summary>
        public int IndexInChain { get; private set; }
        /// <summary>Total number of segments in this chain (controller is authoritative).</summary>
        public int ChainLength { get; private set; } = 1;
        /// <summary>This segment's role in the chain.</summary>
        public EnumBeltPart Part { get; private set; } = EnumBeltPart.Solo;

        /// <summary>Direction variant of this belt block (n/e/s/w).</summary>
        public string Direction { get; private set; }

        /// <summary>True if a shaft is inserted through this segment (visual + future kinetic tap-off).</summary>
        public bool HasShaft { get; private set; }

        /// <summary>Axis (x/y/z) of the inserted shaft, for round-tripping on extraction. Empty when none.</summary>
        public string InsertedShaftAxis { get; private set; }

        /// <summary>
        /// Item-list — only populated on the controller. Items are in chain order with
        /// progress in [0, ChainLength], 0 = tail face of Start, ChainLength = head face of End.
        /// </summary>
        private readonly List<BeltItem> items = new();

        /// <summary>Read-only view of the controller's items. Empty list if not controller.</summary>
        public IReadOnlyList<BeltItem> Items => items;

        /// <summary>Y at which items rest (just above the top belt strip).</summary>
        public const float BeltTopY = 11f / 16f;

        private const float ItemCaptureMargin = 0.35f;
        private const float ItemEjectVelocityScale = 0.2f;
        private const float ItemInsertClearance = 0.3f;
        private const float ItemEndStopMargin = 0.05f;

        private BeltAnimationRenderer animationRenderer;
        private long tickListenerId;

        public void SetShaft(bool value, string axis = null)
        {
            if (HasShaft == value && InsertedShaftAxis == axis) return;
            HasShaft = value;
            InsertedShaftAxis = value ? axis : null;
            UpdateKineticState(triggerRebuild: true);
            MarkDirty(true);
        }

        /// <summary>True iff this segment is the controller (the chain's <see cref="EnumBeltPart.Start"/>).</summary>
        public bool IsController => ControllerPos != null && ControllerPos.Equals(Pos);
    }
}
