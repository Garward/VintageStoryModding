using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using VintageKinematics.Api;
using VintageKinematics.BlockEntities;

namespace VintageKinematics.Blocks
{
    public class BlockKineticCharcoalRetort : Block, IPlacementPreviewProvider
    {
        private WorldInteraction[] retortInteractions;

        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);
            PlacedPriorityInteract = true;
        }

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
            {
                return base.TryPlaceBlock(world, byPlayer, itemStack, blockSel, ref failureCode);
            }

            return variant.TryPlaceBlock(world, byPlayer, itemStack, blockSel, ref failureCode);
        }

        private static string SideFacingPlayer(IPlayer byPlayer)
        {
            if (byPlayer?.Entity == null) return null;
            BlockFacing toward = BlockFacing.HorizontalFromYaw(byPlayer.Entity.Pos.Yaw);
            if (toward == BlockFacing.NORTH) return "n";
            if (toward == BlockFacing.EAST) return "e";
            if (toward == BlockFacing.SOUTH) return "s";
            if (toward == BlockFacing.WEST) return "w";
            return null;
        }

        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            if (blockSel == null) return false;
            BEKineticCharcoalRetort be = MultiblockHelper.GetMultiblockAwareBE(world, blockSel.Position) as BEKineticCharcoalRetort;
            if (be == null) return base.OnBlockInteractStart(world, byPlayer, blockSel);
            return be.OnPlayerRightClick(byPlayer, blockSel);
        }

        public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
        {
            WorldInteraction[] baseInteractions = base.GetPlacedBlockInteractionHelp(world, selection, forPlayer);
            retortInteractions ??= new[]
            {
                new WorldInteraction { ActionLangCode = "vintagekinematics:blockhelp-retort-addfirewood", MouseButton = EnumMouseButton.Right, HotKeyCode = "shift" },
                new WorldInteraction { ActionLangCode = "vintagekinematics:blockhelp-retort-addallfirewood", MouseButton = EnumMouseButton.Right, HotKeyCodes = new[] { "shift", "ctrl" } },
                new WorldInteraction { ActionLangCode = "vintagekinematics:blockhelp-retort-takecharcoal", MouseButton = EnumMouseButton.Right },
                new WorldInteraction { ActionLangCode = "vintagekinematics:blockhelp-retort-takeallcharcoal", MouseButton = EnumMouseButton.Right, HotKeyCode = "ctrl" }
            };

            return baseInteractions == null
                ? retortInteractions
                : baseInteractions.Concat(retortInteractions).ToArray();
        }
    }
}
