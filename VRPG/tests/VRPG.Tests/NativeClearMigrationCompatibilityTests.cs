using VRPG.Inventory.Migration;
using Xunit;

namespace VRPG.Tests;

[Collection("EngineItemBirth")]
public sealed class NativeClearMigrationCompatibilityTests
{
    [Fact]
    public void HealthyJournaledSourceClearAndRestoreRemainAvailableUnderRecoveryCustody()
    {
        using var scope = new NativeMutationScope("recovery");
        var fixture = new NativeLegacyInventoryFixture();
        var inventories = fixture.Inventories;
        var source = fixture.Source;
        var inspection = source.Inspect();
        Assert.Empty(inspection.BlockingReasons);
        Assert.Equal(2, inspection.Slots.Length);
        Assert.True(source.Clear(inspection.Slots).Success);
        Assert.Empty(source.Inspect().Slots);
        Assert.True(source.Restore(inspection.Slots).Success);
        Assert.Equal(LegacyInventoryState.MatchesSnapshot, source.CompareTo(inspection.Slots));
        Assert.Equal(7, inventories["hotbar"][0].StackSize);
        Assert.Equal(3, inventories["mouse"][0].StackSize);
        inventories["hotbar"].Clear();
        Assert.Equal(7, inventories["hotbar"][0].StackSize);
    }
}
