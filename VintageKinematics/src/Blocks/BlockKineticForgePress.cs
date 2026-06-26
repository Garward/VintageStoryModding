using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using VintageKinematics.Api;
using VintageKinematics.BlockEntities;

namespace VintageKinematics.Blocks
{
    public class BlockKineticForgePress : BlockKineticOpenableMachine
    {
        internal const string RefractoryLiningAttribute = "tier3RefractoryLining";

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

        public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos)
        {
            return WithForgePressState(base.OnPickBlock(world, pos), world, pos);
        }

        public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1f)
        {
            ItemStack[] drops = base.GetDrops(world, pos, byPlayer, dropQuantityMultiplier);
            if (drops == null) return null;

            for (int i = 0; i < drops.Length; i++)
            {
                drops[i] = WithForgePressState(drops[i], world, pos);
            }

            return drops;
        }

        private static ItemStack WithForgePressState(ItemStack stack, IWorldAccessor world, BlockPos pos)
        {
            if (stack == null || world == null || pos == null) return stack;

            BEKineticForgePress be = MultiblockHelper.GetMultiblockAwareBE(world, pos) as BEKineticForgePress;
            if (be?.HasTier3RefractoryLining == true)
            {
                stack.Attributes.SetBool(RefractoryLiningAttribute, true);
            }

            return stack;
        }
    }
}
