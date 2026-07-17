using VRPG.Modules.Rpg.Combat;
using VRPG.Network;
using Xunit;

namespace VRPG.Tests;

public sealed class GroundAreaTableTests
{
    private static GroundAreaRecord Record(long id, long expiresAtMs, GroundAreaState state = GroundAreaState.Active)
    {
        return new GroundAreaRecord { Id = id, StyleCode = "vrpg:test", Radius = 2f, State = state, ExpiresAtMs = expiresAtMs };
    }

    [Fact]
    public void AreasNearExpiryTransitionToExpiringExactlyOnce()
    {
        var table = new GroundAreaTable();
        table.Upsert(Record(1, expiresAtMs: 2000));

        ExpiryTransitions first = table.TickExpiry(nowMs: 800);
        Assert.Single(first.NowExpiring);
        Assert.Equal(GroundAreaState.Expiring, first.NowExpiring[0].State);

        ExpiryTransitions second = table.TickExpiry(nowMs: 900);
        Assert.Empty(second.NowExpiring);
    }

    [Fact]
    public void ExpiredAreasAreRemovedAndReported()
    {
        var table = new GroundAreaTable();
        table.Upsert(Record(1, expiresAtMs: 1000));
        ExpiryTransitions transitions = table.TickExpiry(nowMs: 1500);
        Assert.Contains(1L, transitions.Expired);
        Assert.Empty(table.All);
    }

    [Fact]
    public void ArmedAreasDoNotEnterExpiringEarly()
    {
        var table = new GroundAreaTable();
        table.Upsert(Record(1, expiresAtMs: 60000, GroundAreaState.Armed));
        Assert.Empty(table.TickExpiry(nowMs: 58000).NowExpiring);   // 2000 ms left > 1500 ms lead
        Assert.Single(table.TickExpiry(nowMs: 58600).NowExpiring);  // 1400 ms left <= lead
    }
}
