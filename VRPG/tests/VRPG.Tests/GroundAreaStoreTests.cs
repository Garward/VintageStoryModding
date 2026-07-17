using VRPG.Client.Visuals;
using VRPG.Network;
using Xunit;

namespace VRPG.Tests;

public sealed class GroundAreaStoreTests
{
    private static GroundAreaUpsertPacket Packet(long id, int remainingMs, GroundAreaState state = GroundAreaState.Active)
    {
        return new GroundAreaUpsertPacket { Id = id, StyleCode = "vrpg:test", Radius = 2f, State = (byte)state, RemainingMs = remainingMs };
    }

    [Fact]
    public void UpsertComputesLocalExpiryFromRemainingTime()
    {
        var store = new GroundAreaStore();
        store.Upsert(Packet(1, remainingMs: 5000), nowMs: 1000);
        ClientGroundArea area = System.Linq.Enumerable.Single(store.All);
        Assert.Equal(6000, area.LocalExpiresAtMs);
    }

    [Fact]
    public void StateChangeStampsTransitionTimeForFlashAnimation()
    {
        var store = new GroundAreaStore();
        store.Upsert(Packet(1, 60000, GroundAreaState.Armed), nowMs: 1000);
        store.Upsert(Packet(1, 59000, GroundAreaState.Triggered), nowMs: 2000);
        ClientGroundArea area = System.Linq.Enumerable.Single(store.All);
        Assert.Equal(GroundAreaState.Triggered, area.State);
        Assert.Equal(2000, area.StateChangedAtMs);
    }

    [Fact]
    public void SameStateUpsertKeepsOriginalTransitionStamp()
    {
        var store = new GroundAreaStore();
        store.Upsert(Packet(1, 60000, GroundAreaState.Active), nowMs: 1000);
        store.Upsert(Packet(1, 59000, GroundAreaState.Active), nowMs: 5000);
        Assert.Equal(1000, System.Linq.Enumerable.Single(store.All).StateChangedAtMs);
    }

    [Fact]
    public void PruneDropsAreasPastLocalExpiryEvenWithoutRemovePacket()
    {
        var store = new GroundAreaStore();
        store.Upsert(Packet(1, 1000), nowMs: 0);
        store.Prune(nowMs: 1500);
        Assert.Empty(store.All);
    }

    [Fact]
    public void RemoveDeletesTheArea()
    {
        var store = new GroundAreaStore();
        store.Upsert(Packet(1, 60000), nowMs: 0);
        store.Remove(1);
        Assert.Empty(store.All);
    }
}
