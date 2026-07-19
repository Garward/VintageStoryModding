using Vintagestory.API.MathTools;

namespace VintageKinematics.Api.Storage
{
    /// <summary>
    /// Implemented by controller cells, capacity cells, import hatches, export hatches, and future storage structure parts.
    /// </summary>
    public interface IVKStorageStructureMember
    {
        string WarehouseId { get; }
        BlockPos ControllerPos { get; }
        long CapacityContribution { get; }
        int TypeCapacityContribution { get; }
        bool IsController { get; }
    }
}
