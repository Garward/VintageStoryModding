using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VintageKinematics.Api
{
    /// <summary>
    /// Implement on blocks that can receive Mechanical Binder selections and turn blocks
    /// into a moving contraption entity.
    /// </summary>
    public interface IContraptionController
    {
        bool SetSelectionFromWorldBounds(BlockPos start, BlockPos end, IPlayer byPlayer);
    }
}
