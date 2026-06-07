using Vintagestory.API.Common;
using VintageKinematics.Api;
using VintageKinematics.BlockEntities;

namespace VintageKinematics.Blocks
{
    /// <summary>
    /// Sawmill: shaft enters horizontally; blade spins on a vertical plane perpendicular to it.
    /// Variant <c>side</c> is the player-facing placement side. The actual feed face is resolved
    /// from the base shape's InputLipWest marker after shape rotation.
    /// </summary>
    public class BlockKineticSawmill : BlockKineticOpenableMachine
    {
        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            if (blockSel == null) return false;
            if (KineticInteractionHelper.ShouldDeferToHeldWrench(byPlayer)) return false;
            BEKineticSawmill be = world.BlockAccessor.GetBlockEntity(blockSel.Position) as BEKineticSawmill;
            if (be == null) return base.OnBlockInteractStart(world, byPlayer, blockSel);
            return be.OnPlayerRightClick(byPlayer, blockSel);
        }
    }
}
