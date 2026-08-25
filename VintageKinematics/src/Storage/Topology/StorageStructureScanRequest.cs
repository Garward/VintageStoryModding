using System;
using System.Collections.Generic;

namespace VintageKinematics.Storage.Topology
{
    public sealed class StorageStructureScanRequest
    {
        private readonly IReadOnlyList<StorageTopologyPosition> knownMembers;

        public StorageTopologyPosition Controller { get; }
        public string WarehouseId { get; }
        public StorageTopologyLimits Limits { get; }
        public IReadOnlyList<StorageTopologyPosition> KnownMembers => knownMembers;
        public StorageTopologyPosition? ExcludedPosition { get; }

        public StorageStructureScanRequest(
            StorageTopologyPosition controller,
            string warehouseId,
            StorageTopologyLimits limits,
            IReadOnlyList<StorageTopologyPosition> knownMembers = null,
            StorageTopologyPosition? excludedPosition = null)
        {
            if (!Guid.TryParse(warehouseId, out Guid parsed) || parsed == Guid.Empty)
            {
                throw new ArgumentException("Warehouse id must be a non-empty UUID.", nameof(warehouseId));
            }

            Controller = controller;
            WarehouseId = parsed.ToString("D");
            Limits = limits;
            ExcludedPosition = excludedPosition;
            if (knownMembers == null)
            {
                this.knownMembers = Array.Empty<StorageTopologyPosition>();
            }
            else
            {
                StorageTopologyPosition[] copy = new StorageTopologyPosition[knownMembers.Count];
                for (int i = 0; i < knownMembers.Count; i++) copy[i] = knownMembers[i];
                this.knownMembers = copy;
            }
        }
    }
}
