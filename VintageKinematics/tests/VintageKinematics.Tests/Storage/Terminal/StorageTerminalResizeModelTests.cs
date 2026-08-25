using VintageKinematics.Gui.Storage;
using Xunit;

namespace VintageKinematics.Tests.Storage.Terminal
{
    public sealed class StorageTerminalResizeModelTests
    {
        [Fact]
        public void Drag_SnapsToWholeRows()
        {
            int rows = StorageTerminalResizeModel.RowsForDrag(
                startRows: 5,
                mouseDeltaPixels: 112,
                guiScale: 1,
                maximumRows: 10);

            Assert.Equal(7, rows);
        }

        [Fact]
        public void Drag_ClampsAtMinimumAndAvailableMaximum()
        {
            Assert.Equal(5, StorageTerminalResizeModel.RowsForDrag(6, -500, 1, 9));
            Assert.Equal(8, StorageTerminalResizeModel.RowsForDrag(6, 500, 1, 8));
        }

        [Fact]
        public void AvailableSpace_LimitsExpansionByScaledRowHeight()
        {
            Assert.Equal(7, StorageTerminalResizeModel.MaximumRowsThatFit(
                currentRows: 5,
                availablePixelsBelow: 110,
                guiScale: 1));
            Assert.Equal(6, StorageTerminalResizeModel.MaximumRowsThatFit(
                currentRows: 5,
                availablePixelsBelow: 110,
                guiScale: 2));
        }
    }
}
