using Vintagestory.API.MathTools;
using VintageKinematics.Api.Storage;

namespace VintageKinematics.BlockEntities.Storage
{
    /// <summary>
    /// Zero-capacity topology member for explicit automation and power interfaces.
    /// Transfer mechanics are implemented separately from this identity/shape adapter.
    /// </summary>
    public sealed partial class BEKineticWarehousePort : BEKineticWarehouseCell, IVKStoragePort
    {
        public override long CapacityContribution => 0;

        public StoragePortRole PortRole => Block?.Variant?["port"] switch
        {
            "beltoutput" => StoragePortRole.Export,
            "kineticinput" => StoragePortRole.ControllerAccess,
            _ => StoragePortRole.Import
        };

        public BlockFacing Facing => InterfaceFacing(Block?.Variant?["side"]);

        /// <summary>
        /// Resolve the physical interface face after the terminal-style player-facing
        /// model rotation. East/west variant labels are intentionally mirrored because
        /// positive Vintage Story Y rotation turns the north model counter-clockwise.
        /// </summary>
        internal static BlockFacing InterfaceFacing(string side)
        {
            return side switch
            {
                "e" => BlockFacing.WEST,
                "s" => BlockFacing.SOUTH,
                "w" => BlockFacing.EAST,
                _ => BlockFacing.NORTH
            };
        }

        public int TransferRate => System.Math.Max(
            0,
            Block?.Attributes?["vkStorageTransferRate"].AsInt(1) ?? 1);
    }
}
