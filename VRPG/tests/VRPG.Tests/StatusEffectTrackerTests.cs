using System;
using System.Collections.Generic;
using VRPG.Data.Definitions;
using VRPG.Modules.Rpg.StatusEffects;
using Xunit;

namespace VRPG.Tests;

public sealed class StatusEffectTrackerTests
{
    private static StatusEffectTracker CreateTracker()
    {
        var definitions = new Dictionary<string, StatusEffectDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["vrpg:corrosion"] = new StatusEffectDefinition
            {
                Code = "vrpg:corrosion",
                StackMode = "refresh",
                MaxStacks = 10,
                DefaultDurationSeconds = 8f
            },
            ["vrpg:stagger"] = new StatusEffectDefinition
            {
                Code = "vrpg:stagger",
                StackMode = "refresh",
                MaxStacks = 1,
                DefaultDurationSeconds = 6f
            }
        };
        return new StatusEffectTracker(code => definitions.TryGetValue(code, out StatusEffectDefinition? value) ? value : null);
    }

    [Fact]
    public void AddStacksAccumulatesAndCapsPerOwner()
    {
        StatusEffectTracker tracker = CreateTracker();

        Assert.Equal(2, tracker.AddStacks(10, "corrosion", 100, 2));
        Assert.Equal(4, tracker.AddStacks(10, "corrosion", 100, 2));
        Assert.Equal(10, tracker.AddStacks(10, "corrosion", 100, 20));
        Assert.Equal(2, tracker.AddStacks(10, "corrosion", 200, 2));

        Assert.Equal(10, tracker.GetOwned(10, "corrosion", 100)!.Stacks);
        Assert.Equal(2, tracker.GetOwned(10, "corrosion", 200)!.Stacks);
        Assert.Equal(2, tracker.Get(10).Count);
    }

    [Fact]
    public void BuildupCapsAndCanBeConsumedInParts()
    {
        StatusEffectTracker tracker = CreateTracker();

        Assert.Equal(60f, tracker.AddMagnitude(10, "stagger", 100, 60f, 100f));
        Assert.Equal(100f, tracker.AddMagnitude(10, "stagger", 100, 60f, 100f));
        Assert.Equal(60f, tracker.ConsumeMagnitude(10, "stagger", 100, 60f));
        Assert.Equal(40f, tracker.GetOwned(10, "stagger", 100)!.Magnitude);
        Assert.Equal(40f, tracker.ConsumeMagnitude(10, "stagger", 100, 60f));
        Assert.Null(tracker.GetOwned(10, "stagger", 100));
    }

    [Fact]
    public void MutationsNotifyStatusSyncOwner()
    {
        StatusEffectTracker tracker = CreateTracker();
        var changed = new List<long>();
        tracker.Changed += changed.Add;

        tracker.AddStacks(10, "corrosion", 100, 2);
        tracker.AddMagnitude(10, "stagger", 100, 18f, 100f);
        tracker.ConsumeMagnitude(10, "stagger", 100, 18f);

        Assert.Equal(new long[] { 10, 10, 10 }, changed);
    }
}
