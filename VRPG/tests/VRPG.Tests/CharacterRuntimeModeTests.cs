using VRPG.Core;
using VRPG.Patches;
using Vintagestory.API.Common;
using Xunit;

namespace VRPG.Tests;

public sealed class CharacterRuntimeModeTests
{
    [Theory]
    [InlineData(EnumGameMode.Survival, true)]
    [InlineData(EnumGameMode.Creative, false)]
    [InlineData(EnumGameMode.Spectator, false)]
    [InlineData(EnumGameMode.Guest, false)]
    public void AuthorityTemporarilyFollowsSurvivalMode(EnumGameMode gameMode, bool expected)
    {
        Assert.Equal(expected, CharacterRuntimeMode.IsOwned(true, true, gameMode));
    }

    [Fact]
    public void AuthorityCanBeDisabledWithoutChangingGameMode()
    {
        Assert.False(CharacterRuntimeMode.IsOwned(true, false, EnumGameMode.Survival));
        Assert.False(CharacterRuntimeMode.IsOwned(false, true, EnumGameMode.Survival));
    }

    [Theory]
    [InlineData(EnumDamageType.Hunger, true)]
    [InlineData(EnumDamageType.Frost, true)]
    [InlineData(EnumDamageType.BluntAttack, false)]
    [InlineData(EnumDamageType.Fire, false)]
    [InlineData(EnumDamageType.Heal, false)]
    public void OnlyInheritedSurvivalDamageIsSuppressed(EnumDamageType damageType, bool expected)
    {
        Assert.Equal(expected, SurvivalCharacterRuntimePatch.ShouldSuppressDamage(new DamageSource
        { Type = damageType, Source = EnumDamageSource.Internal }));
    }

    [Theory]
    [InlineData(CharacterMovementAuthority.WalkSpeedStat, 1.25f)]
    [InlineData(CharacterMovementAuthority.JumpHeightStat, 1.75f)]
    public void MovementStatsComeFromVrpgAuthority(string stat, float expected)
    {
        Assert.True(CharacterMovementAuthority.TryResolve(
            true,
            true,
            EnumGameMode.Survival,
            stat,
            1.25f,
            1.75f,
            out float value));
        Assert.Equal(expected, value);
    }

    [Fact]
    public void UnownedProfilesAndUnrelatedStatsPassThrough()
    {
        Assert.False(CharacterMovementAuthority.TryResolve(
            true, true, EnumGameMode.Creative, CharacterMovementAuthority.WalkSpeedStat, 2f, 2f, out _));
        Assert.False(CharacterMovementAuthority.TryResolve(
            true, true, EnumGameMode.Survival, "meleeWeaponsDamage", 2f, 2f, out _));
    }

    [Fact]
    public void AppearanceOnlyCreatorSuppressesVanillaClassStep()
    {
        VanillaClassRuntimePatch.ClientAppearanceOnlyResolver = () => true;
        bool result = true;

        try
        {
            Assert.False(VanillaClassRuntimePatch.AllowClassSelectionPrefix(ref result));
            Assert.False(result);
        }
        finally
        {
            VanillaClassRuntimePatch.ClientAppearanceOnlyResolver = null;
        }
    }
}
