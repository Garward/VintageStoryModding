using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using VintageKinematics.Api;

namespace VintageKinematics.Blocks
{
    public class BlockHandCrank : BlockAxisOriented, IKineticActivatable
    {
        private const float WindPulseSeconds = 0.5f;
        private WorldInteraction[] interactions;

        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);
            if (api.Side != EnumAppSide.Client) return;
            interactions = new[]
            {
                new WorldInteraction
                {
                    ActionLangCode = "vintagekinematics:blockhelp-handcrank-wind",
                    MouseButton = EnumMouseButton.Right
                },
                new WorldInteraction
                {
                    ActionLangCode = "vintagekinematics:blockhelp-handcrank-wind-reverse",
                    MouseButton = EnumMouseButton.Right,
                    HotKeyCode = "sneak"
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
            BlockEntity be = world.BlockAccessor.GetBlockEntity(blockSel.Position);
            if (be == null) return false;

            BEBehaviorKineticSource src = be.GetBehavior<BEBehaviorKineticSource>();
            if (src == null) return false;

            if (world.Side == EnumAppSide.Server)
            {
                int direction = byPlayer?.Entity?.Controls?.ShiftKey == true ? -1 : 1;
                src.Wind(seconds: WindPulseSeconds, direction: direction);
                if (byPlayer is IServerPlayer sp)
                {
                    world.Api.Logger.Debug($"[VintageKinematics] Hand crank wound by {sp.PlayerName} dir={direction}");
                }
            }
            return true;
        }

        public bool OnKineticActivate(IWorldAccessor world, BlockPos targetPos, BlockFacing activatedFace, BlockPos activatorPos, float signedRPM)
        {
            if (world.Side != EnumAppSide.Server) return false;

            BlockEntity be = world.BlockAccessor.GetBlockEntity(targetPos);
            BEBehaviorKineticSource src = be?.GetBehavior<BEBehaviorKineticSource>();
            if (src == null) return false;

            int direction = signedRPM < 0f ? -1 : 1;
            src.Wind(seconds: WindPulseSeconds, direction: direction);
            return true;
        }
    }
}
