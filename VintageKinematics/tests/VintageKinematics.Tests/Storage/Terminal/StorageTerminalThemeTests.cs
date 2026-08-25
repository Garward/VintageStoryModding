using VintageKinematics.Gui.Storage;
using Xunit;

namespace VintageKinematics.Tests.Storage.Terminal
{
    public sealed class StorageTerminalThemeTests
    {
        [Fact]
        public void ItemSize_PreservesVanillaSlotMarginRatio()
        {
            double margin = (StorageTerminalTheme.TileSize - StorageTerminalTheme.ItemSize) / 2d;

            Assert.InRange(StorageTerminalTheme.ItemSize, 26d, 27d);
            Assert.True(margin >= 11d);
        }
    }
}
