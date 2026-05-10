using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VintageKinematics.Api
{
    /// <summary>
    /// Implement this on a Block to opt into the kinetic ghost-placement preview and to
    /// expose, in one place, the resolution logic that <c>TryPlaceBlock</c> uses for both
    /// the target position and the variant code. Addons get a ghost preview "for free"
    /// just by implementing this interface — the kinetic preview renderer is generic and
    /// has no per-block knowledge.
    ///
    /// Use <see cref="PlacementPreview.DefaultTargetPos"/> when you only need the standard
    /// "click face replaces the clicked cell or its neighbor" target resolution.
    /// </summary>
    public interface IPlacementPreviewProvider
    {
        /// <summary>
        /// Resolve the target position and the actual variant block that will be placed
        /// for the given selection. Return <c>false</c> to skip the preview (e.g. if the
        /// placement would be invalid due to a ratio mismatch or obstruction).
        /// </summary>
        /// <param name="targetPos">World position the variant will land at.</param>
        /// <param name="variant">Block variant that will actually be placed (may be <c>this</c>).</param>
        bool TryResolvePlacementPreview(
            IWorldAccessor world,
            IPlayer byPlayer,
            BlockSelection blockSel,
            out BlockPos targetPos,
            out Block variant);
    }

    /// <summary>Helpers for <see cref="IPlacementPreviewProvider"/> implementers.</summary>
    public static class PlacementPreview
    {
        /// <summary>
        /// Standard target-position resolution: replace the clicked cell if its current
        /// content allows it, otherwise place into the face-adjacent neighbor.
        /// </summary>
        public static BlockPos DefaultTargetPos(IWorldAccessor world, BlockSelection sel, Block held)
        {
            Block atSel = world.BlockAccessor.GetBlock(sel.Position);
            return (atSel != null && atSel.IsReplacableBy(held))
                ? sel.Position
                : sel.Position.AddCopy(sel.Face);
        }
    }
}
