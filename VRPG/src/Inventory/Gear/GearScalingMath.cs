using System;
using System.Collections.Generic;

namespace VRPG.Inventory.Gear;

/// <summary>Pure numeric formulas for permanent gear and combat defenses.</summary>
public static class GearScalingMath
{
    public static double WeaponLevelBase(int requiredLevel, double levelOne = 10.0) =>
        PositiveFinite(levelOne) * Math.Pow(1.035, Math.Clamp(requiredLevel, 1, 100) - 1);

    public static double ResolveWeaponPower(
        double levelBasePower,
        double flatWeaponPower = 0.0,
        double additionalWeaponPowerPercent = 0.0,
        double moreWeaponPowerPercent = 0.0,
        double rarityScalar = 1.0) =>
        ResolveWeaponPower(
            levelBasePower,
            flatWeaponPower,
            additionalWeaponPowerPercent,
            new[] { moreWeaponPowerPercent },
            rarityScalar);

    public static double ResolveWeaponPower(
        double levelBasePower,
        double flatWeaponPower,
        double additionalWeaponPowerPercent,
        IReadOnlyList<double> moreWeaponPowerPercents,
        double rarityScalar = 1.0)
    {
        ArgumentNullException.ThrowIfNull(moreWeaponPowerPercents);
        double basePower = NonNegativeFinite(levelBasePower);
        double flatPower = Finite(flatWeaponPower);
        double additionalPercent = Finite(additionalWeaponPowerPercent);
        double rarity = PositiveFinite(rarityScalar);
        double additiveBase = Math.Max(0.0, basePower + flatPower);
        double additionalMultiplier = Math.Max(0.0, 1.0 + additionalPercent / 100.0);
        double moreMultiplier = 1.0;
        for (int index = 0; index < moreWeaponPowerPercents.Count; index++)
        {
            double morePercent = Finite(moreWeaponPowerPercents[index]);
            double moreFactor = Math.Max(0.0, 1.0 + morePercent / 100.0);
            moreMultiplier = Finite(moreMultiplier * moreFactor);
        }

        return Finite(additiveBase * additionalMultiplier * moreMultiplier * rarity);
    }

    public static double ArmorLevelBase(int requiredLevel, double levelOneRating) =>
        NonNegativeFinite(levelOneRating) * Math.Pow(1.08, Math.Clamp(requiredLevel, 1, 100) - 1);

    public static double AttackPressure(int attackerLevel, double levelOnePressure = 150.0) =>
        PositiveFinite(levelOnePressure) * Math.Pow(1.08, Math.Clamp(attackerLevel, 1, 100) - 1);

    public static double ArmorMitigation(double armor, double pressure)
    {
        double rating = NonNegativeFinite(armor);
        double attackPressure = PositiveFinite(pressure);
        return Math.Min(0.99, rating / (rating + attackPressure));
    }

    public static double ClampElementalResistance(double value) =>
        Math.Clamp(Finite(value), 0.0, 0.75);

    public static double EffectiveElementalResistance(
        double resistance,
        double penetration,
        double exposure) =>
        ClampElementalResistance(
            Finite(resistance)
            - Math.Max(0.0, Finite(penetration))
            - Math.Max(0.0, Finite(exposure)));

    public static double EffectiveEvasion(double evasion, double accuracy) =>
        Math.Clamp(Finite(evasion) - Math.Max(0.0, Finite(accuracy)), 0.0, 0.90);

    private static double Finite(double value)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "Gear scaling inputs must be finite.");
        }

        return value;
    }

    private static double NonNegativeFinite(double value) => Math.Max(0.0, Finite(value));

    private static double PositiveFinite(double value) => Math.Max(0.01, Finite(value));
}
