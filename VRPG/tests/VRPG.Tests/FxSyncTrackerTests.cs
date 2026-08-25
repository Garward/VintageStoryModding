using VRPG.Client.Visuals;
using Xunit;

namespace VRPG.Tests;

public sealed class FxSyncTrackerTests
{
    [Fact]
    public void BaselineRemovesClockOffsetAndReportsJitterAsDrift()
    {
        var tracker = new FxSyncTracker();
        FxSyncObservation floor = tracker.Observe("vrpg:test", 1000, 6040)!;
        FxSyncObservation jittered = tracker.Observe("vrpg:test", 2000, 7065)!;
        FxSyncMeasurement measurement = tracker.Complete(jittered, 7070, null);

        Assert.Equal(5040, floor.BaselineMs);
        Assert.Equal(5040, jittered.BaselineMs);
        Assert.Equal(25, jittered.DriftMs);
        Assert.Equal(30, measurement.GameplayToVisualMs);
    }

    [Fact]
    public void LatencyStepDoesNotImmediatelyBecomeNewBaseline()
    {
        var tracker = new FxSyncTracker();
        tracker.Observe("vrpg:test", 1000, 6040);
        FxSyncObservation stepped = tracker.Observe("vrpg:test", 2000, 7140)!;

        Assert.Equal(5040, stepped.BaselineMs);
        Assert.Equal(100, stepped.DriftMs);
    }

    [Fact]
    public void MissingServerStampDisablesMeasurement()
    {
        var tracker = new FxSyncTracker();
        Assert.Null(tracker.Observe("vrpg:test", 0, 1000));
    }
}
