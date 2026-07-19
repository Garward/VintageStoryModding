using Vintagestory.API.MathTools;

namespace VintageKinematics.Api.Storage
{
    public enum StoragePortRole
    {
        Import,
        Export,
        ImportExport,
        ControllerAccess
    }

    /// <summary>
    /// Optional structure member interface for hatches and terminals.
    /// </summary>
    public interface IVKStoragePort : IVKStorageStructureMember
    {
        StoragePortRole PortRole { get; }
        BlockFacing Facing { get; }
        int TransferRate { get; }
    }
}
