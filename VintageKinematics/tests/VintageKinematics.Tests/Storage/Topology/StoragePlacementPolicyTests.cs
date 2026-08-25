using System.Collections.Generic;
using VintageKinematics.Storage.Topology;
using Xunit;

namespace VintageKinematics.Tests.Storage.Topology
{
    public class StoragePlacementPolicyTests
    {
        private const string First = "00000000-0000-0000-0000-000000000001";
        private const string Second = "00000000-0000-0000-0000-000000000002";
        private static readonly StorageTopologyPosition FirstController = new(1, 2, 3, 0);
        private static readonly StorageTopologyPosition SecondController = new(4, 5, 6, 0);

        [Fact]
        public void ControllerCannotTouchAnExistingStorageMember()
        {
            StoragePlacementDecision result = StoragePlacementPolicy.Evaluate(
                StoragePlacementRole.Controller,
                new[] { Neighbor(First, FirstController, true) });

            Assert.Equal(StoragePlacementIssue.ControllerTouchesStorage, result.Issue);
        }

        [Fact]
        public void CellRequiresOneOnlineWarehouse()
        {
            StoragePlacementDecision result = StoragePlacementPolicy.Evaluate(
                StoragePlacementRole.Cell,
                new[]
                {
                    Neighbor(First, FirstController, true),
                    Neighbor(First, FirstController, false)
                });

            Assert.True(result.Allowed);
            Assert.Equal(First, result.WarehouseId);
            Assert.Equal(FirstController, result.Controller);
        }

        [Fact]
        public void CellCannotBridgeTwoControllers()
        {
            StoragePlacementDecision result = StoragePlacementPolicy.Evaluate(
                StoragePlacementRole.Cell,
                new[]
                {
                    Neighbor(First, FirstController, true),
                    Neighbor(Second, SecondController, true)
                });

            Assert.Equal(StoragePlacementIssue.ConflictingWarehouses, result.Issue);
        }

        [Fact]
        public void CellCannotAttachThroughUnlinkedEvidence()
        {
            StoragePlacementDecision result = StoragePlacementPolicy.Evaluate(
                StoragePlacementRole.Cell,
                new[] { new StoragePlacementNeighbor(null, null, false) });

            Assert.Equal(StoragePlacementIssue.UnlinkedNeighbor, result.Issue);
        }

        [Fact]
        public void CellCannotAttachWhileControllerIsUnavailable()
        {
            StoragePlacementDecision result = StoragePlacementPolicy.Evaluate(
                StoragePlacementRole.Cell,
                new[] { Neighbor(First, FirstController, false) });

            Assert.Equal(StoragePlacementIssue.ControllerUnavailable, result.Issue);
        }

        private static StoragePlacementNeighbor Neighbor(
            string warehouseId,
            StorageTopologyPosition controller,
            bool online)
        {
            return new StoragePlacementNeighbor(warehouseId, controller, online);
        }
    }
}
