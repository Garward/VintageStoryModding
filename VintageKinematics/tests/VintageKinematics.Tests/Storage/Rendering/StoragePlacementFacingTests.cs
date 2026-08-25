using Vintagestory.API.MathTools;
using VintageKinematics.BlockEntities.Storage;
using VintageKinematics.Blocks;
using Xunit;

namespace VintageKinematics.Tests.Storage.Rendering
{
    public sealed class StoragePlacementFacingTests
    {
        [Theory]
        [InlineData("north", "s")]
        [InlineData("east", "e")]
        [InlineData("south", "n")]
        [InlineData("west", "w")]
        public void ControllerFrontUsesVkStoragePlayerFacingConvention(
            string playerFacing,
            string expectedSide)
        {
            string side = BlockKineticWarehouseMember.SideFacingPlayer(
                BlockFacing.FromCode(playerFacing));

            Assert.Equal(expectedSide, side);
        }

        [Theory]
        [InlineData("n", "north")]
        [InlineData("e", "west")]
        [InlineData("s", "south")]
        [InlineData("w", "east")]
        public void WarehousePortAutomationUsesPhysicalRenderedFace(
            string side,
            string expectedFacing)
        {
            BlockFacing facing = BEKineticWarehousePort.InterfaceFacing(side);

            Assert.Same(BlockFacing.FromCode(expectedFacing), facing);
        }
    }
}
