using Vintagestory.API.Common;
using VintageKinematics.BlockEntities;

namespace VintageKinematics.Blocks
{
    public class BlockTrashcan : Block
    {
        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            if (blockSel == null) return false;
            if (KineticInteractionHelper.ShouldDeferToHeldWrench(byPlayer)) return false;
            BETrashcan be = world.BlockAccessor.GetBlockEntity(blockSel.Position) as BETrashcan;
            if (be == null) return base.OnBlockInteractStart(world, byPlayer, blockSel);
            return be.OnPlayerRightClick(byPlayer, blockSel);
        }
    }
}
