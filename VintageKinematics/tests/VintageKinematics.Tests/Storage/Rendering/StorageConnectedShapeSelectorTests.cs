using VintageKinematics.Storage.Rendering;
using Xunit;

namespace VintageKinematics.Tests.Storage.Rendering
{
    public sealed class StorageConnectedShapeSelectorTests
    {
        [Theory]
        [InlineData("ew", "storagecell-mask-ew.json")]
        [InlineData("ns", "storagecell-mask-ns.json")]
        [InlineData("ud", "storagecell-mask-ud.json")]
        [InlineData("ne", "storagecell-mask-ne.json")]
        [InlineData("es", "storagecell-mask-es.json")]
        [InlineData("eu", "storagecell-mask-eu.json")]
        [InlineData("wd", "storagecell-mask-wd.json")]
        [InlineData("n", "storagecell-mask-n.json")]
        [InlineData("u", "storagecell-mask-u.json")]
        public void SelectCellMapsExactFaceMask(
            string mask,
            string filename)
        {
            StorageConnectedShapeSelection selected =
                StorageConnectedShapeSelector.SelectCell(mask);

            Assert.EndsWith(filename, selected.ShapePath);
            Assert.Equal(0, selected.RotateY);
            Assert.Equal(0, selected.RotateZ);
        }

        [Theory]
        [InlineData("")]
        [InlineData("nes")]
        [InlineData("neswud")]
        public void SelectCellUsesExactIsolatedAndJunctionMasks(string mask)
        {
            StorageConnectedShapeSelection selected =
                StorageConnectedShapeSelector.SelectCell(mask);

            Assert.EndsWith(
                "storagecell-mask-" + (mask.Length == 0 ? "isolated" : mask) + ".json",
                selected.ShapePath);
        }

        [Fact]
        public void SelectControllerUsesPlacedHorizontalSide()
        {
            StorageConnectedShapeSelection selected =
                StorageConnectedShapeSelector.SelectController("east");

            Assert.EndsWith("storagecontroller-north.json", selected.ShapePath);
            Assert.Equal(90, selected.RotateY);
        }

        [Theory]
        [InlineData("beltinput", "storageport-belt-input-north.json")]
        [InlineData("beltoutput", "storageport-belt-output-north.json")]
        [InlineData("kineticinput", "storageport-kinetic-input-north.json")]
        public void SelectPortUsesDedicatedCenteredInterface(string port, string filename)
        {
            StorageConnectedShapeSelection selected =
                StorageConnectedShapeSelector.SelectPort(port, "west");

            Assert.EndsWith(filename, selected.ShapePath);
            Assert.Equal(270, selected.RotateY);
        }

        [Fact]
        public void SelectElbowUsesDedicatedOverlayShape()
        {
            StorageConnectedShapeSelection selected =
                StorageConnectedShapeSelector.SelectElbow("y-east-south");

            Assert.EndsWith("storagecell-elbow-y-east-south.json", selected.ShapePath);
            Assert.Null(StorageConnectedShapeSelector.SelectElbow("not-an-elbow"));
        }
    }
}
