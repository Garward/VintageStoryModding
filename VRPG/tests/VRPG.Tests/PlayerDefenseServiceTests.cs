using System;
using System.Collections.Generic;
using System.Reflection;
using VRPG.Config;
using VRPG.Data.Definitions;
using VRPG.Modules.Rpg.Combat;
using VRPG.Modules.Rpg.Movement;
using VRPG.Modules.Rpg.Players;
using VRPG.Modules.Rpg.Stats;
using VRPG.Modules.Rpg.Talents;
using VRPG.Network;
using VRPG.Patches;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.Server;
using Xunit;

namespace VRPG.Tests;

[Collection("EntityDamageScalingPatch")]
public sealed class PlayerDefenseServiceTests
{
    [Fact]
    public void ShieldSpendingPrecedesPostShieldLethalEscape()
    {
        var f = new Fixture();
        f.State.MagicShield = 8;
        Assert.Equal(7, f.Resolve(15));
        Assert.Equal(0, f.State.MagicShield);
        Assert.Empty(f.Movement);
        f.State.MagicShield = 5;
        Assert.Equal(0, f.Resolve(15));
        Assert.Equal(0, f.State.MagicShield);
        Assert.Single(f.Movement);
        Assert.Equal(.28, f.Player.Entity.Pos.Motion.X, 5);
        Assert.Equal(6, f.Escape.CooldownRemainingSeconds(f.Player));
    }

    [Fact]
    public void ActiveInvulnerabilityRunsBeforeRngStatsAndShieldButExplicitBypassConnects()
    {
        var f = new Fixture();
        Assert.Equal(0, f.Resolve(10));
        f.State.MagicShield = 30;
        int rolls = f.Rolls;
        int reads = f.GearReads;
        Assert.Equal(0, f.Resolve(100));
        Assert.Equal(rolls, f.Rolls);
        Assert.Equal(reads, f.GearReads);
        Assert.Equal(30, f.State.MagicShield);
        f.Source.IgnoreInvFrames = true;
        Assert.Equal(70, f.Resolve(100));
        Assert.Equal(0, f.State.MagicShield);
    }

    [Fact]
    public void MetadataBypassesInvulnerabilityAndPreventsEvasion()
    {
        var f = new Fixture();
        f.Resolve(10);
        f.Gear = Gear(evasion: .9f);
        f.Roll = 0;
        f.Context = new(IgnoreInvulnerability: true, Unavoidable: true);
        Assert.Equal(10, f.Resolve(10));
        Assert.Equal(1, f.Rolls);
    }

    [Theory]
    [InlineData(EnumDamageSource.Entity, false, false, false, false, 0, 1)]
    [InlineData(EnumDamageSource.Entity, true, false, false, false, 5, 0)]
    [InlineData(EnumDamageSource.Entity, false, true, false, false, 5, 0)]
    [InlineData(EnumDamageSource.Entity, false, false, true, false, 5, 0)]
    [InlineData(EnumDamageSource.Entity, false, false, false, true, 5, 0)]
    [InlineData(EnumDamageSource.Block, false, false, false, false, 5, 0)]
    [InlineData(EnumDamageSource.Internal, false, false, false, false, 5, 0)]
    [InlineData(EnumDamageSource.Fall, false, false, false, false, 5, 0)]
    [InlineData(EnumDamageSource.Bleed, false, false, false, false, 5, 0)]
    public void OnlyEligibleDirectHitsRollEvasion(EnumDamageSource source, bool duration, bool dot,
        bool hazard, bool unavoidable, float expected, int rolls)
    {
        var f = new Fixture { Gear = Gear(evasion: .9f), Roll = 0 };
        f.Source.Source = source;
        f.Source.Duration = duration ? TimeSpan.FromSeconds(1) : TimeSpan.Zero;
        f.Context = new(DamageOverTime: dot, PersistentHazard: hazard, Unavoidable: unavoidable);
        Assert.Equal(expected, f.Resolve(5));
        Assert.Equal(rolls, f.Rolls);
    }

