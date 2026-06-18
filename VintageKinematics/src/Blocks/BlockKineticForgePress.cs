using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using VintageKinematics.Api;
using VintageKinematics.BlockEntities;

namespace VintageKinematics.Blocks
{
    public class BlockKineticForgePress : BlockKineticOpenableMachine
    {
        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            if (blockSel == null) return false;
            if (KineticInteractionHelper.ShouldDeferToHeldWrench(byPlayer)) return false;
            BEKineticForgePress be = MultiblockHelper.GetMultiblockAwareBE(world, blockSel.Position) as BEKineticForgePress;
            if (be == null) return base.OnBlockInteractStart(world, byPlayer, blockSel);
            bool sneaking = byPlayer?.Entity?.Controls?.Sneak == true || byPlayer?.Entity?.Controls?.ShiftKey == true;
            if (sneaking && be.TryUpgradeRefractoryLining(byPlayer)) return true;
            return base.OnBlockInteractStart(world, byPlayer, blockSel);
        }
    }
}
