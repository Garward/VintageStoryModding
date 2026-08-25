using VintageKinematics.Storage.Operations;
using Xunit;

namespace VintageKinematics.Tests.Storage
{
    public sealed class StoragePowerPolicyTests
    {
        [Fact]
        public void DisabledRequirement_IsAlwaysOperational()
        {
            Assert.True(StoragePowerPolicy.IsPowered(false, 4f));
        }

        [Fact]
        public void RequiredPower_RejectsMissingAndSlowDrives()
        {
            Assert.False(StoragePowerPolicy.IsPowered(true, 4f));
            Assert.False(StoragePowerPolicy.IsPowered(true, 4f, 0f, -3.99f));
        }

        [Fact]
        public void RequiredPower_AcceptsEitherRotationDirection()
        {
            Assert.True(StoragePowerPolicy.IsPowered(true, 4f, -4f));
        }

        [Fact]
        public void InvalidMinimumRPM_UsesSafeDefault()
        {
            Assert.Equal(16f, StoragePowerPolicy.NormalizeMinimumRPM(float.NaN));
            Assert.Equal(0f, StoragePowerPolicy.NormalizeMinimumRPM(-10f));
        }

        [Fact]
        public void CellScaling_UsesPhysicalCountAndRpmMultipliesLater()
        {
            float woodImpact = StoragePowerPolicy.CalculateStressImpact(true, 16f, 0.25f, 64);
            float reinforcedImpact = StoragePowerPolicy.CalculateStressImpact(true, 16f, 0.25f, 16);

            Assert.Equal(32f, woodImpact);
            Assert.Equal(20f, reinforcedImpact);
            Assert.Equal(512f, woodImpact * 16f);
            Assert.Equal(320f, reinforcedImpact * 16f);
        }

        [Fact]
        public void DisabledPower_HasNoWarehouseStressImpact()
        {
            Assert.Equal(0f, StoragePowerPolicy.CalculateStressImpact(false, 16f, 0.25f, 100));
        }
    }
}
