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
                if (neighbors.Count == 0)
                {
                    return new StoragePlacementDecision(StoragePlacementIssue.None);
                }

                string orphanWarehouseId = null;
                StorageTopologyPosition? orphanController = null;
                foreach (StoragePlacementNeighbor neighbor in neighbors)
                {
                    if (!neighbor.IsLinked || neighbor.ControllerPresent)
                    {
                        return new StoragePlacementDecision(
                            StoragePlacementIssue.ControllerTouchesStorage);
                    }
                    if (orphanWarehouseId == null)
                    {
                        orphanWarehouseId = neighbor.WarehouseId;
                        orphanController = neighbor.Controller;
                    }
                    else if (!string.Equals(
                            orphanWarehouseId,
                            neighbor.WarehouseId,
                            StringComparison.Ordinal)
                        || orphanController != neighbor.Controller)
                    {
                        return new StoragePlacementDecision(
                            StoragePlacementIssue.ControllerTouchesStorage);
                    }
                }

                // A single consistent set of cells whose old controller is absent is the
                // physical proof required to reattach a moved terminal to that warehouse.
                return new StoragePlacementDecision(
                    StoragePlacementIssue.None,
                    orphanWarehouseId,
                    orphanController);
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
