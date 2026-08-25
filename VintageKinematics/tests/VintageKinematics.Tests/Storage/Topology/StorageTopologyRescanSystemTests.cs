using Vintagestory.API.MathTools;
using VintageKinematics.Storage.Topology;
using Xunit;

namespace VintageKinematics.Tests.Storage.Topology
{
    public class StorageTopologyRescanSystemTests
    {
        [Theory]
        [InlineData(0, 0, true)]
        [InlineData(-1, 0, true)]
        [InlineData(1, 0, false)]
        [InlineData(0, 1, false)]
        public void ChunkRetryWindowIncludesEveryPotentialScanColumn(
            int chunkX,
            int chunkZ,
            bool expected)
        {
            BlockPos controller = new BlockPos(0, 20, 0);

            bool result = StorageTopologyRescanSystem.CouldAffect(
                controller,
                new Vec2i(chunkX, chunkZ));

            Assert.Equal(expected, result);
        }

        [Fact]
        public void ChunkRetryWindowHandlesNegativeCoordinatesWithFloorDivision()
        {
            BlockPos controller = new BlockPos(-1, 20, -1);

            Assert.True(StorageTopologyRescanSystem.CouldAffect(controller, new Vec2i(-1, -1)));
            Assert.True(StorageTopologyRescanSystem.CouldAffect(controller, new Vec2i(0, 0)));
            Assert.False(StorageTopologyRescanSystem.CouldAffect(controller, new Vec2i(1, 1)));
        }
    }
}