    [Theory]
    [InlineData(EnumDamageType.Fire, 90)]
    [InlineData(EnumDamageType.Frost, 80)]
    [InlineData(EnumDamageType.Electricity, 70)]
    [InlineData(EnumDamageType.Acid, 60)]
    public void NativeSpellTypesUseMatchingResistanceAndCanBeEvaded(EnumDamageType type, float expected)
    {
        var f = new Fixture { Gear = Gear(armor: 100000, evasion: .9f, fire: .1f, cold: .2f, lightning: .3f, rust: .4f), Dexterity = false };
        f.Source.Type = type;
        Assert.Equal(expected, f.Resolve(100), 3);
        f.Roll = 0;
        Assert.Equal(0, f.Resolve(100));
    }

    [Fact]
    public void AuthoritativeContextAppliesAccuracyPenetrationExposureAndOtherReduction()
    {
        var f = new Fixture { Gear = Gear(evasion: .9f, fire: .75f), Roll = .6, Dexterity = false };
        f.Source.Type = EnumDamageType.Fire;
        f.Context = new(Accuracy: .4, ResistancePenetration: .25, ResistanceExposure: .1, OtherReductionMultiplier: .5);
        f.State.MagicShield = 4;
        Assert.Equal(26, f.Resolve(100), 3);
        Assert.Equal(0, f.State.MagicShield);
    }

    [Fact]
    public void ArmorPressureTracksAttackerLevelAndLowArmorConnectedHitsRemainDangerous()
    {
        var f = new Fixture { Gear = Gear(armor: 150), Dexterity = false };
        Assert.Equal(50, f.Resolve(100), 3);
        f.Attacker.WatchedAttributes.SetInt("vrpgLevel", 2);
        Assert.Equal(51.92308f, f.Resolve(100), 4);
        f.Gear = Gear();
        Assert.Equal(100, f.Resolve(100));
    }

    [Fact]
    public void EscapeRequiresHostileLethalityAndSelectedClassAuthority()
    {
        var f = new Fixture();
        Assert.False(f.Escape.TryActivate(f.Player.Entity, f.Source, false));
        f.Dexterity = false;
        f.State.StartingAttributeAffinity = "dexterity";
        f.State.BaseStats["dexterity"] = 100;
        Assert.False(f.Escape.TryActivate(f.Player.Entity, f.Source, true));
        f.Dexterity = true;
        f.Hostile = false;
        Assert.False(f.Escape.TryActivate(f.Player.Entity, f.Source, true));
        f.Hostile = true;
        f.Source.CauseEntity = f.Player.Entity;
        Assert.False(f.Escape.TryActivate(f.Player.Entity, f.Source, true));
        f.Source.CauseEntity = f.Attacker;
        f.Source.Source = EnumDamageSource.Internal;
        Assert.False(f.Escape.TryActivate(f.Player.Entity, f.Source, true));
        Assert.Empty(f.Movement);
    }

    [Fact]
    public void CooldownIframeExpiryAndClassLossUseServerClock()
    {
        var f = new Fixture();
        Assert.Equal(0, f.Resolve(10));
        f.Now = 379;
        f.Dexterity = false;
        Assert.True(f.Escape.IsInvulnerable(f.Player.Entity));
        f.Now = 380;
        Assert.False(f.Escape.IsInvulnerable(f.Player.Entity));
        Assert.Equal(10, f.Resolve(10));
        f.Dexterity = true;
        f.Now = 5999;
        Assert.Equal(10, f.Resolve(10));
        f.Now = 6000;
        Assert.Equal(0, f.Resolve(10));
        Assert.Equal(2, f.Movement.Count);
    }

    [Fact]
    public void QuickReflexUntouchableMomentAndSlipstreamModifyTheirOwnLayers()
    {
        var f = new Fixture();
        f.AddTalent("quick_reflex", "evasive_step_cooldown", "percent", -10);
        f.AddTalent("untouchable_moment", "evasive_step_invulnerability", "add", .05f);
        f.AddTalent("slipstream", "evasive_step_impulse", "add", .05f);
        Assert.Equal(0, f.Resolve(10));
        Assert.Equal(5.4f, f.Escape.CooldownRemainingSeconds(f.Player), 3);
        Assert.Equal(.33, f.Player.Entity.Pos.Motion.X, 5);
        f.Now = 429;
        Assert.True(f.Escape.IsInvulnerable(f.Player.Entity));
        f.Now = 430;
        Assert.False(f.Escape.IsInvulnerable(f.Player.Entity));
        f.Now = 5400;
        Assert.Equal(0, f.Resolve(10));
    }

