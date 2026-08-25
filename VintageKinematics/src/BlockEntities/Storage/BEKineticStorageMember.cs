using System;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using VintageKinematics.Api.Storage;

namespace VintageKinematics.BlockEntities.Storage
{
    /// <summary>
    /// Shared persisted identity for controllers and cells. Never owns item data.
    /// </summary>
    public abstract partial class BEKineticStorageMember : BlockEntity, IVKStorageStructureMember
    {
        public string WarehouseId { get; protected set; }
        public BlockPos ControllerPos { get; protected set; }
        public abstract long CapacityContribution { get; }
        public virtual int TypeCapacityContribution => 0;
        public abstract bool IsController { get; }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            WriteStorageMemberAttributes(tree);
        }

        internal void WriteStorageMemberAttributes(ITreeAttribute tree)
        {
            tree.SetString(StorageBlockEntityKeys.WarehouseId, WarehouseId ?? string.Empty);
            if (ControllerPos == null) return;
            tree.SetInt(StorageBlockEntityKeys.ControllerX, ControllerPos.X);
            tree.SetInt(StorageBlockEntityKeys.ControllerY, ControllerPos.Y);
            tree.SetInt(StorageBlockEntityKeys.ControllerZ, ControllerPos.Z);
            tree.SetInt(StorageBlockEntityKeys.ControllerDimension, ControllerPos.dimension);
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
        {
            base.FromTreeAttributes(tree, worldForResolving);
            ReadStorageMemberAttributes(tree);
        }

        internal void ReadStorageMemberAttributes(ITreeAttribute tree)
        {
            string storedId = tree.GetString(StorageBlockEntityKeys.WarehouseId, string.Empty);
            WarehouseId = Guid.TryParse(storedId, out Guid parsed) && parsed != Guid.Empty
                ? parsed.ToString("D")
                : null;
            if (tree.HasAttribute(StorageBlockEntityKeys.ControllerX))
            {
                ControllerPos = new BlockPos(
                    tree.GetInt(StorageBlockEntityKeys.ControllerX),
                    tree.GetInt(StorageBlockEntityKeys.ControllerY),
                    tree.GetInt(StorageBlockEntityKeys.ControllerZ),
                    tree.GetInt(StorageBlockEntityKeys.ControllerDimension));
            }
            else
            {
                ControllerPos = null;
            }
        }

        protected void SetLink(string warehouseId, BlockPos controllerPos)
        {
            if (!Guid.TryParse(warehouseId, out Guid parsed) || parsed == Guid.Empty)
            {
                throw new ArgumentException("Warehouse id must be a non-empty UUID.", nameof(warehouseId));
            }
            WarehouseId = parsed.ToString("D");
            ControllerPos = controllerPos?.Copy()
                ?? throw new ArgumentNullException(nameof(controllerPos));
        }

        protected void ClearLink()
        {
            WarehouseId = null;
            ControllerPos = null;
        }
    }
}
