using Vintagestory.API.Common;
using VintageKinematics.Blocks;

namespace VintageKinematics.BlockEntities
{
    public class BECopperPipe : BlockEntity
    {
        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            if (api.Side == EnumAppSide.Server)
            {
                RegisterGameTickListener(RefreshConnections, 1000, 250);
            }
        }

        private void RefreshConnections(float dt)
        {
            BlockCopperPipe.UpdatePipeAt(Api.World, Pos);
        }
    }
}
