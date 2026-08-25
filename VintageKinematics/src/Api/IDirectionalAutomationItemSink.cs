using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VintageKinematics.Api
{
    /// <summary>Optional belt sink contract for blocks whose input is limited to a physical face.</summary>
    public interface IDirectionalAutomationItemSink : IAutomationItemSink
    {
        bool TryAcceptFromBelt(ItemStack stack, BlockPos beltPosition);
        bool TryAcceptFromFunnel(ItemStack stack, BlockPos funnelPosition);
    }
}
