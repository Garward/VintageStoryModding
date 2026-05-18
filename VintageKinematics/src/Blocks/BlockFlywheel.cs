using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using VintageKinematics.Api;
using VintageKinematics.BlockEntities;

namespace VintageKinematics.Blocks
{
    public class BlockFlywheel : Block, IPlacementPreviewProvider
    {
        private WorldInteraction[] interactions;

        private static string OutputSideFacingPlayer(IPlayer byPlayer)
        {
            if (byPlayer?.Entity == null) return "e";
            BlockFacing facing = BlockFacing.HorizontalFromYaw(byPlayer.Entity.Pos.Yaw);
            if (facing == BlockFacing.NORTH) return "n";
            if (facing == BlockFacing.EAST)  return "e";
            if (facing == BlockFacing.SOUTH) return "s";
            if (facing == BlockFacing.WEST)  return "w";
            return "e";
        }

        public bool TryResolvePlacementPreview(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, out BlockPos targetPos, out Block variant)
        {
            targetPos = null;
            variant = null;
            if (blockSel?.Face == null) return false;

            targetPos = PlacementPreview.DefaultTargetPos(world, blockSel, this);
            string desired = OutputSideFacingPlayer(byPlayer);
            variant = world.GetBlock(CodeWithVariant("side", desired)) ?? this;
            return true;
        }

        public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemStack, BlockSelection blockSel, ref string failureCode)
        {
            if (!TryResolvePlacementPreview(world, byPlayer, blockSel, out _, out Block variant) || variant == this)
            {
                return base.TryPlaceBlock(world, byPlayer, itemStack, blockSel, ref failureCode);
            }
            return variant.TryPlaceBlock(world, byPlayer, itemStack, blockSel, ref failureCode);
        }

        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);
            if (api.Side != EnumAppSide.Client) return;
            interactions = new[]
            {
                new WorldInteraction
                {
                    ActionLangCode = "vintagekinematics:blockhelp-flywheel-toggle",
                    MouseButton = EnumMouseButton.Right
                }
            };
        }

        public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
        {
            return interactions ?? base.GetPlacedBlockInteractionHelp(world, selection, forPlayer);
        }

        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            if (blockSel == null) return false;
            if (world.Side == EnumAppSide.Server)
            {
                BEFlywheel be = MultiblockHelper.GetMultiblockAwareBE(world, blockSel.Position) as BEFlywheel;
                be?.ToggleBankRelease();
            }
            return true;
        }
    }
}
