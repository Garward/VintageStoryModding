using VRPG.Client;
using VRPG.Network;
using Xunit;

namespace VRPG.Tests;

public sealed class SkillChargeProjectionTests
{
    [Fact]
    public void HudAndInputSeeRecoveredLastChargeTogether()
    {
        SkillLoadoutSlotPacket entry = Recovering(current: 0, maximum: 5, firstRecovery: 0.4f);

        Assert.Equal(0, SkillChargeProjection.Current(entry, 1000, 1399));
        Assert.Equal(1, SkillChargeProjection.Current(entry, 1000, 1400));
        Assert.Equal(1.8, SkillChargeProjection.RemainingSeconds(entry, 1000, 1400), 3);
    }

    [Fact]
    public void ProjectionRecoversSequentiallyWithoutExceedingMaximum()
    {
        SkillLoadoutSlotPacket entry = Recovering(current: 1, maximum: 5, firstRecovery: 0.5f);

        Assert.Equal(2, SkillChargeProjection.Current(entry, 1000, 1500));
        Assert.Equal(3, SkillChargeProjection.Current(entry, 1000, 3300));
        Assert.Equal(5, SkillChargeProjection.Current(entry, 1000, 9000));
        Assert.Equal(0.0, SkillChargeProjection.RemainingSeconds(entry, 1000, 9000), 3);
    }

    [Fact]
    public void MissingRecoveryTimeDoesNotInventACharge()
    {
        SkillLoadoutSlotPacket entry = Recovering(current: 0, maximum: 5, firstRecovery: 0f);

        Assert.Equal(0, SkillChargeProjection.Current(entry, 1000, 9000));
        Assert.Equal(0.0, SkillChargeProjection.RemainingSeconds(entry, 1000, 9000), 3);
    }

    private static SkillLoadoutSlotPacket Recovering(int current, int maximum, float firstRecovery)
    {
        return new SkillLoadoutSlotPacket
        {
            CurrentCharges = current,
            MaximumCharges = maximum,
            CooldownSeconds = 1.8f,
            CooldownRemainingSeconds = firstRecovery
        };
    }
}
