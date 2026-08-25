using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using VintageKinematics.Api.Storage;
using VintageKinematics.Storage.Topology;
using Xunit;

namespace VintageKinematics.Tests.Storage.Topology
{
    public class WorldStorageTopologySourceTests
    {
        [Fact]
        public void BlockPosition_RoundTripsInternalYAndDimension()
        {
            BlockPos original = new BlockPos(12, 345, -67, 3);

            StorageTopologyPosition topology = WorldStorageTopologySource.FromBlockPos(original);
            BlockPos restored = WorldStorageTopologySource.ToBlockPos(topology);

            Assert.Equal(original.X, restored.X);
            Assert.Equal(original.InternalY, restored.InternalY);
            Assert.Equal(original.Z, restored.Z);
            Assert.Equal(original.dimension, restored.dimension);
        }

        [Fact]
        public void Placement_ClientUsesSyncedOnlineStateWithoutServerOnlyIndex()
        {
            Assert.True(WorldStoragePlacementPolicy.ControllerAcceptsPlacement(
                EnumAppSide.Client,
                StorageState.Online,
                serverIndexReady: false));
        }

        [Fact]
        public void Placement_ClientCanSubmitIntentBeforeControllerLoadCompletes()
        {
            Assert.True(WorldStoragePlacementPolicy.ClientCanSubmitLinkedIntent(
                "cbb91396-f8c6-4992-8447-504841a13ed9",
                hasControllerPosition: true));
            Assert.False(WorldStoragePlacementPolicy.ClientCanSubmitLinkedIntent(
                null,
                hasControllerPosition: true));
        }

        [Fact]
        public void Placement_ServerStillRequiresAuthoritativeIndex()
        {
            Assert.False(WorldStoragePlacementPolicy.ControllerAcceptsPlacement(
                EnumAppSide.Server,
                StorageState.Online,
                serverIndexReady: false));
            Assert.True(WorldStoragePlacementPolicy.ControllerAcceptsPlacement(
                EnumAppSide.Server,
                StorageState.Online,
                serverIndexReady: true));
        }
    }
}
