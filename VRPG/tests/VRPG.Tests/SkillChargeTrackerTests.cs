using VRPG.Modules.Rpg.Skills;
using Xunit;

namespace VRPG.Tests;

public sealed class SkillChargeTrackerTests
{
    [Fact]
    public void NewReservoirStartsFullAndRecoversSequentially()
    {
        var tracker = new SkillChargeTracker();

        SkillChargeSnapshot initial = tracker.Snapshot("player", "vrpg:junk_toss", 5, 1800, 1000);
        Assert.Equal(5, initial.Current);
        Assert.Equal(0, initial.NextRechargeAtMilliseconds);

        Assert.True(tracker.TryConsume("player", "vrpg:junk_toss", 5, 1800, 1000, out SkillChargeSnapshot spent));
        Assert.Equal(4, spent.Current);
        Assert.Equal(2800, spent.NextRechargeAtMilliseconds);

        Assert.True(tracker.TryConsume("player", "vrpg:junk_toss", 5, 1800, 1500, out spent));
        Assert.Equal(3, spent.Current);
        Assert.Equal(2800, spent.NextRechargeAtMilliseconds);

        SkillChargeSnapshot oneRecovered = tracker.Snapshot("player", "vrpg:junk_toss", 5, 1800, 2800);
        Assert.Equal(4, oneRecovered.Current);
        Assert.Equal(4600, oneRecovered.NextRechargeAtMilliseconds);
    }

    [Fact]
    public void LongElapsedTimeRestoresUpToMaximum()
    {
        var tracker = new SkillChargeTracker();
        for (int i = 0; i < 5; i++)
        {
            Assert.True(tracker.TryConsume("player", "vrpg:junk_toss", 5, 1000, 0, out _));
        }

        Assert.False(tracker.TryConsume("player", "vrpg:junk_toss", 5, 1000, 500, out SkillChargeSnapshot empty));
        Assert.Equal(0, empty.Current);
        Assert.Equal(0.5f, empty.RechargeRemainingSeconds(500), 3);

        SkillChargeSnapshot full = tracker.Snapshot("player", "vrpg:junk_toss", 5, 1000, 5000);
        Assert.Equal(5, full.Current);
        Assert.Equal(0, full.NextRechargeAtMilliseconds);
    }

    [Fact]
    public void PlayersAndSkillsHaveIndependentReservoirs()
    {
        var tracker = new SkillChargeTracker();
        tracker.TryConsume("one", "vrpg:junk_toss", 5, 1000, 0, out _);

        Assert.Equal(4, tracker.Snapshot("one", "vrpg:junk_toss", 5, 1000, 0).Current);
        Assert.Equal(5, tracker.Snapshot("two", "vrpg:junk_toss", 5, 1000, 0).Current);
        Assert.Equal(5, tracker.Snapshot("one", "vrpg:other", 5, 1000, 0).Current);
    }
}
