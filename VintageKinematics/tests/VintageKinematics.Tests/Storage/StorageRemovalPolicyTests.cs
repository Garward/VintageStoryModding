using VintageKinematics.Api.Storage;
using VintageKinematics.Storage;
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
    }
}
