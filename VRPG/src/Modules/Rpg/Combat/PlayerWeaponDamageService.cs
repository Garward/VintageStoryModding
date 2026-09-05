using VRPG.Modules.Rpg.Players;
using VRPG.Modules.Rpg.Stats;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace VRPG.Modules.Rpg.Combat;

/// <summary>Replaces direct RPG player basic attacks; eligibility is owned by server composition.</summary>
public sealed class PlayerWeaponDamageService
{
    private readonly System.Func<EntityPlayer, bool> isActive;
    private readonly System.Func<Entity, bool> isEligible;
    private readonly System.Func<EntityPlayer, RpgPlayerState> playerState;
    private readonly System.Func<EntityPlayer, EquippedGearStats> equippedStats;
    private readonly SkillDamageResolver damageResolver;

    public PlayerWeaponDamageService(System.Func<EntityPlayer, bool> isActive,
        System.Func<Entity, bool> isEligible, System.Func<EntityPlayer, RpgPlayerState> playerState,
        System.Func<EntityPlayer, EquippedGearStats> equippedStats, SkillDamageResolver damageResolver)
    {
        this.isActive = isActive;
        this.isEligible = isEligible;
        this.playerState = playerState;
        this.equippedStats = equippedStats;
        this.damageResolver = damageResolver;
    }

    public float Resolve(Entity target, DamageSource source, float engineDamage) =>
        ResolveOutgoing(target, source, engineDamage) ?? engineDamage;

    /// <summary>Null means the existing engine/scaling pipeline owns this hit, even if values happen to match.</summary>
    public float? ResolveOutgoing(Entity target, DamageSource source, float engineDamage)
    {
        if (source == null || source.GetCauseEntity() is not EntityPlayer player
            || !isActive(player) || !isEligible(target)) return null;
        if (RpgDamageSourceTagger.IsSkill(source)) return engineDamage;
        if (engineDamage <= 0 || source.Duration > System.TimeSpan.Zero || source.SourceEntity == null
            || source.Source is not (EnumDamageSource.Player or EnumDamageSource.Entity)
            || source.Type is not (EnumDamageType.BluntAttack or EnumDamageType.PiercingAttack or EnumDamageType.SlashingAttack)) return null;

        return damageResolver.ResolveAttack(playerState(player), source.SourceEntity != player, equippedStats(player));
    }
}
