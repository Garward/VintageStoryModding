using System.Linq;
using VRPG.Client.Visuals;
using Xunit;

namespace VRPG.Tests;

public sealed class CombatTextModelTests
{
    private static CombatTextModel Model(int maxEntries = 20, bool merge = true)
    {
        return new CombatTextModel(new CombatTextSettings { MaxEntries = maxEntries, MergeNumbers = merge });
    }

    [Fact]
    public void RapidHitsOnSameTargetAndTypeMergeIntoOneEntry()
    {
        CombatTextModel model = Model();
        model.AddNumber(1, 0, 12f, false, 0, 0, 0, nowMs: 1000);
        model.AddNumber(1, 0, 8f, false, 0, 0, 0, nowMs: 1300);
        CombatTextEntry entry = Assert.Single(model.Entries);
        Assert.Equal(20f, entry.Amount);
        Assert.Equal(2, entry.MergeCount);
        Assert.Equal(1300, entry.LastMergeMs);
    }

    [Fact]
    public void MergeWindowMeasuresFromLastMergeSoStreamsKeepAccumulating()
    {
        CombatTextModel model = Model();
        model.AddNumber(1, 0, 5f, false, 0, 0, 0, 1000);
        model.AddNumber(1, 0, 5f, false, 0, 0, 0, 1400);
        model.AddNumber(1, 0, 5f, false, 0, 0, 0, 1800);
        Assert.Single(model.Entries);
        Assert.Equal(15f, model.Entries[0].Amount);
    }

    [Fact]
    public void HitsOutsideTheWindowStartANewNumber()
    {
        CombatTextModel model = Model();
        model.AddNumber(1, 0, 5f, false, 0, 0, 0, 1000);
        model.AddNumber(1, 0, 5f, false, 0, 0, 0, 1600);
        Assert.Equal(2, model.Entries.Count);
    }

    [Fact]
    public void DifferentDamageTypesNeverMerge()
    {
        CombatTextModel model = Model();
        model.AddNumber(1, 1, 5f, false, 0, 0, 0, 1000);
        model.AddNumber(1, 4, 5f, false, 0, 0, 0, 1100);
        Assert.Equal(2, model.Entries.Count);
    }

    [Fact]
    public void MergingDisabledSpawnsIndividualNumbers()
    {
        CombatTextModel model = Model(merge: false);
        model.AddNumber(1, 0, 5f, false, 0, 0, 0, 1000);
        model.AddNumber(1, 0, 5f, false, 0, 0, 0, 1100);
        Assert.Equal(2, model.Entries.Count);
    }

    [Fact]
    public void CritMarksTheMergedEntry()
    {
        CombatTextModel model = Model();
        model.AddNumber(1, 0, 5f, false, 0, 0, 0, 1000);
        model.AddNumber(1, 0, 30f, true, 0, 0, 0, 1200);
        Assert.True(model.Entries[0].Crit);
        Assert.Equal(10, model.Entries[0].Priority);
    }

    [Fact]
    public void OneWordSlotPerTargetHigherPriorityWins()
    {
        CombatTextModel model = Model();
        model.AddWord(1, "STAGGERED", 100, 0, 0, 0, 1000);
        model.AddWord(1, "BREAK", 110, 0, 0, 0, 1100);
        CombatTextEntry word = Assert.Single(model.Entries.Where(e => e.Kind == CombatTextKind.Word));
        Assert.Equal("BREAK", word.Word);
    }

    [Fact]
    public void LowerPriorityWordDoesNotReplaceLiveHigherOne()
    {
        CombatTextModel model = Model();
        model.AddWord(1, "BREAK", 110, 0, 0, 0, 1000);
        model.AddWord(1, "MARKED", 100, 0, 0, 0, 1100);
        Assert.Equal("BREAK", model.Entries.Single(e => e.Kind == CombatTextKind.Word).Word);
    }

    [Fact]
    public void ScreenCapEvictsLowestPriorityOldestFirst()
    {
        CombatTextModel model = Model(maxEntries: 3);
        model.AddNumber(1, 0, 5f, false, 0, 0, 0, 1000);   // plain, oldest
        model.AddNumber(2, 0, 5f, true, 0, 0, 0, 1100);    // crit
        model.AddWord(3, "BREAK", 110, 0, 0, 0, 1200);     // word
        model.AddNumber(4, 0, 5f, false, 0, 0, 0, 1300);   // forces eviction

        Assert.Equal(3, model.Entries.Count);
        Assert.DoesNotContain(model.Entries, e => e.TargetEntityId == 1);
        Assert.Contains(model.Entries, e => e.Kind == CombatTextKind.Word);
    }

    [Fact]
    public void TickRetiresExpiredEntries()
    {
        CombatTextModel model = Model();
        model.AddNumber(1, 0, 5f, false, 0, 0, 0, 1000);
        model.Tick(nowMs: 3000);
        Assert.Empty(model.Entries);
    }

    [Fact]
    public void MergingRefreshesExpiry()
    {
        CombatTextModel model = Model();
        model.AddNumber(1, 0, 5f, false, 0, 0, 0, 1000);
        model.AddNumber(1, 0, 5f, false, 0, 0, 0, 1400);
        model.Tick(nowMs: 2200);
        Assert.Single(model.Entries);
    }
}
