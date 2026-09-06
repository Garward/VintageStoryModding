using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json;
using VRPG.Config;
using VRPG.Data;
using VRPG.Data.Definitions;
using VRPG.Data.Library;
using VRPG.Modules.Rpg;
using VRPG.Modules.Rpg.Combat;
using VRPG.Modules.Rpg.Players;
using VRPG.Modules.Rpg.Skills;
using VRPG.Modules.Rpg.Stats;
using VRPG.Modules.Rpg.StatusEffects;
using VRPG.Modules.Rpg.Talents;
using VRPG.Network;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Xunit;

namespace VRPG.Tests;

[Collection("EntityDamageScalingPatch")]
public sealed class SkillPacketAuthorityTests
{
    [Theory]
    [InlineData("equip", false)]
    [InlineData("equip", true)]
    [InlineData("press", false)]
    [InlineData("press", true)]
    [InlineData("release", false)]
    [InlineData("release", true)]
    [InlineData("notify", false)]
    [InlineData("notify", true)]
    public void UnavailableFeedbackNeverLooksUpOrNormalizesTheSharedProfile(string action, bool malformed)
    {
        using var f = new Fixture();
        f.AssertSharedStore();
        if (malformed)
        {
            var profile = f.Store.GetOrCreate(f.Player);
            profile.SkillLevels = null!;
            profile.EquippedSkills = new string[] { null! };
            f.Store.Save();
        }
        string before = JsonConvert.SerializeObject(f.Profiles);
        int writes = f.Storage.Writes;
        byte[]? persisted = f.Storage.Bytes?.ToArray();
        for (int i = 0; i < 2; i++)
        {
            if (action == "equip") f.Equip("test");
            else if (action == "notify") f.Module.NotifyAbilityFailure(f.Player, AbilityFailureKind.Cooldown, "unused");
            else f.Cast(action == "press");
        }
        Assert.Equal(before, JsonConvert.SerializeObject(f.Profiles));
        Assert.Equal(writes, f.Storage.Writes);
        Assert.Equal(persisted, f.Storage.Bytes);
        Assert.Empty(f.Packets);
        Assert.Equal("Skills are unavailable right now.", Assert.Single(f.Player.Messages));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ActiveEquipPacketsStillBuildTheHubAndReportTheRealResult(bool valid)
    {
        using var f = new Fixture();
        f.Active = true;
        f.Store.GetOrCreate(f.Player).SkillLevels["vrpg:test"] = 1;
        f.AssertSharedStore();
        f.Equip(valid ? "test" : "missing");
        var hub = Assert.Single(f.Packets.OfType<OpenHubPacket>());
        Assert.Equal(!valid, hub.FeedbackError);
        Assert.NotEmpty(hub.Entries);
        Assert.NotNull(hub.Options);
        Assert.Equal(valid ? "vrpg:test" : "", f.Profiles["p"].EquippedSkills[1]);
        Assert.Equal(valid ? 1 : 0, f.Packets.OfType<SkillLoadoutPacket>().Count());
        Assert.Empty(f.Player.Messages);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ActiveCastFailureStillRespectsNotificationPreferences(bool notify)
    {
        using var f = new Fixture();
        f.Active = true;
        var profile = f.Store.GetOrCreate(f.Player);
        profile.SkillLevels["vrpg:test"] = 1;
        profile.EquippedSkills[0] = "vrpg:test";
        profile.ResourcesInitialized = true;
        profile.Mana = 0;
        profile.ShowResourceNotifications = notify;
        f.Cast(true);
        Assert.Equal(notify ? 1 : 0, f.Player.Messages.Count);
        if (notify) Assert.Contains("mana", f.Player.Messages[0]);
        Assert.Single(f.Packets.OfType<SkillLoadoutPacket>());
        Assert.Empty(f.Packets.OfType<OpenHubPacket>());
        Assert.Equal(0, f.Profiles["p"].Mana);
    }

    private sealed class Fixture : IDisposable
    {
        public readonly TestPlayer Player = new();
        public readonly Storage Storage = new();
        public readonly RpgModule Module;
        public readonly RpgPlayerStore Store;
        public readonly SkillCastingService Skills;
        public readonly RpgResourceService Resources;
        public readonly AbilityFailureNotificationService Notifications;
        public readonly VRPGModSystem System = new();
        public readonly List<object> Packets = new();
        private readonly ICoreServerAPI api;
        public bool Active;
        public Dictionary<string, RpgPlayerState> Profiles => Field<Dictionary<string, RpgPlayerState>>(Store, "states");

        public Fixture()
        {
            var data = new VRPGDataRegistry();
            data.Skills.Load(new[] { new SkillDefinition { Code = "vrpg:test", Name = "Test", Delivery = "circle",
                Resource = new() { Type = "mana", Base = 5 } } });
            var world = PlayerDefenseServiceTests.Proxy.Create<IServerWorldAccessor>((name, _) => name switch
            {
                "get_ElapsedMilliseconds" => 1000L,
                "get_Side" => EnumAppSide.Server,
                "PlayerByUid" => Player,
                "get_AllOnlinePlayers" => Array.Empty<IPlayer>(),
                _ => throw new NotSupportedException(name)
            });
            var logger = PlayerDefenseServiceTests.Proxy.Create<ILogger>((_, _) => null);
            api = PlayerDefenseServiceTests.Proxy.Create<ICoreServerAPI>((name, _) => name switch
            {
                "get_World" => world,
                "get_Logger" => logger,
                _ => throw new NotSupportedException(name)
            });
            var channel = PlayerDefenseServiceTests.Proxy.Create<IServerNetworkChannel>((name, args) =>
            {
                if (name != "SendPacket") throw new NotSupportedException(name);
                Packets.Add(args![0]!);
                return null;
            });
            Player.Entity.World = world;
            Player.Entity.WatchedAttributes["health"] = new TreeAttribute();
            var config = new RpgModuleConfig();
            Module = new RpgModule(config, data, channel);
            // Derive the store from the real module; never replace it with a parallel fixture store.
            Store = Field<RpgPlayerStore>(Module, "playerStore");
            Store.Load(Storage);
            var talents = new TalentTreeCatalog();
            Resources = new RpgResourceService(api, config, data, talents, Store, channel, new CombatStateService(api, 1))
                { IsActive = _ => Active };
            Skills = new SkillCastingService(api, data, Store, Resources, new SkillDamageResolver(talents), channel,
                new CombatVisualBroadcaster(api, channel), null!, new StatusEffectTracker(data));
            Notifications = new AbilityFailureNotificationService(api, Store);
            AccessTools.Field(typeof(RpgModule), "resources").SetValue(Module, Resources);
            AccessTools.Field(typeof(RpgModule), "skills").SetValue(Module, Skills);
            AccessTools.Field(typeof(RpgModule), "failureNotifications").SetValue(Module, Notifications);
            AccessTools.Field(typeof(VRPGModSystem), "rpgModule").SetValue(System, Module);
            AccessTools.Field(typeof(VRPGModSystem), "serverChannel").SetValue(System, channel);
            AccessTools.Field(typeof(VRPGModSystem), "libraryIndex").SetValue(System, LibraryIndex.Build(data));
            AssertSharedStore();
        }

        public void AssertSharedStore()
        {
            Assert.Same(Store, Field<RpgPlayerStore>(Module, "playerStore"));
            Assert.Same(Store, Field<RpgPlayerStore>(Skills, "playerStore"));
            Assert.Same(Store, Field<RpgPlayerStore>(Resources, "playerStore"));
            Assert.Same(Store, Field<RpgPlayerStore>(Notifications, "playerStore"));
        }

        public void Equip(string code) => AccessTools.Method(typeof(VRPGModSystem), "HandleSkillEquipRequest").Invoke(System,
            new object[] { api, Player, new SkillEquipRequestPacket { Slot = 2, SkillCode = code } });
        public void Cast(bool pressed) => AccessTools.Method(typeof(VRPGModSystem), "HandleSkillCastRequest").Invoke(System,
            new object[] { Player, new SkillCastRequestPacket { Slot = 0, Pressed = pressed } });
        public void Dispose() => Module.Dispose();
    }

    private static T Field<T>(object owner, string name) => (T)AccessTools.Field(owner.GetType(), name).GetValue(owner)!;

    private sealed class Storage : IRpgPlayerStateStorage
    {
        public int Writes;
        public byte[]? Bytes;
        public byte[]? Read() => Bytes;
        public void Write(byte[] bytes) { Writes++; Bytes = bytes.ToArray(); }
    }

    private sealed class TestPlayer() : Vintagestory.Server.ServerPlayer(null!,
        new Vintagestory.Server.ServerWorldPlayerData { EntityPlayer = new EntityPlayer() }), IServerPlayer
    {
        public readonly List<string> Messages = new();
        public override string PlayerUID => "p";
        public override bool HasPrivilege(string privilegeCode) => true;
        void IServerPlayer.SendMessage(int groupId, string message, EnumChatType chatType, string data) => Messages.Add(message);
        protected override void Init() { }
    }
}
