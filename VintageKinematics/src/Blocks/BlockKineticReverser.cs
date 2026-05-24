using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using VintageKinematics.Api;
using VintageKinematics.BlockEntities;

namespace VintageKinematics.Blocks
{
    public class BlockKineticReverser : Block, IPlacementPreviewProvider
    {
        private WorldInteraction[] interactions;

        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);
            if (api.Side != EnumAppSide.Client) return;

            interactions = new[]
            {
                new WorldInteraction
                {
                    ActionLangCode = "vintagekinematics:blockhelp-kineticreverser-toggle",
                    MouseButton = EnumMouseButton.Right
                }
            };
        }

        public bool TryResolvePlacementPreview(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, out BlockPos targetPos, out Block variant)
        {
            targetPos = null;
            variant = null;
            if (blockSel?.Face == null) return false;

            targetPos = VintageKinematics.Api.PlacementPreview.DefaultTargetPos(world, blockSel, this);
            string desiredSide = GetPlacementVariantSide(byPlayer, blockSel);
            variant = world.GetBlock(CodeWithVariant("side", desiredSide)) ?? this;
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

        private static string GetPlacementVariantSide(IPlayer byPlayer, BlockSelection blockSel)
        {
            string faceSide = SideCode(blockSel?.Face);
            if (faceSide != null)
            {
                return faceSide;
            }

            BlockFacing away = SideAwayFromPlayer(byPlayer);
            return SideCode(away) ?? "n";
        }

        private static BlockFacing SideAwayFromPlayer(IPlayer byPlayer)
        {
            if (byPlayer?.Entity == null) return null;
            BlockFacing away = BlockFacing.HorizontalFromYaw(byPlayer.Entity.Pos.Yaw);
            if (away == BlockFacing.NORTH || away == BlockFacing.EAST || away == BlockFacing.SOUTH || away == BlockFacing.WEST) return away;
            return null;
        }

        private static string SideCode(BlockFacing facing)
        {
            if (facing == BlockFacing.NORTH) return "n";
            if (facing == BlockFacing.EAST) return "e";
            if (facing == BlockFacing.SOUTH) return "s";
            if (facing == BlockFacing.WEST) return "w";
            if (facing == BlockFacing.UP) return "u";
            if (facing == BlockFacing.DOWN) return "d";
            return null;
        }

        public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
        {
            return interactions ?? base.GetPlacedBlockInteractionHelp(world, selection, forPlayer);
        }

        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            if (blockSel == null) return false;
            if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is not BEKineticReverser reverser) return false;
            reverser.Toggle(byPlayer);
            return true;
        }
    }
}
