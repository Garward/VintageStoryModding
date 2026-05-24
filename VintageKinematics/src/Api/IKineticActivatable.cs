using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VintageKinematics.Api
{
    /// <summary>
    /// Optional block or block-entity hook for Kinetic Activator support.
    /// Use this instead of player-only right-click code when automation should be allowed.
    /// </summary>
    public interface IKineticActivatable
    {
        bool OnKineticActivate(IWorldAccessor world, BlockPos targetPos, BlockFacing activatedFace, BlockPos activatorPos, float signedRPM);
    }
}
