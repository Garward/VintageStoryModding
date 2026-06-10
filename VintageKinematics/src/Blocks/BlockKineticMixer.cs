using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using VintageKinematics.Api;
using VintageKinematics.BlockEntities;

namespace VintageKinematics.Blocks
{
    /// <summary>
    /// Mixer: quern-style vertical cog drives a vertical paddle in a tub. The retained
    /// side variants are used to orient automation faces.
    /// </summary>
    public class BlockKineticMixer : Block, IPlacementPreviewProvider, ILiquidSink
    {
        public bool AllowHeldLiquidTransfer => false;
        public float CapacityLitres => BEKineticMixer.LiquidCapacityLitres;
        public float TransferSizeLitres => 1f;

        public bool TryResolvePlacementPreview(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, out BlockPos targetPos, out Block variant)
        {
            targetPos = null;
            variant = null;
            if (blockSel?.Face == null) return false;

            targetPos = PlacementPreview.DefaultTargetPos(world, blockSel, this);
            string desired = SideFacingPlayer(byPlayer);
            if (desired == null)
            {
                variant = this;
                return true;
            }

            variant = world.GetBlock(CodeWithVariant("side", desired)) ?? this;
            return true;
        }

        public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemStack, BlockSelection blockSel, ref string failureCode)
        {
            if (!TryResolvePlacementPreview(world, byPlayer, blockSel, out _, out Block variant) || variant == this)
                return base.TryPlaceBlock(world, byPlayer, itemStack, blockSel, ref failureCode);
            return variant.TryPlaceBlock(world, byPlayer, itemStack, blockSel, ref failureCode);
        }

        private static string SideFacingPlayer(IPlayer byPlayer)
        {
            if (byPlayer?.Entity == null) return null;
            BlockFacing facing = BlockFacing.HorizontalFromYaw(byPlayer.Entity.Pos.Yaw);
            if (facing == BlockFacing.NORTH) return "n";
            if (facing == BlockFacing.EAST) return "e";
            if (facing == BlockFacing.SOUTH) return "s";
            if (facing == BlockFacing.WEST) return "w";
            return null;
        }

        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            if (blockSel == null) return false;
            if (KineticInteractionHelper.ShouldDeferToHeldWrench(byPlayer)) return false;
            BEKineticMixer be = MultiblockHelper.GetMultiblockAwareBE(world, blockSel.Position) as BEKineticMixer;
            if (be == null) return base.OnBlockInteractStart(world, byPlayer, blockSel);
            return be.OnPlayerRightClick(byPlayer, blockSel);
        }

        public float GetCurrentLitres(ItemStack containerStack)
        {
            return 0f;
        }

        public float GetCurrentLitres(BlockPos pos)
        {
            return MixerAt(pos)?.GetLiquidLitres() ?? 0f;
        }

        public bool IsFull(ItemStack containerStack)
        {
            return true;
        }

        public bool IsFull(BlockPos pos)
        {
            return MixerAt(pos)?.IsLiquidFull() ?? true;
        }

        public WaterTightContainableProps GetContentProps(ItemStack containerStack)
        {
            return null;
        }

        public WaterTightContainableProps GetContentProps(BlockPos pos)
        {
            return MixerAt(pos)?.GetLiquidContentProps();
        }

        public ItemStack GetContent(ItemStack containerStack)
        {
            return null;
        }

        public ItemStack GetContent(BlockPos pos)
        {
            return MixerAt(pos)?.GetLiquidContent()?.Clone();
        }

        public void SetContent(ItemStack containerStack, ItemStack content)
        {
        }

        public void SetContent(BlockPos pos, ItemStack content)
        {
            MixerAt(pos)?.SetLiquidContent(content);
        }

        public int TryPutLiquid(BlockPos pos, ItemStack liquidStack, float desiredLitres)
        {
            return MixerAt(pos)?.TryPutLiquid(liquidStack, desiredLitres) ?? 0;
        }

        public int TryPutLiquid(ItemStack containerStack, ItemStack liquidStack, float desiredLitres)
        {
            return 0;
        }

        private BEKineticMixer MixerAt(BlockPos pos)
        {
            return MultiblockHelper.GetMultiblockAwareBE(api?.World, pos) as BEKineticMixer;
        }
    }
}
