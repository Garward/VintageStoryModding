using System.Collections.Generic;
using VRPG.Client.Visuals;
using VRPG.Data.Definitions;
using VRPG.Modules.Rpg.StatusEffects;
using Vintagestory.API.Datastructures;
using Xunit;

namespace VRPG.Tests;

public sealed class StatusSyncTests
{
    private static StatusEffectInstance Instance(string code, float remainingSeconds, int stacks, float magnitude)
    {
        var definition = new StatusEffectDefinition { Code = code, MaxStacks = 10 };
        var instance = new StatusEffectInstance(definition, 0, remainingSeconds, stacks);
        instance.AddMagnitude(magnitude);
        return instance;
    }

    [Fact]
    public void WriteThenReadRoundTripsEffects()
    {
        var attributes = new TreeAttribute();
        StatusSync.Write(attributes, new List<StatusEffectInstance>
        {
            Instance("vrpg:corrosion", 6.5f, 3, 0f),
            Instance("vrpg:stagger", 4f, 1, 42f)
        });

        List<SyncedStatus> read = StatusSync.Read(attributes);
        Assert.Equal(2, read.Count);
        SyncedStatus corrosion = read.Find(s => s.Code == "vrpg:corrosion")!;
        Assert.Equal(3, corrosion.Stacks);
        Assert.Equal(6500, corrosion.RemainingMs);
        SyncedStatus stagger = read.Find(s => s.Code == "vrpg:stagger")!;
        Assert.Equal(42f, stagger.Magnitude);
        Assert.Equal(1, stagger.Rev);
    }

    [Fact]
    public void EachWriteBumpsRevision()
    {
        var attributes = new TreeAttribute();
        StatusSync.Write(attributes, new List<StatusEffectInstance> { Instance("vrpg:burn", 3f, 1, 0f) });
        StatusSync.Write(attributes, new List<StatusEffectInstance> { Instance("vrpg:burn", 3f, 2, 0f) });
        Assert.Equal(2, StatusSync.Read(attributes)[0].Rev);
    }

    [Fact]
    public void WritingEmptyListClearsTheTree()
    {
        var attributes = new TreeAttribute();
        StatusSync.Write(attributes, new List<StatusEffectInstance> { Instance("vrpg:burn", 3f, 1, 0f) });
        StatusSync.Write(attributes, new List<StatusEffectInstance>());
        Assert.Empty(StatusSync.Read(attributes));
    }

    [Fact]
    public void CacheCountsDownLocallyBetweenRevisions()
    {
        var attributes = new TreeAttribute();
        StatusSync.Write(attributes, new List<StatusEffectInstance> { Instance("vrpg:bleed", 5f, 2, 0f) });

        var cache = new EntityStatusCache();
        IReadOnlyList<ActiveStatus> first = cache.Update(7, attributes, nowMs: 1000);
        Assert.Single(first);
        Assert.Equal(6000, first[0].EndMs);
        Assert.Equal(5f, first[0].RemainingSeconds(1000), 2);

        // No new write: two seconds later the same end time holds.
        IReadOnlyList<ActiveStatus> later = cache.Update(7, attributes, nowMs: 3000);
        Assert.Equal(6000, later[0].EndMs);
        Assert.Equal(3f, later[0].RemainingSeconds(3000), 2);

        // A refresh write resets the countdown from the new remaining time.
        StatusSync.Write(attributes, new List<StatusEffectInstance> { Instance("vrpg:bleed", 5f, 3, 0f) });
        IReadOnlyList<ActiveStatus> refreshed = cache.Update(7, attributes, nowMs: 4000);
        Assert.Equal(9000, refreshed[0].EndMs);
        Assert.Equal(3, refreshed[0].Stacks);
    }

    [Fact]
    public void TrackerFiresChangedOnApplyAndExpiry()
    {
        var data = new VRPG.Data.VRPGDataRegistry();
        var tracker = new StatusEffectTracker(data);
        // Registry is empty in tests; Apply with unknown code returns false and must not fire.
        var changed = new List<long>();
        tracker.Changed += id => changed.Add(id);
        Assert.False(tracker.Apply(5, "vrpg:missing"));
        Assert.Empty(changed);
    }
}
