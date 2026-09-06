using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using Newtonsoft.Json;
using VRPG.Config;
using VRPG.Data;
using VRPG.Data.Definitions;
using VRPG.Modules.Rpg.Combat;
using VRPG.Modules.Rpg;
using VRPG.Modules.Rpg.Players;
using VRPG.Modules.Rpg.Skills;
using VRPG.Modules.Rpg.Stats;
using VRPG.Modules.Rpg.StatusEffects;
using VRPG.Modules.Rpg.Talents;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Xunit;

namespace VRPG.Tests;

[Collection("EntityDamageScalingPatch")]
public sealed class SkillCastingAuthorityTests
{
    [Theory]
    [InlineData("press")]
    [InlineData("release")]
    [InlineData("direct")]
    public void UnavailableModuleInputDoesNotPublishOrMutateLoadout(string path)
    {
        var f = new Fixture(false);
        f.Resources.IsActive = _ => false;
        using var module = new RpgModule(new RpgModuleConfig(), new VRPGDataRegistry(), f.Channel);
        AccessTools.Field(typeof(RpgModule), "skills").SetValue(module, f.Skills);
        bool success = path == "direct" ? module.TryCastSkill(f.Player, 0, out _, out _)
            : module.TryHandleSkillInput(f.Player, 0, path == "press", out _, out _);
        Assert.False(success);
        Assert.Equal(0, f.Packets);
    }

