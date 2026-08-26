using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VintageKinematics.Api
{
    public static class AutomationBlockMutation
    {
        public static void RemoveAndNotify(IWorldAccessor world, BlockPos pos)
        {
            if (world?.BlockAccessor == null || pos == null) return;

            world.BlockAccessor.SetBlock(0, pos);
            world.BlockAccessor.MarkBlockDirty(pos);
            world.BlockAccessor.TriggerNeighbourBlockUpdate(pos);
        }
    }
}
