using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using VintageKinematics.Api;
using VintageKinematics.Network;
using VintageKinematics.Rendering;

namespace VintageKinematics.BlockEntities
{
    public partial class BEBelt
    {
        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            Direction = Block?.Variant?["direction"];
            UpdateKineticState(triggerRebuild: false);
            if (ControllerPos == null) ControllerPos = Pos.Copy();

            if (api.Side == EnumAppSide.Server)
            {
                // Defer assembly one tick so neighbouring BEs are also initialized.
                RegisterDelayedCallback(_ => RebuildChain(), 50);
                tickListenerId = RegisterGameTickListener(OnServerTick, 50);
            }
            else if (api is ICoreClientAPI capi)
            {
                animationRenderer = new BeltAnimationRenderer(capi, this, GetBehavior<BEBehaviorKinetic>());
                capi.Event.RegisterRenderer(animationRenderer, EnumRenderStage.Opaque);
                tickListenerId = RegisterGameTickListener(OnClientTick, 50);
            }
        }

        public override void OnBlockPlaced(ItemStack byItemStack = null)
        {
            base.OnBlockPlaced(byItemStack);
            Direction = Block?.Variant?["direction"];
            UpdateKineticState(triggerRebuild: false);
            if (Api?.Side == EnumAppSide.Server)
            {
                RegisterDelayedCallback(_ => RebuildChain(), 50);
                RegisterDelayedCallback(_ =>
                {
                    Api?.ModLoader.GetModSystem<KineticNetworkManager>()?.OnPlaced(Pos);
                }, 50);
            }
        }

        public override void OnBlockUnloaded()
        {
            base.OnBlockUnloaded();
            DisposeAnimationRenderer();
        }

        public override void GetBlockInfo(IPlayer forPlayer, System.Text.StringBuilder dsc)
        {
            base.GetBlockInfo(forPlayer, dsc);
            dsc.Append("Belt ").Append(Direction).Append(" — ").Append(Part)
               .Append(" (").Append(IndexInChain + 1).Append('/').Append(ChainLength).Append(')')
               .AppendLine();
        }
    }
}
