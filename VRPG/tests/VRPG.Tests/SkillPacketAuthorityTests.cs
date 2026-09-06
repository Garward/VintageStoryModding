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
    [InlineData("disabled")]
    [InlineData("module")]
    public void ProductionFallbackClockDoesNotRequireAnyPlayerWorldAccess(string path)
    {
        using var f = new Fixture(injectFallbackClock: false);
        f.ConfigureFeedback(path);
        f.Player.Entity.World = PlayerDefenseServiceTests.Proxy.Create<IServerWorldAccessor>((name, _) =>
            throw new NotSupportedException(name));
        Assert.Null(Record.Exception(() => f.Feedback(path, f.Player)));
        Assert.Single(f.Player.Messages);
        Assert.Empty(f.Profiles);
        Assert.Equal(0, f.Storage.Writes);
        Assert.Empty(f.Packets);
    }

    [Theory]
    [InlineData("disabled", false)]
    [InlineData("disabled", true)]
    [InlineData("module", false)]
    [InlineData("module", true)]
    public void FallbackFeedbackKeepsItsClockDomainWhenPlayerWorldChanges(string path, bool startsWithWorld)
    {
        using var f = new Fixture();
        f.ConfigureFeedback(path);
        var world = f.Player.Entity.World;
        if (!startsWithWorld) f.Player.Entity.World = null!;
        f.Feedback(path, f.Player);
        Assert.Single(f.Player.Messages);

        f.Now = 2599;
        f.Player.Entity.World = startsWithWorld ? null! : world;
        f.Feedback(path, f.Player);
        Assert.Single(f.Player.Messages);
        f.Now = 2600;
        f.Feedback(path, f.Player);
        Assert.Equal(2, f.Player.Messages.Count);

        f.Player.Entity.World = startsWithWorld ? world : null!;
        f.FeedbackBurst(path, f.Player);
        Assert.Equal(2, f.Player.Messages.Count);
        Assert.Empty(f.Profiles);
        Assert.Equal(0, f.Storage.Writes);
        Assert.Empty(f.Packets);
    }

    [Theory]
    [InlineData("disabled", false)]
    [InlineData("disabled", true)]
    [InlineData("module", false)]
    [InlineData("module", true)]
    [InlineData("service", false)]
    [InlineData("service", true)]
    public void UnavailableFeedbackThrottlesAcrossInputsWithoutProfileAccess(string path, bool malformed)
    {
        using var f = new Fixture();
        if (malformed)
        {
            var profile = f.Store.GetOrCreate(f.Player);
            profile.SkillLevels = null!;
            profile.EquippedSkills = new string[] { null! };
            f.Store.Save();
        }
        f.ConfigureFeedback(path);
        string before = JsonConvert.SerializeObject(f.Profiles);
        byte[]? persisted = f.Storage.Bytes?.ToArray();
        int writes = f.Storage.Writes;

        f.FeedbackBurst(path, f.Player);
        Assert.Single(f.Player.Messages);
        f.Now = 2599;
        f.Feedback(path, f.Player);
        Assert.Single(f.Player.Messages);
        f.Now = 2600;
        f.Feedback(path, f.Player);
        Assert.Equal(2, f.Player.Messages.Count);
        Assert.All(f.Player.Messages, message => Assert.Equal("Skills are unavailable right now.", message));
        Assert.Equal(before, JsonConvert.SerializeObject(f.Profiles));
        Assert.Equal(persisted, f.Storage.Bytes);
        Assert.Equal(writes, f.Storage.Writes);
        Assert.Empty(f.Packets);
    }

    [Theory]
    [InlineData("disabled", "PlayerLeave")]
    [InlineData("disabled", "PlayerDisconnect")]
    [InlineData("disabled", "dispose")]
    [InlineData("module", "PlayerLeave")]
    [InlineData("module", "dispose")]
    public void FallbackFeedbackCleanupRespectsPlayerAndWorldLifetimes(string path, string cleanup)
    {
        using var f = new Fixture();
        f.ConfigureFeedback(path);
        var other = new TestPlayer("other");
        other.Entity.World = f.Player.Entity.World;
        f.FeedbackBurst(path, f.Player);
        f.FeedbackBurst(path, other);
        Assert.Single(f.Player.Messages);
        Assert.Single(other.Messages);

        if (cleanup == "dispose")
        {
            if (path == "disabled")
            {
                f.System.Dispose();
                Assert.All(f.EventHandlers.Values, handler => Assert.Null(handler));
                f.ConfigureFeedback(path);
            }
            else f.Module.Dispose();
        }
        else if (path == "disabled") f.Raise(cleanup, f.Player);
        else AccessTools.Method(typeof(RpgModule), "OnPlayerLeave").Invoke(f.Module, new object[] { f.Player });

        string before = JsonConvert.SerializeObject(f.Profiles);
        int writes = f.Storage.Writes;
        f.FeedbackBurst(path, f.Player);
        f.FeedbackBurst(path, other);
        Assert.Equal(2, f.Player.Messages.Count);
        Assert.Equal(cleanup == "dispose" ? 2 : 1, other.Messages.Count);
        Assert.Equal(before, JsonConvert.SerializeObject(f.Profiles));
        Assert.Equal(writes, f.Storage.Writes);
        Assert.Empty(f.Packets);
    }

    [Theory]
    [InlineData("disabled")]
    [InlineData("module")]
    public void FallbackFeedbackStateDoesNotLeakBetweenWorldOwners(string path)
    {
        using var first = new Fixture();
        using var second = new Fixture();
        first.ConfigureFeedback(path);
        second.ConfigureFeedback(path);
        first.Feedback(path, first.Player);
        second.Feedback(path, second.Player);
        Assert.Single(first.Player.Messages);
        Assert.Single(second.Player.Messages);
        Assert.Empty(first.Profiles);
        Assert.Empty(second.Profiles);
        Assert.Equal(0, first.Storage.Writes);
        Assert.Equal(0, second.Storage.Writes);
    }

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
        public readonly Dictionary<string, Delegate?> EventHandlers = new();
        private readonly ICoreServerAPI api;
        public bool Active;
        public long Now = 1000;
        public Dictionary<string, RpgPlayerState> Profiles => Field<Dictionary<string, RpgPlayerState>>(Store, "states");

        public Fixture(bool injectFallbackClock = true)
        {
            var data = new VRPGDataRegistry();
            data.Skills.Load(new[] { new SkillDefinition { Code = "vrpg:test", Name = "Test", Delivery = "circle",
                Resource = new() { Type = "mana", Base = 5 } } });
            var world = PlayerDefenseServiceTests.Proxy.Create<IServerWorldAccessor>((name, _) => name switch
            {
                "get_ElapsedMilliseconds" => Now,
                "get_Side" => EnumAppSide.Server,
                "PlayerByUid" => Player,
                "get_AllOnlinePlayers" => Array.Empty<IPlayer>(),
                _ => throw new NotSupportedException(name)
            });
            var logger = PlayerDefenseServiceTests.Proxy.Create<ILogger>((_, _) => null);
            var events = PlayerDefenseServiceTests.Proxy.Create<IServerEventAPI>((name, args) =>
            {
                string key = name[(name.IndexOf('_') + 1)..];
                EventHandlers.TryGetValue(key, out var current);
                if (name.StartsWith("add_", StringComparison.Ordinal)) EventHandlers[key] = Delegate.Combine(current, (Delegate)args![0]!);
                else if (name.StartsWith("remove_", StringComparison.Ordinal)) EventHandlers[key] = Delegate.Remove(current, (Delegate)args![0]!);
                else throw new NotSupportedException(name);
                return null;
            });
            IChatCommand? command = null;
            command = PlayerDefenseServiceTests.Proxy.Create<IChatCommand>((_, _) => command);
            var parsers = new CommandArgumentParsers(null!);
            var commands = PlayerDefenseServiceTests.Proxy.Create<IChatCommandApi>((name, _) => name switch
            {
                "GetOrCreate" => command,
                "get_Parsers" => parsers,
                _ => throw new NotSupportedException(name)
            });
            IServerNetworkChannel? channel = null;
            channel = PlayerDefenseServiceTests.Proxy.Create<IServerNetworkChannel>((name, args) =>
            {
                if (name is "RegisterMessageType" or "SetMessageHandler") return channel;
                if (name != "SendPacket") throw new NotSupportedException(name);
                Packets.Add(args![0]!);
                return null;
            });
            var network = PlayerDefenseServiceTests.Proxy.Create<IServerNetworkAPI>((name, _) =>
                name == "RegisterChannel" ? channel : throw new NotSupportedException(name));
            api = PlayerDefenseServiceTests.Proxy.Create<ICoreServerAPI>((name, _) => name switch
            {
                "get_World" => world,
                "get_Logger" => logger,
                "get_Event" => events,
                "get_Network" => network,
                "get_ChatCommands" => commands,
                _ => throw new NotSupportedException(name)
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
            AccessTools.Field(typeof(VRPGModSystem), "dataRegistry").SetValue(System, data);
            AccessTools.Field(typeof(VRPGModSystem), "serverChannel").SetValue(System, channel);
            AccessTools.Field(typeof(VRPGModSystem), "libraryIndex").SetValue(System, LibraryIndex.Build(data));
            if (injectFallbackClock)
            {
                UseFallbackClock(Module);
                UseFallbackClock(System);
            }
            AssertSharedStore();
        }

        private void UseFallbackClock(object owner)
        {
            var field = AccessTools.Field(owner.GetType(), "unavailableSkillNotifications");
            var constructor = field.FieldType.GetConstructor(new[] { typeof(Func<long>) });
            Assert.NotNull(constructor);
            var notifications = constructor.Invoke(new object[] { (Func<long>)(() => Now) });
            field.SetValue(owner, notifications);
            Assert.Same(notifications, field.GetValue(owner));
        }

        public void AssertSharedStore()
        {
            Assert.Same(Store, Field<RpgPlayerStore>(Module, "playerStore"));
            Assert.Same(Store, Field<RpgPlayerStore>(Skills, "playerStore"));
            Assert.Same(Store, Field<RpgPlayerStore>(Resources, "playerStore"));
            Assert.Same(Store, Field<RpgPlayerStore>(Notifications, "playerStore"));
        }

        public void Equip(string code, IServerPlayer? player = null) => AccessTools.Method(typeof(VRPGModSystem), "HandleSkillEquipRequest").Invoke(System,
            new object[] { api, player ?? Player, new SkillEquipRequestPacket { Slot = 2, SkillCode = code } });
        public void Cast(bool pressed, IServerPlayer? player = null) => AccessTools.Method(typeof(VRPGModSystem), "HandleSkillCastRequest").Invoke(System,
            new object[] { player ?? Player, new SkillCastRequestPacket { Slot = 0, Pressed = pressed } });
        public void ConfigureFeedback(string path)
        {
            if (path == "module") AccessTools.Field(typeof(RpgModule), "failureNotifications").SetValue(Module, null);
            if (path != "disabled") return;
            System.Config.Modules.Rpg.Enabled = false;
            System.Config.Modules.Rpg.InventoryReplacement.Enabled = false;
            System.Config.Modules.Dungeons.Enabled = false;
            System.StartServerSide(api);
            Assert.Null(Field<RpgModule?>(System, "rpgModule"));
        }
        public void Feedback(string path, IServerPlayer player)
        {
            if (path == "disabled") Cast(true, player);
            else Module.NotifyAbilityFailure(player, AbilityFailureKind.Other, "unused");
        }
        public void FeedbackBurst(string path, IServerPlayer player)
        {
            Feedback(path, player);
            if (path == "disabled") { Cast(false, player); Equip("test", player); }
            else { Feedback(path, player); Feedback(path, player); }
        }
        public void Raise(string name, IServerPlayer player)
        {
            Assert.True(EventHandlers.TryGetValue(name, out var handler), "No " + name + " cleanup handler was registered.");
            Assert.IsAssignableFrom<Delegate>(handler).DynamicInvoke(player);
        }
        public void Dispose() { System.Dispose(); Module.Dispose(); }
    }

    private static T Field<T>(object owner, string name) => (T)AccessTools.Field(owner.GetType(), name).GetValue(owner)!;

    private sealed class Storage : IRpgPlayerStateStorage
    {
        public int Writes;
        public byte[]? Bytes;
        public byte[]? Read() => Bytes;
        public void Write(byte[] bytes) { Writes++; Bytes = bytes.ToArray(); }
    }

    private sealed class TestPlayer(string uid = "p") : Vintagestory.Server.ServerPlayer(null!,
        new Vintagestory.Server.ServerWorldPlayerData { EntityPlayer = new EntityPlayer() }), IServerPlayer
    {
        public readonly List<string> Messages = new();
        public override string PlayerUID => uid;
        public override bool HasPrivilege(string privilegeCode) => true;
        void IServerPlayer.SendMessage(int groupId, string message, EnumChatType chatType, string data) => Messages.Add(message);
        protected override void Init() { }
    }
}
