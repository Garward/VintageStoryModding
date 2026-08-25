using System;

namespace VintageKinematics.Storage.Topology
{
    internal enum StoragePlacementRole
    {
        Controller,
        Cell
    }

    internal enum StoragePlacementIssue
    {
        None,
        ControllerTouchesStorage,
        NoWarehouseNeighbor,
        UnlinkedNeighbor,
        ConflictingWarehouses,
        ControllerUnavailable
    }

    internal readonly struct StoragePlacementNeighbor
    {
        public string WarehouseId { get; }
        public StorageTopologyPosition? Controller { get; }
        public bool ControllerOnline { get; }
        public bool IsLinked => !string.IsNullOrWhiteSpace(WarehouseId) && Controller.HasValue;

        public StoragePlacementNeighbor(
            string warehouseId,
            StorageTopologyPosition? controller,
            bool controllerOnline)
        {
            WarehouseId = warehouseId;
            Controller = controller;
            ControllerOnline = controllerOnline;
        }
    }

    internal sealed class StoragePlacementDecision
    {
        public bool Allowed => Issue == StoragePlacementIssue.None;
        public StoragePlacementIssue Issue { get; }
        public string WarehouseId { get; }
        public StorageTopologyPosition? Controller { get; }

        public StoragePlacementDecision(
            StoragePlacementIssue issue,
            string warehouseId = null,
            StorageTopologyPosition? controller = null)
        {
            Issue = issue;
            WarehouseId = warehouseId;
            Controller = controller;
        }
    }
}