    [Fact]
    public void ConfigClampsApplyToEscapeAndFallbackDirectionIsFinite()
    {
        var f = new Fixture(new RpgModuleConfig { EvasiveStepCooldownSeconds = 0,
            EvasiveStepInvulnerabilitySeconds = 9, EvasiveStepImpulse = 9 });
        f.Attacker.Pos.SetPos(f.Player.Entity.Pos.XYZ);
        Assert.Equal(0, f.Resolve(10));
        Assert.Equal(.5f, f.Escape.CooldownRemainingSeconds(f.Player));
        var motion = f.Player.Entity.Pos.Motion;
        Assert.Equal(.6, Math.Sqrt(motion.X * motion.X + motion.Z * motion.Z), 5);
        f.Now = 749;
        Assert.True(f.Escape.IsInvulnerable(f.Player.Entity));
        f.Now = 750;
        Assert.False(f.Escape.IsInvulnerable(f.Player.Entity));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PatchRoutesEnvironmentalAndSelfDamageToPlayerResolver(bool self)
    {
        var f = new Fixture { Dexterity = false, Gear = Gear(armor: 150) };
        f.Source.Source = EnumDamageSource.Internal;
        f.Source.SourceEntity = self ? f.Player.Entity : null;
        f.Source.CauseEntity = null;
        EntityDamageScalingPatch.PlayerDamageResolver = f.Defense.ResolveDamage;
        try
        {
            float damage = 10;
            EntityDamageScalingPatch.OnEntityReceiveDamagePrefix(new HealthBehavior(f.Player.Entity), f.Source, ref damage);
            EntityDamageScalingPatch.AfterDamageCallbacks(new HealthBehavior(f.Player.Entity), f.Source, ref damage);
            Assert.Equal(5, damage);
        }
        finally { EntityDamageScalingPatch.PlayerDamageResolver = null; }
    }

    [Fact]
    public void PatchRunsIncomingAfterOutgoingAndCreatureScalingAndOnlyForPlayers()
    {
        var target = new EntityPlayer();
        var attacker = new EntityAgent();
        attacker.WatchedAttributes.SetFloat("vrpgDamageMultiplier", 2);
        var source = new DamageSource { Source = EnumDamageSource.Entity, SourceEntity = attacker, Type = EnumDamageType.BluntAttack };
        var observed = new List<float>();
        EntityDamageScalingPatch.PlayerDamageResolver = (_, _, value) => { observed.Add(value); return value / 2; };
        EntityDamageScalingPatch.PlayerOutgoingDamageResolver = (_, _, _) => 30;
        try
        {
            float damage = 10;
            EntityDamageScalingPatch.OnEntityReceiveDamagePrefix(new HealthBehavior(target), source, ref damage);
            EntityDamageScalingPatch.AfterDamageCallbacks(new HealthBehavior(target), source, ref damage);
            Assert.Equal(10, damage);
            source.SourceEntity = new EntityPlayer();
            source.SourceEntity.WatchedAttributes.SetFloat("vrpgDamageMultiplier", 9);
            damage = 10;
            EntityDamageScalingPatch.OnEntityReceiveDamagePrefix(new HealthBehavior(target), source, ref damage);
            EntityDamageScalingPatch.AfterDamageCallbacks(new HealthBehavior(target), source, ref damage);
            Assert.Equal(15, damage);
            damage = 10;
            EntityDamageScalingPatch.OnEntityReceiveDamagePrefix(new HealthBehavior(attacker), source, ref damage);
            EntityDamageScalingPatch.AfterDamageCallbacks(new HealthBehavior(attacker), source, ref damage);
            Assert.Equal(30, damage);
            Assert.Equal(new[] { 20f, 30f }, observed);
        }
        finally
        {
            EntityDamageScalingPatch.PlayerDamageResolver = null;
            EntityDamageScalingPatch.PlayerOutgoingDamageResolver = null;
        }
    }

    private static EquippedGearStats Gear(float armor = 0, float evasion = 0, float fire = 0,
        float cold = 0, float lightning = 0, float rust = 0) =>
        new(0, armor, 0, 0, evasion, fire, cold, lightning, rust, Array.Empty<StatModifierDefinition>());

    private sealed class HealthBehavior(Entity entity) : EntityBehavior(entity)
    { public override string PropertyName() => "health"; }

    internal sealed class Fixture
    {
        public long Now;
        public bool Dexterity = true, Hostile = true;
        public double Roll = .99;
        public int Rolls, GearReads;
        public EquippedGearStats Gear = EquippedGearStats.Empty;
        public PlayerDefenseContext Context = new();
        public readonly TestPlayer Player;
        public readonly EntityAgent Attacker = new();
        public readonly RpgPlayerStore Store = new();
        public readonly TalentTreeCatalog Talents = new();
        public readonly List<MovementEffectPacket> Movement = new();
        public readonly DamageSource Source;
        public readonly EvasiveStepService Escape;
        public readonly PlayerDefenseService Defense;
        public RpgPlayerState State => Store.GetOrCreate(Player);

        public Fixture(RpgModuleConfig? config = null, EntityPlayer? entity = null)
        {
            Player = new TestPlayer(entity ?? new EntityPlayer());
            config ??= new RpgModuleConfig();
            var world = Proxy.Create<IServerWorldAccessor>((name, _) => name switch
            {
                "get_ElapsedMilliseconds" => Now,
                "get_Side" => EnumAppSide.Server,
                "PlayerByUid" => Player,
                "get_AllOnlinePlayers" => Array.Empty<IPlayer>(),
                _ => throw new NotSupportedException(name)
            });
            var api = Proxy.Create<ICoreServerAPI>((name, _) => name == "get_World" ? world : throw new NotSupportedException(name));
            var channel = Proxy.Create<IServerNetworkChannel>((name, args) =>
            {
                if (name != "SendPacket") throw new NotSupportedException(name);
                if (args![0] is MovementEffectPacket packet) Movement.Add(packet);
                return null;
            });
            Player.Entity.World = world;
            Player.Entity.WatchedAttributes.SetString("playerUID", "p");
            Player.Entity.WatchedAttributes["health"] = new TreeAttribute();
            Player.Entity.WatchedAttributes.GetTreeAttribute("health").SetFloat("currenthealth", 10);
            Player.Entity.Pos.SetPos(1, 0, 0);
            Source = new DamageSource { SourceEntity = Attacker, Source = EnumDamageSource.Entity, Type = EnumDamageType.BluntAttack };
            Escape = new EvasiveStepService(api, config, Talents, Store, channel,
                new PlayerMovementEffectService(api, channel), _ => Dexterity, (_, _) => Hostile);
            var resources = new RpgResourceService(api, config, null!, Talents, Store, channel, new CombatStateService(api, 1));
            Defense = new PlayerDefenseService(Store, resources, Escape, _ => { GearReads++; return Gear; },
                (_, _) => Context, () => { Rolls++; return Roll; });
        }

        public float Resolve(float damage) => Defense.ResolveDamage(Player.Entity, Source, damage);
        public void AddTalent(string code, string stat, string operation, float value)
        {
            State.Talents.Add("vrpg:" + code);
            var nodes = new List<TalentNodeDefinition>(Talents.All);
            nodes.Add(new TalentNodeDefinition { Code = "vrpg:" + code, Modifiers = new[]
            { new StatModifierDefinition { Stat = "vrpg:" + stat, Operation = operation, Min = value, Max = value } } });
            Talents.Replace("test", "Test", nodes.ToArray());
        }
    }

    public class Proxy : DispatchProxy
    {
        private System.Func<string, object?[]?, object?> call = null!;
        public static T Create<T>(System.Func<string, object?[]?, object?> call) where T : class
        {
            T proxy = DispatchProxy.Create<T, Proxy>();
            ((Proxy)(object)proxy).call = call;
            return proxy;
        }
        protected override object? Invoke(MethodInfo? method, object?[]? args) => call(method!.Name, args);
    }

    internal sealed class TestPlayer(EntityPlayer entity) : ServerPlayer(null!, new ServerWorldPlayerData { EntityPlayer = entity })
    {
        public override string PlayerUID => "p";
        protected override void Init() { }
    }
}
