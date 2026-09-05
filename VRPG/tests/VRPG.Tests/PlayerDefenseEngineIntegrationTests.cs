using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using VRPG.Modules.Rpg.Combat;
using VRPG.Modules.Rpg.Stats;
using VRPG.Patches;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;
using Xunit;

namespace VRPG.Tests;

[Collection("EntityDamageScalingPatch")]
public sealed class PlayerDefenseEngineIntegrationTests
{
    [Fact]
    public void UnwiredPlayerDefenseRetainsNativeQueuedSourceScaling()
    {
        using var scope = new EngineScope();
        var f = scope.Fixture;
        EntityDamageScalingPatch.PlayerDamageResolver = null;
        f.Attacker.WatchedAttributes.SetFloat("vrpgDamageMultiplier", 2);
        f.Source.Duration = TimeSpan.FromSeconds(2);
        f.Source.TicksPerDuration = 2;
        float damage = 10;
        scope.Health.OnEntityReceiveDamage(f.Source, ref damage);
        Assert.Equal(10, Assert.Single(scope.Health.ActiveDoTEffects).Damage);
    }

    [Theory]
    [InlineData(false, true, EnumDamageType.BluntAttack, true)]
    [InlineData(true, false, EnumDamageType.BluntAttack, true)]
    [InlineData(true, true, EnumDamageType.Heal, true)]
    [InlineData(true, true, EnumDamageType.BluntAttack, false)]
    public void WearableAuthorityOnlyReplacesOwnedDamageWithAnActiveResolver(bool owned, bool active,
        EnumDamageType type, bool nativeRuns)
    {
        using var scope = new EngineScope();
        SurvivalCharacterRuntimePatch.OwnershipResolver = _ => owned;
        if (!active) EntityDamageScalingPatch.PlayerDamageResolver = null;
        float result = -1;
        Assert.Equal(nativeRuns, EntityDamageScalingPatch.WearableDamagePrefix(scope.Fixture.Player, 20,
            new DamageSource { Type = type }, ref result));
        Assert.Equal(nativeRuns ? -1 : 20, result);
    }

    [Fact]
    public void QueuedPlayerDotKeepsItsTickBudgetInsteadOfBecomingABasicWeaponAttack()
    {
        using var scope = new EngineScope();
        var f = scope.Fixture;
        f.Dexterity = false;
        f.Source.SourceEntity = new EntityPlayer();
        f.Source.Source = EnumDamageSource.Player;
        f.Source.Duration = TimeSpan.FromSeconds(2);
        f.Source.TicksPerDuration = 2;
        EntityDamageScalingPatch.PlayerOutgoingDamageResolver = (_, _, _) => 999;
        try
        {
            float damage = 20;
            scope.Health.OnEntityReceiveDamage(f.Source, ref damage);
            f.Now = 1000;
            scope.Health.TickDamage();
            Assert.Equal(10, Assert.Single(scope.Entity.LandedDamage));
        }
        finally { EntityDamageScalingPatch.PlayerOutgoingDamageResolver = null; }
    }

    [Fact]
    public void QueuedBudgetDoesNotSpendShieldAndActualNativeTicksResolveOnceWithContext()
    {
        using var scope = new EngineScope();
        var f = scope.Fixture;
        f.State.MagicShield = 8;
        f.Context = new(OtherReductionMultiplier: .5);
        f.Source.Duration = TimeSpan.FromSeconds(2);
        f.Source.TicksPerDuration = 2;
        float damage = 40;
        scope.Health.OnEntityReceiveDamage(f.Source, ref damage);
        Assert.Equal(8, f.State.MagicShield);
        Assert.Empty(f.Movement);
        Assert.Equal(20, Assert.Single(scope.Health.ActiveDoTEffects).Damage);
        // Per-hit context survives the native queue, even if the current provider changes later.
        f.Context = new();
        f.Now = 1000;
        scope.Health.TickDamage();
        Assert.Equal(0, f.State.MagicShield);
        Assert.Equal(2, Assert.Single(scope.Entity.LandedDamage));
        Assert.Empty(f.Movement);
        f.Now = 2000;
        scope.Health.TickDamage();
        Assert.Equal(new[] { 2f, 0f }, scope.Entity.LandedDamage);
        Assert.Single(f.Movement); // Only the second, actually lethal tick triggers escape.
        Assert.Equal(0, f.Rolls);
    }

