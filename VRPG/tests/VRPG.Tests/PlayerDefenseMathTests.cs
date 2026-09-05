using System;
using System.Linq;
using VRPG.Modules.Rpg.Combat;
using Xunit;

namespace VRPG.Tests;

public sealed class PlayerDefenseMathTests
{
    [Fact]
    public void PhysicalDamageUsesEvasionThenArmorThenShieldThenHealth()
    {
        var result = PlayerDefenseMath.Resolve(Input(true, .9, .95) with
        { IncomingDamage = 100, Armor = 1350, MagicShield = 4, CurrentHealth = 10 });
        Assert.False(result.Evaded);
        Assert.Equal(.9, result.Mitigation, 3);
        Assert.Equal(4, result.ShieldAbsorbed, 3);
        Assert.Equal(6, result.HealthDamage, 3);
        Assert.False(result.WouldBeLethal);
    }

    [Theory]
    [InlineData(false, .9, .01, 0, false, 10)]
    [InlineData(true, 9, .91, 0, false, 10)]
    [InlineData(true, .9, .89, 0, true, 0)]
    [InlineData(true, .9, .51, .4, false, 10)]
    [InlineData(true, .9, .49, .4, true, 0)]
    public void EvasionHonorsEligibilityCapAndAccuracy(bool eligible, double evasion, double roll,
        double accuracy, bool evaded, float healthDamage)
    {
        var result = PlayerDefenseMath.Resolve(Input(eligible, evasion, roll) with { Accuracy = accuracy });
        Assert.Equal(evaded, result.Evaded);
        Assert.Equal(healthDamage, result.HealthDamage);
        Assert.InRange(result.EffectiveEvasion, 0, .9);
    }

    [Fact]
    public void ArmorHardCapsAtNinetyNinePercent()
    {
        var result = PlayerDefenseMath.Resolve(Input(false, 0, 1) with { IncomingDamage = 100, Armor = 1500000 });
        Assert.Equal(.99, result.Mitigation);
        Assert.Equal(1, result.HealthDamage);
    }

    [Theory]
    [InlineData(DamageElement.Fire)]
    [InlineData(DamageElement.Cold)]
    [InlineData(DamageElement.Lightning)]
    [InlineData(DamageElement.Rust)]
    public void EachElementCapsResistanceAndIgnoresArmor(DamageElement element)
    {
        var input = Input(false, 0, 1) with { Element = element, IncomingDamage = 100, Armor = 1500000,
            ElementalResistance = .95 };
        Assert.Equal(25, PlayerDefenseMath.Resolve(input).HealthDamage);
        Assert.Equal(45, PlayerDefenseMath.Resolve(input with
        { ResistancePenetration = .25, ResistanceExposure = .15 }).HealthDamage, 3);
        Assert.Equal(100, PlayerDefenseMath.Resolve(input with { ElementalResistance = 0 }).HealthDamage);
    }

    [Fact]
    public void OtherReductionPrecedesShieldAndOnlyResidualCanBeLethal()
    {
        var input = Input(false, 0, 1) with { IncomingDamage = 100, OtherReductionMultiplier = .5,
            MagicShield = 40, CurrentHealth = 10 };
        var result = PlayerDefenseMath.Resolve(input);
        Assert.Equal(40, result.ShieldAbsorbed);
        Assert.Equal(10, result.HealthDamage);
        Assert.True(result.WouldBeLethal);
        result = PlayerDefenseMath.Resolve(input with { MagicShield = 60 });
        Assert.Equal(50, result.ShieldAbsorbed);
        Assert.Equal(0, result.HealthDamage);
        Assert.False(result.WouldBeLethal);
    }

    [Fact]
    public void EncounterCompositionSplitsAggregateSourceBudgetsBeforeMitigation()
    {
        // Individual enemies need not deal both kinds: these four sources form one encounter.
        var budgets = new[] { (DamageElement.Physical, 60f), (DamageElement.Physical, 40f),
            (DamageElement.Fire, 75f), (DamageElement.Cold, 25f) };
        Assert.Equal(100, budgets.Where(x => x.Item1 == DamageElement.Physical).Sum(x => x.Item2));
        Assert.Equal(100, budgets.Where(x => x.Item1 != DamageElement.Physical).Sum(x => x.Item2));
        float health = budgets.Sum(x => PlayerDefenseMath.Resolve(Input(false, 0, 1) with
        { Element = x.Item1, IncomingDamage = x.Item2, Armor = 1350, ElementalResistance = .75 }).HealthDamage);
        Assert.Equal(35, health, 3);
    }

    [Fact]
    public void InvalidDamageCannotCreateShieldOrHealthDamage()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PlayerDefenseMath.Resolve(Input(false, 0, 1) with
        { OtherReductionMultiplier = double.NaN }));
        Assert.Throws<ArgumentOutOfRangeException>(() => PlayerDefenseMath.Resolve(Input(false, 0, 1) with
        { IncomingDamage = float.NaN }));
        var result = PlayerDefenseMath.Resolve(Input(false, 0, 1) with { IncomingDamage = -5, MagicShield = -10 });
        Assert.Equal(0, result.ShieldAbsorbed);
        Assert.Equal(0, result.HealthDamage);
        Assert.False(result.WouldBeLethal);
    }

    private static DefenseResolutionInput Input(bool evasionEligible, double evasion, double evasionRoll01) =>
        new(10, DamageElement.Physical, evasionEligible, evasionRoll01, evasion, 0, 0, 150, 0, 0, 0, 1, 0, 20);
}
