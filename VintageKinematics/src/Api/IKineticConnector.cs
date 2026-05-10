using Vintagestory.API.MathTools;

namespace VintageKinematics.Api
{
    /// <summary>
    /// Implemented by Block (or BlockEntity) classes that need custom logic
    /// for whether and how they connect to a kinetic neighbor. Return null
    /// to defer to the default rules; return a result to override them.
    /// </summary>
    public interface IKineticConnector
    {
        /// <summary>
        /// Decide how <paramref name="self"/> connects to its neighbor <paramref name="other"/>.
        /// Return <c>null</c> to defer to the default rules; return a result to override them.
        /// </summary>
        KineticConnectionResult? TryConnect(KineticNodeInfo self, KineticNodeInfo other, BlockPos fromPos, BlockPos toPos);
    }
}
