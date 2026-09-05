using VRPG.Client;
using Vintagestory.API.Common;
using Xunit;

namespace VRPG.Tests;

public sealed class CombatActionBarModeTests
{
    [Theory]
    [InlineData(EnumGameMode.Survival, true)]
    [InlineData(EnumGameMode.Creative, false)]
    [InlineData(EnumGameMode.Spectator, false)]
    [InlineData(EnumGameMode.Guest, false)]
    public void ReplacementInitiallyFollowsSurvivalMode(EnumGameMode mode, bool expected)
    {
        Assert.Equal(expected, CombatActionBarMode.IsActive(true, mode));
    }

    [Fact]
    public void ReplacementCanBeDisabledWithoutChangingVanillaGameMode()
    {
        Assert.False(CombatActionBarMode.IsActive(false, EnumGameMode.Survival));
    }
}
