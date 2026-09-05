using VRPG.Client;
using Xunit;

namespace VRPG.Tests;

public sealed class CombatWeaponSelectionTests
{
    [Fact]
    public void WheelCyclesOnlyProvidedWeaponSlotsAndWraps()
    {
        int[] weapons = { 1, 4, 8 };

        Assert.Equal(4, CombatWeaponSelection.Cycle(weapons, 1, -1));
        Assert.Equal(8, CombatWeaponSelection.Cycle(weapons, 4, -1));
        Assert.Equal(1, CombatWeaponSelection.Cycle(weapons, 8, -1));
        Assert.Equal(8, CombatWeaponSelection.Cycle(weapons, 1, 1));
    }

    [Fact]
    public void WheelSelectsAWeaponWhenTheHandWasNeutral()
    {
        int[] weapons = { 2, 7 };

        Assert.Equal(2, CombatWeaponSelection.Cycle(weapons, -1, -1));
        Assert.Equal(7, CombatWeaponSelection.Cycle(weapons, -1, 1));
    }

    [Fact]
    public void EmptyWeaponSetKeepsTheCurrentSelection()
    {
        Assert.Equal(10, CombatWeaponSelection.Cycle(System.Array.Empty<int>(), 10, -1));
    }
}