    [Fact]
    public void ReloadedNativeDotCannotBecomeEvadeableOrTriggerEscapeFromItsQueuedTotal()
    {
        using var scope = new EngineScope();
        var f = scope.Fixture;
        f.Gear = new EquippedGearStats(0, 150, 0, 0, .9f, 0, 0, 0, 0, Array.Empty<VRPG.Data.Definitions.StatModifierDefinition>());
        f.Roll = 0;
        f.Source.Duration = TimeSpan.FromSeconds(2);
        f.Source.TicksPerDuration = 2;
        float damage = 20;
        scope.Health.OnEntityReceiveDamage(f.Source, ref damage);
        Assert.Empty(f.Movement);
        using var bytes = new MemoryStream();
        using var writer = new BinaryWriter(bytes, System.Text.Encoding.UTF8, true);
        Assert.Single(scope.Health.ActiveDoTEffects).ToBytes(writer);
        bytes.Position = 0;
        using var reader = new BinaryReader(bytes);
        scope.Health.ActiveDoTEffects.Clear();
        scope.Health.ActiveDoTEffects.Add(DamageOverTimeEffect.FromBytes(reader));
        f.Now = 1000;
        scope.Health.TickDamage();
        Assert.Equal(5, Assert.Single(scope.Entity.LandedDamage));
        Assert.Equal(0, f.Rolls);
        Assert.Empty(f.Movement);
    }

    [Theory]
    [InlineData(EnumDamageSource.Entity, 6)]
    [InlineData(EnumDamageSource.Player, 6)]
    [InlineData(EnumDamageSource.Internal, 0)]
    [InlineData(EnumDamageSource.Weather, 0)]
    [InlineData(EnumDamageSource.Block, 6)]
    public void OwnedColdHitsSurviveTheSurvivalGateButInheritedFreezingDoesNot(EnumDamageSource source, float expected)
    {
        using var scope = new EngineScope();
        var f = scope.Fixture;
        f.Source.Type = EnumDamageType.Frost;
        f.Source.Source = source;
        f.Gear = new EquippedGearStats(0, 0, 0, 0, 0, 0, .4f, 0, 0, Array.Empty<VRPG.Data.Definitions.StatModifierDefinition>());
        scope.Entity.ReceiveDamage(f.Source, 10);
        Assert.Equal(expected, Assert.Single(scope.Entity.LandedDamage), 3);
        Assert.Empty(f.Movement);
    }

    [Fact]
    public void InstalledFinalBoundaryResolvesAfterCallbacksAndNativeWearablesCannotChangeOwnedDamage()
    {
        using var scope = new EngineScope();
        var f = scope.Fixture;
        var wearable = new ModSystemWearableStats();
        var native = AccessTools.Method(typeof(ModSystemWearableStats), "handleDamaged");
        scope.Health.onDamaged += (damage, source) => (float)native.Invoke(wearable, new object[] { f.Player, damage, source })!;
        scope.Health.onDamaged += (damage, _) => damage * .5f;
        f.State.MagicShield = 4;
        scope.Entity.ReceiveDamage(f.Source, 20);
        Assert.Equal(6, Assert.Single(scope.Entity.LandedDamage));
        Assert.Equal(0, f.State.MagicShield);
        Assert.Empty(f.Movement); // Premature resolution would mistake 20 - 4 for lethal damage.
    }

    private sealed class EngineScope : IDisposable
    {
        private readonly Harmony harmony = new("vrpg.tests.player-defense." + Guid.NewGuid());
        public readonly CapturePlayer Entity = new();
        public readonly NativeHealth Health;
        public readonly PlayerDefenseServiceTests.Fixture Fixture;

        public EngineScope()
        {
            Fixture = new PlayerDefenseServiceTests.Fixture(entity: Entity);
            Health = new NativeHealth(Entity);
            Entity.Health = Health;
            EntityDamageScalingPatch.PlayerDamageResolver = Fixture.Defense.ResolveDamage;
            SurvivalCharacterRuntimePatch.OwnershipResolver = _ => true;
            EntityDamageScalingPatch.Patch(harmony);
        }

        public void Dispose()
        {
            harmony.UnpatchAll(harmony.Id);
            EntityDamageScalingPatch.PlayerDamageResolver = null;
            SurvivalCharacterRuntimePatch.OwnershipResolver = null;
        }
    }

    private sealed class NativeHealth(Entity entity) : EntityBehaviorHealth(entity)
    {
        public void TickDamage()
        {
            ProcessDoTEffects(1);
            ProcessDoTEffects(0);
        }
        public void DamageCallbacks(DamageSource source, ref float damage) => ApplyOnDamageDelegates(source, ref damage);
    }

    private sealed class CapturePlayer : EntityPlayer
    {
        public NativeHealth Health = null!;
        public readonly List<float> LandedDamage = new();
        public override bool ReceiveDamage(DamageSource damageSource, float damage)
        {
            SurvivalCharacterRuntimePatch.HealthDamagePrefix(Health, damageSource, ref damage);
            EntityDamageScalingPatch.OnEntityReceiveDamagePrefix(Health, damageSource, ref damage);
            Health.DamageCallbacks(damageSource, ref damage);
            LandedDamage.Add(damage);
            return damage > 0;
        }
    }
}
