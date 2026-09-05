using System;
using VRPG.Modules.Rpg.Combat;
using VRPG.Modules.Rpg.Players;
using VRPG.Modules.Rpg.Stats;
using VRPG.Modules.Rpg.Talents;
using VRPG.Patches;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Xunit;

namespace VRPG.Tests;

[Collection("EntityDamageScalingPatch")]
public sealed class PlayerWeaponDamageServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EligibleAttacksUseMainWeaponWithAttackModifiers(bool projectile)
    {
        var player = new EntityPlayer();
        var state = new RpgPlayerState();
        state.BaseStats["strength"] = 100;
        var gear = new EquippedGearStatResolver().Resolve("p", 1,
            EquippedGearStatResolverTests.Snapshot(1, EquippedGearStatResolverTests.Gear(25)));
        var service = Service(gear, state);
        var source = Source(player, projectile);
        Assert.Equal(27.5f, service.Resolve(new EntityAgent(), source, 3), 4);
        Assert.Equal(10, Service(EquippedGearStats.Empty).Resolve(new EntityAgent(), source, 99));
        Assert.Equal(0, Service(EquippedGearStats.Invalid).Resolve(new EntityAgent(), source, 99));
    }

    [Fact]
    public void TagsPreserveOnlyTheExactSkillSourceAndUnrelatedDamageStaysNative()
    {
        var player = new EntityPlayer();
        var source = Source(player);
        var target = new EntityAgent();
        var service = Service(EquippedGearStats.Empty);
        RpgDamageSourceTagger.MarkSkill(source);
        RpgDamageSourceTagger.MarkSkill(source);
        Assert.True(RpgDamageSourceTagger.IsSkill(source));
        Assert.False(RpgDamageSourceTagger.IsSkill(Source(player)));
        Assert.Equal(123, service.Resolve(target, source, 123));
        Assert.Equal(123, Service(EquippedGearStats.Empty, eligible: false).Resolve(target, Source(player), 123));
        Assert.Equal(123, Service(EquippedGearStats.Empty, active: false).Resolve(target, Source(player), 123));
        Assert.Equal(123, service.Resolve(target, Source(new EntityAgent()), 123));
        var dot = Source(player);
        dot.Duration = TimeSpan.FromSeconds(3);
        Assert.Equal(123, service.Resolve(target, dot, 123));
    }

    [Fact]
    public void BasicAttacksApplyAttackCategoriesButExcludeSkillOnlyModifiers()
    {
        var instance = EquippedGearStatResolverTests.Gear(20) with { Affixes = new[]
        {
            new VRPG.Inventory.Gear.GearAffixRoll("vrpg:attack", 1, new[]
            {
                new VRPG.Inventory.Gear.GearModifierRoll("vrpg:attack_damage", "more", 50),
                new VRPG.Inventory.Gear.GearModifierRoll("vrpg:skill_damage", "more", 1000)
            })
        } };
        var gear = new EquippedGearStatResolver().Resolve("p", 1, EquippedGearStatResolverTests.Snapshot(1, instance));
        Assert.Equal(30, Service(gear).Resolve(new EntityAgent(), Source(new EntityPlayer()), 2));
    }

    [Theory]
    [InlineData(false, true, 10f)]
    [InlineData(true, true, 10f)]
    [InlineData(false, false, 40f)]
    public void PatchSkipsCreatureScalingOnlyForResolvedPlayerHits(bool tagged, bool eligible, float expected)
    {
        var player = new EntityPlayer();
        player.WatchedAttributes.SetFloat("vrpgDamageMultiplier", 4);
        var source = Source(player);
        if (tagged) RpgDamageSourceTagger.MarkSkill(source);
        var service = Service(EquippedGearStats.Empty, eligible: eligible);
        EntityDamageScalingPatch.PlayerOutgoingDamageResolver = service.ResolveOutgoing;
        try
        {
            float damage = 10; // Deliberately equal to replacement: equality must not select the legacy path.
            EntityDamageScalingPatch.OnEntityReceiveDamagePrefix(new HealthBehavior(new EntityAgent()), source, ref damage);
            Assert.Equal(expected, damage);
        }
        finally { EntityDamageScalingPatch.PlayerOutgoingDamageResolver = null; }
    }

    private static PlayerWeaponDamageService Service(EquippedGearStats gear, RpgPlayerState? state = null,
        bool eligible = true, bool active = true) => new(_ => active, _ => eligible, _ => state ?? new RpgPlayerState(),
            _ => gear, new SkillDamageResolver(new TalentTreeCatalog()));

    private static DamageSource Source(Entity player, bool projectile = false) => new()
    {
        Source = projectile ? EnumDamageSource.Entity : EnumDamageSource.Player,
        SourceEntity = projectile ? new EntityAgent() : player,
        CauseEntity = player,
        Type = EnumDamageType.PiercingAttack
    };

    private sealed class HealthBehavior(Entity entity) : EntityBehavior(entity)
    {
        public override string PropertyName() => "health";
    }
}
