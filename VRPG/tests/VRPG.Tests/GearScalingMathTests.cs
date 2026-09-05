using System;
using VRPG.Inventory.Gear;
using Xunit;

namespace VRPG.Tests;

public sealed class GearScalingMathTests
{
    [Theory]
    [InlineData(1, 10.0)]
    [InlineData(20, 19.23)]
    [InlineData(40, 38.25)]
    [InlineData(60, 76.12)]
    [InlineData(80, 151.46)]
    [InlineData(100, 301.37)]
    public void WeaponBaseUsesPermanentRequiredLevel(int level, double expected)
    {
        Assert.Equal(expected, GearScalingMath.WeaponLevelBase(level), 2);
    }

    [Fact]
    public void WeaponLevelIsClampedToTheSupportedRange()
    {
        Assert.Equal(GearScalingMath.WeaponLevelBase(1), GearScalingMath.WeaponLevelBase(0), 12);
        Assert.Equal(GearScalingMath.WeaponLevelBase(100), GearScalingMath.WeaponLevelBase(101), 12);
        Assert.Equal(20.0, GearScalingMath.WeaponLevelBase(1, 20.0), 12);
    }

    [Fact]
    public void WeaponPowerResolvesBaseFlatAdditionalMoreAndRarityInOrder()
    {
        double result = GearScalingMath.ResolveWeaponPower(
            levelBasePower: 100,
            flatWeaponPower: 20,
            additionalWeaponPowerPercent: 50,
            moreWeaponPowerPercent: 25,
            rarityScalar: 1.15);

        Assert.Equal(258.75, result, 3);
    }

    [Fact]
    public void WeaponPowerCannotResolveToANegativeValue()
    {
        Assert.Equal(0.0, GearScalingMath.ResolveWeaponPower(-10, -20), 12);
        Assert.Equal(0.0, GearScalingMath.ResolveWeaponPower(100, 0, -100), 12);
        Assert.Equal(0.0, GearScalingMath.ResolveWeaponPower(100, 0, 0, -100), 12);
    }

    [Fact]
    public void ArmorAndPressureUseTheirLevelOneRatings()
    {
        Assert.Equal(100.0, GearScalingMath.ArmorLevelBase(1, 100.0), 12);
        Assert.Equal(GearScalingMath.ArmorLevelBase(1, 100.0), GearScalingMath.ArmorLevelBase(0, 100.0), 12);
        Assert.Equal(150.0, GearScalingMath.AttackPressure(1), 12);
        Assert.Equal(GearScalingMath.AttackPressure(1), GearScalingMath.AttackPressure(0), 12);
        Assert.Equal(100.0 * Math.Pow(1.08, 99), GearScalingMath.ArmorLevelBase(100, 100.0), 8);
    }

    [Fact]
    public void MatchedEightPercentGrowthStaysAtFortyPercentMitigation()
    {
        double armor = GearScalingMath.ArmorLevelBase(20, 100.0);
        double pressure = GearScalingMath.AttackPressure(20, 150.0);

        Assert.Equal(0.40, GearScalingMath.ArmorMitigation(armor, pressure), 3);
    }

    [Theory]
    [InlineData(9, 0.90)]
    [InlineData(19, 0.95)]
    [InlineData(32.333333, 0.97)]
    [InlineData(99, 0.99)]
    [InlineData(999, 0.99)]
    public void ArmorHasNaturalDiminishingReturnsAndHardCap(double pressureMultiple, double expected)
    {
        Assert.Equal(expected, GearScalingMath.ArmorMitigation(pressureMultiple * 150, 150), 2);
    }

    [Fact]
    public void ResistanceAndEffectiveEvasionUseIndependentHardCaps()
    {
        Assert.Equal(0.75, GearScalingMath.ClampElementalResistance(2.0), 3);
        Assert.Equal(0.50, GearScalingMath.EffectiveElementalResistance(0.75, 0.15, 0.10), 3);
        Assert.Equal(0.70, GearScalingMath.EffectiveEvasion(0.90, 0.20), 3);
        Assert.Equal(0.90, GearScalingMath.EffectiveEvasion(2.0, 0.0), 3);
        Assert.Equal(0.0, GearScalingMath.EffectiveElementalResistance(0.10, 0.20, 0.20), 3);
        Assert.Equal(0.0, GearScalingMath.EffectiveEvasion(0.10, 0.20), 3);
    }

    [Fact]
    public void Level100AuthoredBudgetCanCapAllFourElementalResistances()
    {
        double[][] armorPieceResistances =
        {
            new[] { 0.15, 0.15, 0.15, 0.15 },
            new[] { 0.15, 0.15, 0.15, 0.15 },
            new[] { 0.15, 0.15, 0.15, 0.15 },
            new[] { 0.15, 0.15, 0.15, 0.15 },
            new[] { 0.15, 0.15, 0.15, 0.15 }
        };

        double primaryDefense = GearScalingMath.AttackPressure(100) * 99.0;

        for (int element = 0; element < 4; element++)
        {
            double totalResistance = 0.0;
            foreach (double[] piece in armorPieceResistances)
            {
                totalResistance += piece[element];
            }

            Assert.Equal(0.75, GearScalingMath.ClampElementalResistance(totalResistance), 3);
        }

        Assert.Equal(0.99, GearScalingMath.ArmorMitigation(primaryDefense, GearScalingMath.AttackPressure(100)), 3);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void NonFiniteInputsAreRejected(double value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => GearScalingMath.WeaponLevelBase(1, value));
        Assert.Throws<ArgumentOutOfRangeException>(() => GearScalingMath.ResolveWeaponPower(value));
        Assert.Throws<ArgumentOutOfRangeException>(() => GearScalingMath.ResolveWeaponPower(1, value));
        Assert.Throws<ArgumentOutOfRangeException>(() => GearScalingMath.ResolveWeaponPower(1, 0, value));
        Assert.Throws<ArgumentOutOfRangeException>(() => GearScalingMath.ResolveWeaponPower(1, 0, 0, value));
        Assert.Throws<ArgumentOutOfRangeException>(() => GearScalingMath.ResolveWeaponPower(1, 0, 0, 0, value));
        Assert.Throws<ArgumentOutOfRangeException>(() => GearScalingMath.ArmorLevelBase(1, value));
        Assert.Throws<ArgumentOutOfRangeException>(() => GearScalingMath.AttackPressure(1, value));
        Assert.Throws<ArgumentOutOfRangeException>(() => GearScalingMath.ArmorMitigation(value, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => GearScalingMath.ArmorMitigation(1, value));
        Assert.Throws<ArgumentOutOfRangeException>(() => GearScalingMath.ClampElementalResistance(value));
        Assert.Throws<ArgumentOutOfRangeException>(() => GearScalingMath.EffectiveElementalResistance(value, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => GearScalingMath.EffectiveElementalResistance(0, value, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => GearScalingMath.EffectiveElementalResistance(0, 0, value));
        Assert.Throws<ArgumentOutOfRangeException>(() => GearScalingMath.EffectiveEvasion(value, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => GearScalingMath.EffectiveEvasion(0, value));
    }
}
