using Vintagestory.API.Datastructures;
using VintageKinematics.Storage.Persistence;
using Xunit;

namespace VintageKinematics.Tests.Storage.Persistence
{
    public class StorageAttributeCodecTests
    {
        [Fact]
        public void Encode_IsDeterministicAcrossAttributeInsertionOrder()
        {
            TreeAttribute first = new TreeAttribute();
            first.SetInt("zeta", 2);
            first.SetString("alpha", "one");
            TreeAttribute second = new TreeAttribute();
            second.SetString("alpha", "one");
            second.SetInt("zeta", 2);

            byte[] firstBytes = StorageAttributeCodec.Encode(first);
            byte[] secondBytes = StorageAttributeCodec.Encode(second);

            Assert.Equal(firstBytes, secondBytes);
        }

        [Fact]
        public void Encode_NormalizesNestedTrees()
        {
            TreeAttribute firstChild = new TreeAttribute();
            firstChild.SetInt("b", 2);
            firstChild.SetInt("a", 1);
            TreeAttribute secondChild = new TreeAttribute();
            secondChild.SetInt("a", 1);
            secondChild.SetInt("b", 2);
            TreeAttribute first = new TreeAttribute { ["child"] = firstChild };
            TreeAttribute second = new TreeAttribute { ["child"] = secondChild };

            Assert.Equal(StorageAttributeCodec.Encode(first), StorageAttributeCodec.Encode(second));
        }
    }
}
