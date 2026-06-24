using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using VintageKinematics.Api;
using VintageKinematics.BlockEntities;

namespace VintageKinematics.Blocks
{
    public class BlockTrebuchet : BlockKineticSidePlaced, IMultiBlockColSelBoxes
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
                    ActionLangCode = "vintagekinematics:blockhelp-trebuchet-mount",
                    MouseButton = EnumMouseButton.Right
                },
                new WorldInteraction
                {
                    ActionLangCode = "vintagekinematics:blockhelp-trebuchet-settings",
                    MouseButton = EnumMouseButton.Right,
                    HotKeyCode = "sneak"
                }
            };
        }

        public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
        {
            return interactions ?? base.GetPlacedBlockInteractionHelp(world, selection, forPlayer);
        }

        public override Cuboidf[] GetCollisionBoxes(IBlockAccessor blockAccessor, BlockPos pos)
        {
            if (IsLaunchCollisionSuppressed(blockAccessor, pos)) return Array.Empty<Cuboidf>();
            return base.GetCollisionBoxes(blockAccessor, pos);
        }

        public Cuboidf[] MBGetCollisionBoxes(IBlockAccessor blockAccessor, BlockPos pos, Vec3i offset)
        {
            BlockPos controllerPos = pos.AddCopy(offset);
            if (IsLaunchCollisionSuppressed(blockAccessor, controllerPos)) return Array.Empty<Cuboidf>();
            return base.GetCollisionBoxes(blockAccessor, controllerPos);
        }

        public Cuboidf[] MBGetSelectionBoxes(IBlockAccessor blockAccessor, BlockPos pos, Vec3i offset)
        {
            return base.GetSelectionBoxes(blockAccessor, pos.AddCopy(offset));
        }

        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            if (byPlayer?.Entity == null || blockSel == null) return false;
            if (KineticInteractionHelper.ShouldDeferToHeldWrench(byPlayer)) return false;

            BETrebuchet be = MultiblockHelper.GetMultiblockAwareBE(world, blockSel.Position) as BETrebuchet;
            if (be == null) return false;

            if (byPlayer.Entity.Controls?.Sneak == true)
            {
                return be.OnPlayerRightClick(byPlayer);
            }

            return be.TryMount(byPlayer.Entity);
        }

        private static bool IsLaunchCollisionSuppressed(IBlockAccessor blockAccessor, BlockPos pos)
        {
            return blockAccessor?.GetBlockEntity(pos) is BETrebuchet { LaunchCollisionSuppressed: true };
        }
    }
}
