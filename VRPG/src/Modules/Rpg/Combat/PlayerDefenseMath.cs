using System;
using VRPG.Inventory.Gear;

namespace VRPG.Modules.Rpg.Combat;

public enum DamageElement { Physical, Fire, Cold, Lightning, Rust }

public sealed record DefenseResolutionInput(
    float IncomingDamage, DamageElement Element, bool EvasionEligible, double EvasionRoll01,
    double Evasion, double Accuracy, double Armor, double AttackPressure, double ElementalResistance,
    double ResistancePenetration, double ResistanceExposure, double OtherReductionMultiplier,
    float MagicShield, float CurrentHealth);

public sealed record DefenseResolution(bool Evaded, double EffectiveEvasion, double Mitigation,
    float ShieldAbsorbed, float HealthDamage, bool WouldBeLethal);

/// <summary>Ordered, side-effect-free incoming mitigation in server combat units.</summary>
public static class PlayerDefenseMath
{
    public static DefenseResolution Resolve(DefenseResolutionInput input)
    {
        if (!float.IsFinite(input.IncomingDamage) || !float.IsFinite(input.MagicShield)
            || !float.IsFinite(input.CurrentHealth) || !double.IsFinite(input.OtherReductionMultiplier))
            throw new ArgumentOutOfRangeException(nameof(input));

        double evasion = input.EvasionEligible ? GearScalingMath.EffectiveEvasion(input.Evasion, input.Accuracy) : 0;
        if (input.EvasionEligible && input.EvasionRoll01 < evasion)
            return new(true, evasion, 1, 0, 0, false);

        double mitigation = input.Element == DamageElement.Physical
            ? GearScalingMath.ArmorMitigation(input.Armor, input.AttackPressure)
            : GearScalingMath.EffectiveElementalResistance(input.ElementalResistance,
                input.ResistancePenetration, input.ResistanceExposure);
        float mitigated = (float)(Math.Max(0, input.IncomingDamage) * (1 - mitigation)
            * Math.Clamp(input.OtherReductionMultiplier, 0, 1));
        float shield = Math.Min(Math.Max(0, input.MagicShield), mitigated);
        float health = Math.Max(0, mitigated - shield);
        return new(false, evasion, mitigation, shield, health,
            health > 0 && input.CurrentHealth > 0 && health + .001f >= input.CurrentHealth);
    }
}
