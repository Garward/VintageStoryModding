using Vintagestory.API.Common;

namespace VintageKinematics.Api
{
    /// <summary>
    /// Explicit non-inventory sink for logistics blocks. Generic inventory push should not call this;
    /// belts and funnels choose the source-specific entry point instead.
    /// </summary>
    public interface IAutomationItemSink
    {
        bool TryAcceptFromBelt(ItemStack stack);
        bool TryAcceptFromFunnel(ItemStack stack);
    }
}
