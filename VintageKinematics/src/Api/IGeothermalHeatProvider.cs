using Vintagestory.API.MathTools;

namespace VintageKinematics.Api
{
    /// <summary>
    /// Exposes a completed geothermal tap for adjacent machines.
    /// </summary>
    public interface IGeothermalHeatProvider
    {
        bool IsTapped { get; }
        bool CanProvideHeatTo(BlockPos consumerPos);
    }
}
