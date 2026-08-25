using System;

namespace VintageKinematics.Storage.Topology
{
    /// <summary>
    /// Immutable topology data for one storage structure member.
    /// </summary>
    public sealed class StorageMemberSnapshot
    {
        public StorageTopologyPosition Position { get; }
        public string WarehouseId { get; }
        public StorageTopologyPosition? Controller { get; }
        public StorageMemberRole Role { get; }
        public long CapacityContribution { get; }
        public int TypeCapacityContribution { get; }
        public string BlockCode { get; }
        public bool IsController => Role == StorageMemberRole.Controller;
        public bool IsLinked => WarehouseId != null && Controller.HasValue;

        public StorageMemberSnapshot(
            StorageTopologyPosition position,
            string warehouseId,
            StorageMemberRole role,
            long capacityContribution,
            int typeCapacityContribution,
            string blockCode,
            StorageTopologyPosition? controller = null)
        {
            string normalizedWarehouseId = null;
            if (warehouseId != null)
            {
                if (!Guid.TryParse(warehouseId, out Guid parsed) || parsed == Guid.Empty)
                {
                    throw new ArgumentException("Warehouse id must be a non-empty UUID.", nameof(warehouseId));
                }
                normalizedWarehouseId = parsed.ToString("D");
            }
            if (capacityContribution < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacityContribution));
            }
            if (typeCapacityContribution < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(typeCapacityContribution));
            }
            if (string.IsNullOrWhiteSpace(blockCode))
            {
                throw new ArgumentException("Storage member block code is required.", nameof(blockCode));
            }

            Position = position;
            WarehouseId = normalizedWarehouseId;
            Controller = controller;
            Role = role;
            CapacityContribution = capacityContribution;
            TypeCapacityContribution = typeCapacityContribution;
            BlockCode = blockCode;
        }
    }
}