    [Theory]
    [InlineData("channel")]
    [InlineData("sequence")]
    public void LosingReadinessCancelsPendingHitsWithoutSpendingOrStartingCooldown(string mode)
    {
        var f = new Fixture(false);
        f.Skill.Timing.Mode = mode;
        f.Skill.Timing.HitCount = 3;
        f.Skill.Resource.CostMode = "per_second";
        Assert.True(f.Skills.TryCastSlot(f.Player, 0, out _));
        string before = JsonConvert.SerializeObject(f.State);
        int writes = f.Storage.Writes, packets = f.Packets, queries = f.TargetQueries;
        var cooldowns = JsonConvert.SerializeObject(AccessTools.Field(typeof(SkillCastingService), "cooldownEnds").GetValue(f.Skills));
        f.Resources.IsActive = _ => false;
        f.Now += 1000;
        f.Skills.Tick(1);
        Assert.Equal(before, JsonConvert.SerializeObject(f.State));
        Assert.Equal(writes, f.Storage.Writes);
        Assert.Equal(packets + (mode == "channel" ? 1 : 0), f.Packets);
        if (mode == "channel")
        {
            var stopped = Assert.IsType<VRPG.Network.SkillChannelStatePacket>(f.SentPackets[^1]);
            Assert.False(stopped.Active);
            Assert.Equal(0, stopped.Slot);
            Assert.Equal("vrpg:test", stopped.SkillCode);
        }
        Assert.Equal(queries, f.TargetQueries);
        Assert.Equal(cooldowns, JsonConvert.SerializeObject(AccessTools.Field(typeof(SkillCastingService), "cooldownEnds").GetValue(f.Skills)));
        Assert.Empty((IDictionary)AccessTools.Field(typeof(SkillCastingService), "activeChannels").GetValue(f.Skills)!);
        Assert.Empty((IList)AccessTools.Field(typeof(SkillCastingService), "activeSequences").GetValue(f.Skills)!);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LosingReadinessRejectsDelayedProjectileImpact(bool targeted)
    {
        var f = new Fixture(targeted);
        f.Resources.IsActive = _ => false;
        bool resolved;
        if (targeted)
        {
            var drop = new EntityVrpgTargetedDrop();
            drop.Configure(f.Player.Entity, f.Skill, 1, new());
            resolved = f.Skills.HandleTargetedDropImpact(drop, new());
        }
        else
        {
            f.Skill.Delivery = "projectile_aoe";
            var projectile = new EntityVrpgSkillProjectile { FiredBy = f.Player.Entity };
            projectile.Configure(f.Skill, 1, "");
            resolved = f.Skills.HandleProjectileImpact(projectile);
        }
        Assert.Equal(0, f.TargetQueries);
        Assert.Equal(0, f.Packets);
        Assert.False(resolved);
    }

    [Theory]
    [InlineData("press", false, false)]
    [InlineData("release", false, false)]
    [InlineData("direct", false, false)]
    [InlineData("press", true, false)]
    [InlineData("release", true, false)]
    [InlineData("direct", true, false)]
    [InlineData("press", false, true)]
    [InlineData("release", false, true)]
    [InlineData("direct", false, true)]
    [InlineData("press", true, true)]
    [InlineData("release", true, true)]
    [InlineData("direct", true, true)]
    public void UnavailableInputRejectsBeforeCostCooldownEffectsOrSuccess(string path, bool targeted, bool invalidGear)
    {
        var f = new Fixture(targeted);
        f.Resources.IsActive = _ => invalidGear; // Either ownership is inactive or the skill snapshot is invalid.
        f.Skills.EquippedStatsProvider = _ => invalidGear ? EquippedGearStats.Invalid : EquippedGearStats.Empty;
        string before = JsonConvert.SerializeObject(f.State);
        int writes = f.Storage.Writes;
        bool success = path == "direct" ? f.Skills.TryCastSlot(f.Player, 0, out _)
            : f.Skills.TryHandleSlotInput(f.Player, 0, path == "press", out _, out _);
        Assert.Equal(before, JsonConvert.SerializeObject(f.State));
        Assert.Equal(writes, f.Storage.Writes);
        Assert.Equal(0, f.Packets);
        Assert.Equal(0, f.TargetQueries);
        Assert.Empty((IDictionary)AccessTools.Field(typeof(SkillCastingService), "cooldownEnds").GetValue(f.Skills)!);
        Assert.False(success);
    }

    internal sealed class Fixture
    {
        public readonly SkillPlayer Player = new();
        public readonly RpgPlayerStore Store = new();
        public readonly MemoryStorage Storage = new();
        public readonly RpgResourceService Resources;
        public readonly SkillCastingService Skills;
        public readonly SkillDefinition Skill;
        public readonly IServerNetworkChannel Channel;
        public readonly ICoreServerAPI Api;
        public readonly VRPGDataRegistry Data;
        public readonly List<object> SentPackets = new();
        public int Packets, TargetQueries;
        public long Now = 1000;
        public RpgPlayerState State => Store.GetOrCreate(Player);

        public Fixture(bool targeted)
        {
            Store.Load(Storage);
            Player.Entity.WatchedAttributes["health"] = new TreeAttribute();
            var data = new VRPGDataRegistry();
            Data = data;
            data.Skills.Load(new[] { new SkillDefinition
            {
                Code = "vrpg:test", Name = "Test", Delivery = targeted ? "targeted_drop" : "circle",
                Resource = new() { Type = "mana", Base = 5 },
                Timing = new() { Mode = targeted ? "targeted_release" : "instant" }
            } });
            Skill = data.Skills.Get("vrpg:test")!;
            State.SkillLevels["vrpg:test"] = 1;
            State.EquippedSkills[0] = "vrpg:test";
            State.ResourcesInitialized = true;
            State.Mana = 37.25f;
            State.MagicShield = 42;
            State.MaxMagicShield = 84;
            var world = PlayerDefenseServiceTests.Proxy.Create<IServerWorldAccessor>((name, _) =>
            {
                switch (name)
                {
                    case "get_ElapsedMilliseconds": return Now;
                    case "get_Side": return EnumAppSide.Server;
                    case "PlayerByUid": return Player;
                    case "get_AllOnlinePlayers": return Array.Empty<IPlayer>();
                    case "GetEntityType": return new EntityProperties();
                    case "GetEntitiesAround": TargetQueries++; return Array.Empty<Entity>();
                    case "RayTraceForSelection": TargetQueries++; return null;
                    default: throw new NotSupportedException(name);
                }
            });
            Player.Entity.World = world;
            var logger = PlayerDefenseServiceTests.Proxy.Create<ILogger>((_, _) => null);
            var api = PlayerDefenseServiceTests.Proxy.Create<ICoreServerAPI>((name, _) => name switch
            {
                "get_World" => world,
                "get_Logger" => logger,
                _ => throw new NotSupportedException(name)
            });
            Api = api;
            var channel = PlayerDefenseServiceTests.Proxy.Create<IServerNetworkChannel>((name, args) =>
            {
                if (name != "SendPacket") throw new NotSupportedException(name);
                Packets++;
                SentPackets.Add(args![0]!);
                return null;
            });
            Channel = channel;
            var config = new RpgModuleConfig();
            var talents = new TalentTreeCatalog();
            Resources = new RpgResourceService(api, config, data, talents, Store, channel, new CombatStateService(api, 1));
            Skills = new SkillCastingService(api, data, Store, Resources, new SkillDamageResolver(talents), channel,
                new CombatVisualBroadcaster(api, channel), null!, new StatusEffectTracker(data));
        }
    }

    internal sealed class MemoryStorage : IRpgPlayerStateStorage
    {
        public int Writes;
        public byte[]? Read() => null;
        public void Write(byte[] bytes) => Writes++;
    }

    internal sealed class SkillPlayer() : Vintagestory.Server.ServerPlayer(null!,
        new Vintagestory.Server.ServerWorldPlayerData { EntityPlayer = new EntityPlayer() })
    {
        public override string PlayerUID => "p";
        public override bool HasPrivilege(string privilegeCode) => true;
        protected override void Init() { }
    }
}
