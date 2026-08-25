using VintageKinematics.Storage.Operations;
using Xunit;

namespace VintageKinematics.Tests.Storage
{
    public sealed class StoragePortTransferPolicyTests
    {
        [Fact]
        public void DefaultOutput_IsFourIndividualItemsPerSecond()
        {
            Assert.Equal(
                4f,
                StoragePortTransferPolicy.MaximumItemsPerSecond(
                    StoragePortTransferPolicy.DefaultOutputIntervalMs,
                    1));
        }

        [Theory]
        [InlineData(0, 50)]
        [InlineData(250, 250)]
        [InlineData(100000, 60000)]
        public void OutputInterval_IsSafelyBounded(int configured, int expected)
        {
            Assert.Equal(expected, StoragePortTransferPolicy.NormalizeOutputIntervalMs(configured));
        }
    }
}
