using Vintagestory.API.MathTools;

namespace VintageKinematics.Api
{
    /// <summary>
    /// Opt-in marker for a multiblock controller BE that restricts kinetic connectivity
    /// to specific filler cells of its claim. Default <see cref="Network.WorldNodeProvider"/>
    /// treats every filler cell as a virtual coaxial shaft segment; controllers that implement
    /// this interface get to veto cells that don't visually contain a shaft socket.
    /// </summary>
    public interface IKineticMultiblockController
    {
        bool IsKineticShaftCell(BlockPos fillerPos);
    }
}
