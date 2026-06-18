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

        /// <summary>
        /// Literal horizontal side from player yaw: facing north selects <c>n</c>, east selects
        /// <c>e</c>, south selects <c>s</c>, and west selects <c>w</c>.
        /// </summary>
        public static string CardinalSideFromPlayerYaw(IPlayer byPlayer)
        {
            if (byPlayer?.Entity == null) return null;

            BlockFacing facing = BlockFacing.HorizontalFromYaw(byPlayer.Entity.Pos.Yaw);
            return CardinalSide(facing);
        }

        public static string CardinalSideOppositePlayerYaw(IPlayer byPlayer)
        {
            if (byPlayer?.Entity == null) return null;

            BlockFacing facing = BlockFacing.HorizontalFromYaw(byPlayer.Entity.Pos.Yaw).Opposite;
            return CardinalSide(facing);
        }

        public static string CardinalSide(BlockFacing facing)
        {
            if (facing == BlockFacing.NORTH) return "n";
            if (facing == BlockFacing.EAST) return "e";
            if (facing == BlockFacing.SOUTH) return "s";
            if (facing == BlockFacing.WEST) return "w";
            return null;
        }

        /// <summary>
        /// Maps a clicked center cell to the controller cell for the existing bore-style 3x3
        /// footprint. The offsets preserve the legacy bore placement exactly.
        /// </summary>
        public static BlockPos Centered3x3ControllerPos(BlockPos clickPos, string side)
        {
            BlockPos p = clickPos.Copy();
            switch (side)
            {
                case "n": p.X -= 1; p.Z -= 1; break;
                case "e": p.X += 1; p.Z -= 1; break;
                case "s": p.X += 1; p.Z += 1; break;
                case "w": p.X -= 1; p.Z += 1; break;
            }
            return p;
        }
    }
}
