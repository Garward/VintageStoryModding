using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using VintageKinematics.Api;

namespace VintageKinematics.Blocks
{
    /// <summary>
    /// Generic block shell for JSON-defined machines backed by an openable block entity.
    /// Handles side placement previews and the standard right-click-to-open interaction.
    /// </summary>
    public class BlockKineticOpenableMachine : BlockKineticSidePlaced
    {
        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            if (blockSel == null) return false;
            if (KineticInteractionHelper.ShouldDeferToHeldWrench(byPlayer)) return false;

            BlockEntityOpenableContainer be = MultiblockHelper.GetMultiblockAwareBE(world, blockSel.Position) as BlockEntityOpenableContainer;
            if (be == null) return base.OnBlockInteractStart(world, byPlayer, blockSel);
            return be.OnPlayerRightClick(byPlayer, blockSel);
        }
    }
}
