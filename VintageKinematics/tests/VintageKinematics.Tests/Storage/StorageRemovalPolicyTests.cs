using System.Collections.Generic;
using Vintagestory.API.MathTools;
using VintageKinematics.Api.Storage;
using VintageKinematics.Storage;
using VintageKinematics.Storage.Topology;
using VintageKinematics.BlockEntities.Storage;
using Xunit;

namespace VintageKinematics.Tests.Storage
{
    public class StorageRemovalPolicyTests
    {
        [Theory]
        [InlineData(StorageState.StructureUnknown, true, false)]
        [InlineData(StorageState.RecoveryRequired, true, false)]
        [InlineData(StorageState.Corrupt, true, false)]
        [InlineData(StorageState.Online, false, false)]
        [InlineData(StorageState.Online, true, true)]
        public void UnsafeOrReconstructingStateCannotEvaluateRemoval(
            StorageState state,
            bool indexReady,
            bool rebuilding)
        {
            Assert.False(StorageRemovalPolicy.CanEvaluateCapacity(
                state,
                indexReady,
                rebuilding,
                StorageRemovalKind.PlayerBreak));
        }

        [Theory]
        [InlineData(StorageRemovalKind.ContraptionCapture)]
        [InlineData(StorageRemovalKind.BlockReplacement)]
        public void MovingOrReplacingStorageIsAlwaysDenied(StorageRemovalKind kind)
        {
            Assert.False(StorageRemovalPolicy.CanEvaluateCapacity(
                StorageState.Online,
                true,
                false,
                kind));
        }

        [Fact]
        public void StableOnlineWarehouseCanSimulateOrdinaryRemoval()
        {
            Assert.True(StorageRemovalPolicy.CanEvaluateCapacity(
                StorageState.Online,
                true,
                false,
                StorageRemovalKind.PlayerBreak));
        }

        [Fact]
        public void OrphanedEdgeCellCanBeRemovedWhenCapacityRemains()
        {
            StorageTopologyPosition target = Position(2);
            var result = OrphanedStorageRemovalEvaluator.Evaluate(
                BlockPos(target),
                target,
                Members((0, 1024), (1, 1024), (2, 1024)),
                1500);

            Assert.True(result.Allowed);
            Assert.Equal(2048, result.CapacityAfterRemoval);
        }

        [Fact]
        public void OrphanedCellCannotCauseOverflow()
        {
            StorageTopologyPosition target = Position(2);
            var result = OrphanedStorageRemovalEvaluator.Evaluate(
                BlockPos(target),
                target,
                Members((0, 1024), (1, 1024), (2, 1024)),
                2500);

            Assert.False(result.Allowed);
            Assert.Equal("vintagekinematics:storage-removal-would-overflow", result.MessageLangCode);
        }

        [Fact]
        public void OrphanedBridgeCellCannotSplitWarehouse()
        {
            StorageTopologyPosition target = Position(1);
            var result = OrphanedStorageRemovalEvaluator.Evaluate(
                BlockPos(target),
                target,
                Members((0, 1024), (1, 1024), (2, 1024)),
                0);

            Assert.False(result.Allowed);
            Assert.Equal("vintagekinematics:storage-removal-would-split", result.MessageLangCode);
        }

        [Fact]
        public void FinalOrphanedCellCanBeRemovedWhenEmpty()
        {
            StorageTopologyPosition target = Position(0);
            var result = OrphanedStorageRemovalEvaluator.Evaluate(
                BlockPos(target),
                target,
                Members((0, 1024)),
                0);

            Assert.True(result.Allowed);
            Assert.Equal(0, result.CapacityAfterRemoval);
        }

        [Fact]
        public void LastOldControllerAnchorCannotBeRemovedBeforeOtherCells()
        {
            StorageTopologyPosition target = Position(0);
            var result = OrphanedStorageRemovalEvaluator.Evaluate(
                BlockPos(target),
                target,
                Members((0, 1024), (1, 1024)),
                0,
                new StorageTopologyPosition(-1, 0, 0, 0));

            Assert.False(result.Allowed);
            Assert.Equal("vintagekinematics:storage-removal-would-split", result.MessageLangCode);
        }

        [Fact]
        public void PersistedControllerAtAnotherPositionResumesRelocationOnLoad()
        {
            Assert.True(BEKineticWarehouseTerminal.IsDifferentPosition(
                new BlockPos(1, 2, 3),
                new BlockPos(4, 2, 3)));
            Assert.False(BEKineticWarehouseTerminal.IsDifferentPosition(
                new BlockPos(1, 2, 3),
                new BlockPos(1, 2, 3)));
        }

        private static Dictionary<StorageTopologyPosition, long> Members(
            params (int X, long Capacity)[] values)
        {
            var members = new Dictionary<StorageTopologyPosition, long>();
            foreach ((int x, long capacity) in values)
            {
                members.Add(Position(x), capacity);
            }
            return members;
        }

        private static StorageTopologyPosition Position(int x) => new(x, 0, 0, 0);

        private static BlockPos BlockPos(StorageTopologyPosition position) =>
            new(position.X, position.InternalY, position.Z, position.Dimension);
    }
}
