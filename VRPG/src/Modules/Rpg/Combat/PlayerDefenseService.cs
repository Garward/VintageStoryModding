using System;
using VRPG.Inventory.Gear;
using VRPG.Modules.Rpg.Players;
using VRPG.Modules.Rpg.Stats;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;

namespace VRPG.Modules.Rpg.Combat;

/// <summary>Server-owned context for this exact hit; resistance values match its resolved element.</summary>
public sealed record PlayerDefenseContext(
    double Accuracy = 0, double ResistancePenetration = 0, double ResistanceExposure = 0,
    double OtherReductionMultiplier = 1, bool Unavoidable = false, bool PersistentHazard = false,
    bool DamageOverTime = false, bool IgnoreInvulnerability = false, DamageElement? Element = null);

/// <summary>Adapts engine hits to pure mitigation, then commits shield and escape effects in order.</summary>
public sealed class PlayerDefenseService
{
    private readonly RpgPlayerStore players;
    private readonly RpgResourceService resources;
    private readonly EvasiveStepService evasiveStep;
    private readonly System.Func<EntityPlayer, EquippedGearStats> equipped;
    private readonly System.Func<EntityPlayer, DamageSource, PlayerDefenseContext> context;
    private readonly System.Func<double> random;

    public PlayerDefenseService(RpgPlayerStore players, RpgResourceService resources, EvasiveStepService evasiveStep,
        System.Func<EntityPlayer, EquippedGearStats> equipped,
        System.Func<EntityPlayer, DamageSource, PlayerDefenseContext> context, System.Func<double> random)
    {
        this.players = players;
        this.resources = resources;
        this.evasiveStep = evasiveStep;
        this.equipped = equipped;
        this.context = context;
        this.random = random;
    }

    public float ResolveDamage(Entity target, DamageSource source, float incomingDamage)
    {
        if (target is not EntityPlayer entity || entity.Player is not IServerPlayer player
            || !entity.Alive || source.Type == EnumDamageType.Heal || incomingDamage <= 0)
            return incomingDamage;

        PlayerDefenseContext hit = context(entity, source);
        if (!source.IgnoreInvFrames && !hit.IgnoreInvulnerability && evasiveStep.IsInvulnerable(entity)) return 0;

        EquippedGearStats gear = equipped(entity);
        if (!gear.Valid) gear = EquippedGearStats.Empty;
        DamageElement element = hit.Element ?? ElementFor(source.Type);
        bool evasionEligible = !hit.Unavoidable && !hit.PersistentHazard && !hit.DamageOverTime
            && source.Duration <= TimeSpan.Zero && source.DamageOverTimeType == 0
            && source.Source is EnumDamageSource.Entity or EnumDamageSource.Player
            && source.GetCauseEntity() is Entity cause && cause != entity
            && source.Type is EnumDamageType.BluntAttack or EnumDamageType.PiercingAttack or EnumDamageType.SlashingAttack
                or EnumDamageType.Fire or EnumDamageType.Frost or EnumDamageType.Electricity or EnumDamageType.Acid;
        double resistance = element switch
        {
            DamageElement.Fire => gear.FireResistance,
            DamageElement.Cold => gear.ColdResistance,
            DamageElement.Lightning => gear.LightningResistance,
            DamageElement.Rust => gear.RustResistance,
            _ => 0
        };
        int attackerLevel = source.GetCauseEntity()?.WatchedAttributes.GetInt("vrpgLevel", 1) ?? 1;
        var result = PlayerDefenseMath.Resolve(new(incomingDamage, element, evasionEligible,
            evasionEligible ? random() : 1, gear.Evasion, hit.Accuracy, gear.Armor,
            GearScalingMath.AttackPressure(attackerLevel), resistance, hit.ResistancePenetration,
            hit.ResistanceExposure, hit.OtherReductionMultiplier, players.GetOrCreate(player).MagicShield,
            entity.WatchedAttributes.GetTreeAttribute("health")?.GetFloat("currenthealth", 0) ?? 0));

        if (result.Evaded) return 0;
        if (result.ShieldAbsorbed > 0) resources.AbsorbMagicShield(player, result.ShieldAbsorbed);
        if (result.WouldBeLethal && evasiveStep.TryActivate(entity, source, true)) return 0;
        return result.HealthDamage;
    }

    private static DamageElement ElementFor(EnumDamageType type) => type switch
    {
        EnumDamageType.Fire => DamageElement.Fire,
        EnumDamageType.Frost => DamageElement.Cold,
        EnumDamageType.Electricity => DamageElement.Lightning,
        EnumDamageType.Acid => DamageElement.Rust,
        _ => DamageElement.Physical
    };
}
