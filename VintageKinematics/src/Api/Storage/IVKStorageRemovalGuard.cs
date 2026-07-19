using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VintageKinematics.Api.Storage
{
    /// <summary>
    /// Implemented by controllers that can decide whether removing a warehouse part is safe.
    /// </summary>
    public interface IVKStorageRemovalGuard
    {
        StorageRemovalCheck CanRemoveStorageBlock(BlockPos pos, StorageRemovalKind kind, IPlayer byPlayer = null);
    }
}
