using System.Linq;
using VintageKinematics.Storage.Rendering;
using Xunit;

namespace VintageKinematics.Tests.Storage.Rendering
{
    public sealed class StorageConcaveElbowTests
    {
        [Fact]
        public void MissingDiagonalSelectsPerpendicularElbow()
        {
            var selected = StorageConcaveElbow.Select("es", _ => false);

            Assert.Equal("y-east-south", Assert.Single(selected));
        }

        [Fact]
        public void FilledDiagonalSuppressesTwoByTwoInnerCorner()
        {
            var selected = StorageConcaveElbow.Select("es", _ => true);

            Assert.Empty(selected);
        }

        [Fact]
        public void OppositeConnectionsNeverCreateAnElbow()
        {
            Assert.Empty(StorageConcaveElbow.Select("ew", _ => false));
            Assert.Empty(StorageConcaveElbow.Select("ns", _ => false));
            Assert.Empty(StorageConcaveElbow.Select("ud", _ => false));
        }

        [Fact]
        public void JunctionSelectsOnlyCornersWithoutDiagonals()
        {
            var selected = StorageConcaveElbow.Select(
                "nesw",
                elbow => elbow.Name == "y-east-south");

            Assert.Equal(3, selected.Count);
            Assert.DoesNotContain("y-east-south", selected);
            Assert.Contains("y-east-north", selected);
            Assert.Equal(3, selected.Distinct().Count());
        }
    }
}
