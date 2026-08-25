using System;
using System.Collections.Generic;

namespace VintageKinematics.Storage.Topology
{
    /// <summary>
    /// Pure ownership rules applied before a controller or capacity cell enters the world.
    /// </summary>
    internal static class StoragePlacementPolicy
    {
        public static StoragePlacementDecision Evaluate(
            StoragePlacementRole role,
            IReadOnlyCollection<StoragePlacementNeighbor> neighbors)
        {
            neighbors ??= Array.Empty<StoragePlacementNeighbor>();
            if (role == StoragePlacementRole.Controller)
            {
                return neighbors.Count == 0
                    ? new StoragePlacementDecision(StoragePlacementIssue.None)
                    : new StoragePlacementDecision(StoragePlacementIssue.ControllerTouchesStorage);
            }
            if (neighbors.Count == 0)
            {
                return new StoragePlacementDecision(StoragePlacementIssue.NoWarehouseNeighbor);
            }

            string warehouseId = null;
            StorageTopologyPosition? controller = null;
            bool controllerOnline = false;
            foreach (StoragePlacementNeighbor neighbor in neighbors)
            {
                if (!neighbor.IsLinked)
                {
                    return new StoragePlacementDecision(StoragePlacementIssue.UnlinkedNeighbor);
                }
                if (warehouseId == null)
                {
                    warehouseId = neighbor.WarehouseId;
                    controller = neighbor.Controller;
                }
                else if (!string.Equals(warehouseId, neighbor.WarehouseId, StringComparison.Ordinal)
                    || controller != neighbor.Controller)
                {
                    return new StoragePlacementDecision(StoragePlacementIssue.ConflictingWarehouses);
                }
                controllerOnline |= neighbor.ControllerOnline;
            }

            return controllerOnline
                ? new StoragePlacementDecision(StoragePlacementIssue.None, warehouseId, controller)
                : new StoragePlacementDecision(StoragePlacementIssue.ControllerUnavailable);
        }
    }
}
