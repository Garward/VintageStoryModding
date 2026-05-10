using Vintagestory.API.MathTools;

namespace VintageKinematics.Api
{
    /// <summary>
    /// Payload for <see cref="BEBehaviorKineticWorker.OnWorkCompleted"/>. Describes
    /// where the work cycle finished and what state the network was in.
    /// </summary>
    public readonly struct KineticWorkCompletedArgs
    {
        /// <summary>Position of the block whose worker fired.</summary>
        public BlockPos Pos                 { get; }
        /// <summary>Network the worker is attached to (may be null on client).</summary>
        public IKineticNetworkInfo Network  { get; }
        /// <summary>Signed RPM at the worker block when the cycle completed.</summary>
        public float EffectiveRPM           { get; }

        /// <summary>Constructs the args bundle.</summary>
        public KineticWorkCompletedArgs(BlockPos pos, IKineticNetworkInfo network, float effectiveRPM)
        {
            Pos = pos;
            Network = network;
            EffectiveRPM = effectiveRPM;
        }
    }
}
