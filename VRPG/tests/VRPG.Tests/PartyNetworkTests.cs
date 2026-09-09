using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using VRPG.Network;
using VRPG.Parties;
using VRPG.Parties.Networking;
using VRPG.Parties.Persistence;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;
using Vintagestory.Server;
using Xunit;

namespace VRPG.Tests;

public sealed class PartyNetworkTests
{
    [Fact]
    public void ThrottledNewerQueryStillRetiresEarlierQuerySequence()
    {
        using var f = new PartyTransportFixture();
        string a = f.Handshake(f.A), b = f.Handshake(f.B);
        f.Mutate(f.B, b, "create", PartyAction.Create);
        for (int i = 0; i < 4; i++) f.Query(f.A, a, "fill" + i, search: "current");
        f.Deliver(f.A, new PartyQueryPacket { SessionId = a, RequestId = "new", QuerySequence = 100, Search = "blocked" });
        Assert.Equal("rate-limit", f.Last<PartySnapshotPacket>(f.A).Code);
        f.Seconds += 1;
        f.Deliver(f.A, new PartyQueryPacket { SessionId = a, RequestId = "old", QuerySequence = 99, Search = "old" });
        f.Mutate(f.B, b, "invite", PartyAction.Invite, "a");
        Assert.Equal("current", f.Last<PartySnapshotPacket>(f.A).Search);
    }

    [Fact]
    public void ReconnectingCannotBypassInviterCooldownButConnectionRequestBucketResets()
    {
        using var f = new PartyTransportFixture();
        string oldSession = f.Handshake(f.A);
        f.Mutate(f.A, oldSession, "create", PartyAction.Create);
        f.Mutate(f.A, oldSession, "invite-b", PartyAction.Invite, "b");
        f.Network.Disconnect(f.A);
        f.Network.Connect(f.A);
        string freshSession = f.Handshake(f.A);
        f.Mutate(f.A, freshSession, "invite-c", PartyAction.Invite, "c");
        Assert.Equal("invite-rate-limit", f.Last<PartyResultPacket>(f.A).Code);
        f.Seconds += 2;
        f.Mutate(f.A, freshSession, "invite-c-later", PartyAction.Invite, "c");
        Assert.True(f.Last<PartyResultPacket>(f.A).Success);
    }

    [Fact]
    public void TargetPagesUseNativeOnlineNamesWithoutResolvingOtherPlayerData()
    {
        using var f = new PartyTransportFixture();
        string session = f.Handshake(f.A);
        f.Query(f.A, session, "targets", includeTargets: true);
        Assert.Equal(new[] { "Name b", "Name c" }, f.Last<PartySnapshotPacket>(f.A).Targets.Select(t => t.DisplayName));
        Assert.Empty(f.NameLookups);
        f.Mutate(f.A, session, "create", PartyAction.Create);
        Assert.All(f.NameLookups, uid => Assert.Equal("a", uid));
    }

    [Theory]
    [InlineData(EnumClientState.Connecting, false)]
    [InlineData(EnumClientState.Admitted, false)]
    [InlineData(EnumClientState.Queued, false)]
    [InlineData(EnumClientState.Connected, true)]
    [InlineData(EnumClientState.Playing, true)]
    public void InvitationEligibilityUsesLoadedNativeConnectionStates(EnumClientState state, bool eligible)
    {
        using var f = new PartyTransportFixture();
        string a = f.Handshake(f.A);
        f.Mutate(f.A, a, "create", PartyAction.Create);
        f.C.State = state;
        f.Mutate(f.A, a, "invite", PartyAction.Invite, "c");
        Assert.Equal(eligible, f.Last<PartyResultPacket>(f.A).Success);
        Assert.Equal(eligible ? "ok" : "target-offline", f.Last<PartyResultPacket>(f.A).Code);
    }

    [Fact]
    public void DelayedQueryCannotChangeSearchUsedByLaterInvalidations()
    {
        using var f = new PartyTransportFixture();
        string a = f.Handshake(f.A), b = f.Handshake(f.B);
        f.Mutate(f.B, b, "create", PartyAction.Create);
        f.Deliver(f.A, new PartyQueryPacket { SessionId = a, RequestId = "new", QuerySequence = 100, Search = "new", IncludeTargets = true });
        f.Deliver(f.A, new PartyQueryPacket { SessionId = a, RequestId = "old", QuerySequence = 99, Search = "old", IncludeTargets = true });
        f.Mutate(f.B, b, "invite", PartyAction.Invite, "a");
        Assert.Equal("new", f.Last<PartySnapshotPacket>(f.A).Search);
    }

    [Theory]
    [InlineData(PartyAction.Disband)]
    [InlineData(PartyAction.TransferLeader)]
    public void RetiringInvitationAuthorityRefreshesRecipientsWithoutLeakingOtherParties(PartyAction action)
    {
        using var f = new PartyTransportFixture();
        string a = f.Handshake(f.A), b = f.Handshake(f.B);
        f.Handshake(f.C);
        f.Mutate(f.A, a, "create", PartyAction.Create);
        f.Mutate(f.A, a, "invite-b", PartyAction.Invite, "b");
        string invitation = Assert.Single(f.Last<PartySnapshotPacket>(f.B).IncomingInvitations).InvitationId;
        f.Mutate(f.B, b, "accept", PartyAction.Accept, invitation: invitation);
        f.Seconds += 2;
        f.Mutate(f.A, a, "invite-c", PartyAction.Invite, "c");
        Assert.Single(f.Last<PartySnapshotPacket>(f.C).IncomingInvitations);
        f.Mutate(f.A, a, "retire", action, action == PartyAction.TransferLeader ? "b" : "");
        Assert.True(f.Last<PartyResultPacket>(f.A).Success);
        Assert.Empty(f.Last<PartySnapshotPacket>(f.C).IncomingInvitations);
        Assert.Empty(f.Last<PartySnapshotPacket>(f.C).Members);
    }

    [Fact]
    public void ExpiredInvitationsInvalidateOnlyTheirSubscribedRecipients()
    {
        using var f = new PartyTransportFixture();
        string a = f.Handshake(f.A);
        f.Handshake(f.B);
        f.Handshake(f.C);
        f.Mutate(f.A, a, "create", PartyAction.Create);
        f.Mutate(f.A, a, "invite", PartyAction.Invite, "b");
        f.Sent.Clear();
        f.Now += TimeSpan.FromMinutes(6);
        f.Network.Tick();
        Assert.Empty(f.Last<PartySnapshotPacket>(f.B).IncomingInvitations);
        Assert.Single(f.Sent);
    }

    [Fact]
    public void RegisteredTypesMatchBothSidesAndAuthenticatedHandlerPersistsOnlyCallersParty()
    {
        using var f = new PartyTransportFixture();
        Assert.Equal(f.ServerTypes, f.ClientTypes);
        Assert.Contains(typeof(PartyMutationPacket), f.ServerTypes);
        string sessionA = f.Handshake(f.A), sessionB = f.Handshake(f.B);
        f.Mutate(f.B, sessionB, "create-b", PartyAction.Create);
        Assert.True(f.Last<PartyResultPacket>(f.B).Success);
        byte[] before = File.ReadAllBytes(f.Repository.ArchivePath);
        f.Mutate(f.A, sessionB, "wrong-session", PartyAction.Disband);
        Assert.Equal(before, File.ReadAllBytes(f.Repository.ArchivePath));
        f.Mutate(f.A, sessionA, "create-a", PartyAction.Create);
        Assert.True(f.Last<PartyResultPacket>(f.A).Success);
        Assert.NotEqual(f.Service.Read("a", f.Now).PartyId, f.Service.Read("b", f.Now).PartyId);
        f.Mutate(f.A, sessionA, "remove-b", PartyAction.Remove, "b");
        Assert.Equal("invalid-target", f.Last<PartyResultPacket>(f.A).Code);
        Assert.Single(f.Service.Read("b", f.Now).Members);
    }

    [Fact]
    public void BurstAndInvitationAttemptsAreLimitedAndReplacementDisconnectCannotRevokeNewSession()
    {
        using var f = new PartyTransportFixture();
        string session = f.Handshake(f.A);
        for (int i = 0; i < 4; i++) f.Query(f.A, session, "q" + i);
        f.Query(f.A, session, "limited");
        Assert.Equal("rate-limit", f.Last<PartySnapshotPacket>(f.A).Code);
        f.Seconds += .2;
        f.Mutate(f.A, session, "create", PartyAction.Create);
        Assert.True(f.Last<PartyResultPacket>(f.A).Success);
        f.Seconds += 1;
        f.Mutate(f.A, session, "invite", PartyAction.Invite, "b");
        Assert.True(f.Last<PartyResultPacket>(f.A).Success);
        f.Mutate(f.A, session, "invite-fast", PartyAction.Invite, "c");
        Assert.Equal("invite-rate-limit", f.Last<PartyResultPacket>(f.A).Code);
        var replacement = new PartyTransportFixture.Player("a");
        f.Network.Connect(replacement);
        string replacementSession = f.Handshake(replacement);
        Assert.NotEqual(session, replacementSession);
        f.Network.Disconnect(f.A);
        f.Query(replacement, replacementSession, "new");
        Assert.Equal(PartyHealth.Healthy, f.Last<PartySnapshotPacket>(replacement).Health);
        byte[] before = File.ReadAllBytes(f.Repository.ArchivePath);
        f.Mutate(f.A, session, "delayed-old", PartyAction.Disband);
        Assert.Equal(before, File.ReadAllBytes(f.Repository.ArchivePath));
    }

    [Fact]
    public void InvitesAndRosterChangesInvalidateOnlyAffectedConnections()
    {
        using var f = new PartyTransportFixture();
        string a = f.Handshake(f.A), b = f.Handshake(f.B);
        f.Handshake(f.C);
        f.Mutate(f.A, a, "create", PartyAction.Create);
        f.Sent.Clear();
        f.Mutate(f.A, a, "invite", PartyAction.Invite, "b");
        var invited = f.Last<PartySnapshotPacket>(f.B);
        string invitation = Assert.Single(invited.IncomingInvitations).InvitationId;
        Assert.DoesNotContain(f.Sent, x => ReferenceEquals(x.Player, f.C));
        f.Mutate(f.B, b, "accept", PartyAction.Accept, invitation: invitation);
        Assert.Equal(2, f.Last<PartySnapshotPacket>(f.A).Members.Length);
        Assert.DoesNotContain(f.Sent, x => ReferenceEquals(x.Player, f.C));
        f.Network.Disconnect(f.B);
        Assert.False(Assert.Single(f.Last<PartySnapshotPacket>(f.A).Members, x => x.Uid == "b").IsOnline);
    }

    [Fact]
    public void TargetPageIsBoundedAndUnloadedTargetsAreRefusedWithoutLookingUpUnrelatedRosters()
    {
        using var f = new PartyTransportFixture();
        for (int i = 0; i < 20; i++)
        {
            var player = new PartyTransportFixture.Player("target" + i);
            f.Players.Add(player);
            f.Network.Connect(player);
        }
        f.C.State = EnumClientState.Connecting;
        string a = f.Handshake(f.A);
        f.Query(f.A, a, "search", includeTargets: true, search: "target");
        var page = f.Last<PartySnapshotPacket>(f.A);
        Assert.Equal(16, page.Targets.Length);
        Assert.True(page.HasMoreTargets);
        Assert.All(page.Targets, x => Assert.StartsWith("target", x.Uid));
        f.Mutate(f.A, a, "create", PartyAction.Create);
        f.Mutate(f.A, a, "unloaded", PartyAction.Invite, "c");
        Assert.Equal("target-offline", f.Last<PartyResultPacket>(f.A).Code);
        Assert.Empty(f.Service.Read("c", f.Now).IncomingInvitations);
    }

    [Fact]
    public void MalformedRequestsAndWrongPartyCorrelationDoNotWriteAndDisposedHandlersAreInert()
    {
        using var f = new PartyTransportFixture();
        string a = f.Handshake(f.A);
        f.Mutate(f.A, a, "create", PartyAction.Create);
        var before = File.ReadAllBytes(f.Repository.ArchivePath);
        f.Deliver(f.A, new PartyMutationPacket { SessionId = a, RequestId = new string('r', 65), Action = PartyAction.Disband });
        f.Deliver(f.A, new PartyMutationPacket { SessionId = a, RequestId = "wrong-party", PartyId = Guid.NewGuid().ToString("N"),
            ExpectedRevision = f.Service.Read("a", f.Now).Revision, Action = PartyAction.Disband });
        Assert.Equal("stale-party", f.Last<PartyResultPacket>(f.A).Code);
        f.Query(f.A, a, "oversized-search", search: new string('s', 65));
        Assert.Equal("invalid-query", f.Last<PartySnapshotPacket>(f.A).Code);
        Assert.Equal(before, File.ReadAllBytes(f.Repository.ArchivePath));
        f.Network.Dispose();
        int count = f.Sent.Count;
        f.Query(f.A, a, "after-dispose");
        f.Mutate(f.A, a, "after-dispose", PartyAction.Disband);
        Assert.Equal(count, f.Sent.Count);
        Assert.Equal(before, File.ReadAllBytes(f.Repository.ArchivePath));
    }
}

internal sealed class PartyTransportFixture : IDisposable
{
    public readonly string Root = Path.Combine(Path.GetTempPath(), "vrpg-party-network-" + Guid.NewGuid().ToString("N"));
    public DateTimeOffset Now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);
    public double Seconds;
    private long querySequence;
    public readonly Player A = new("a"), B = new("b"), C = new("c");
    public readonly List<IServerPlayer> Players = new();
    public readonly List<(IServerPlayer Player, object Packet)> Sent = new();
    public readonly List<object> ClientSent = new();
    public readonly List<Type> ServerTypes = new(), ClientTypes = new();
    public readonly Dictionary<Type, Delegate> ServerHandlers = new(), ClientHandlers = new();
    public readonly Dictionary<string, Delegate?> Events = new();
    public string WorldIdentity = "module-world";
    public readonly List<string> NameLookups = new();
    public readonly WorldScopedPartyRepository Repository;
    public readonly PartyService Service;
    public readonly PartyNetworkService Network;
    public readonly IServerNetworkChannel ServerChannel;
    public readonly IClientNetworkChannel ClientChannel;
    public readonly ICoreServerAPI Api;
    public readonly ICoreClientAPI ClientApi;

    public PartyTransportFixture()
    {
        Directory.CreateDirectory(Root);
        Players.AddRange(new[] { A, B, C });
        Repository = new(Root, "world-one");
        Service = new(Repository, uid => Players.Any(p => p.PlayerUID == uid && p.ConnectionState is EnumClientState.Connected or EnumClientState.Playing));
        Assert.Equal(PartyHealth.Healthy, Service.Load());
        IServerNetworkChannel? server = null;
        server = Proxy.Create<IServerNetworkChannel>((m, args) =>
        {
            if (m.Name == "RegisterMessageType") ServerTypes.Add(m.GetGenericArguments()[0]);
            else if (m.Name == "SetMessageHandler") ServerHandlers[m.GetGenericArguments()[0]] = (Delegate)args![0]!;
            else if (m.Name == "SendPacket") { foreach (var p in (IServerPlayer[])args![1]!) Sent.Add((p, args[0]!)); return null; }
            else throw new NotSupportedException(m.Name);
            return server;
        });
        IClientNetworkChannel? client = null;
        client = Proxy.Create<IClientNetworkChannel>((m, args) =>
        {
            if (m.Name == "RegisterMessageType") ClientTypes.Add(m.GetGenericArguments()[0]);
            else if (m.Name == "SetMessageHandler") ClientHandlers[m.GetGenericArguments()[0]] = (Delegate)args![0]!;
            else if (m.Name == "SendPacket") { ClientSent.Add(args![0]!); return null; }
            else if (m.Name == "get_Connected") return true;
            else throw new NotSupportedException(m.Name);
            return client;
        });
        var events = Proxy.Create<IServerEventAPI>(EventCall);
        var world = Proxy.Create<IServerWorldAccessor>((m, _) => m.Name switch
        {
            "get_AllOnlinePlayers" => Players.Cast<IPlayer>().ToArray(),
            _ => throw new NotSupportedException(m.Name)
        });
        var network = Proxy.Create<IServerNetworkAPI>((_, _) => server);
        var save = Proxy.Create<ISaveGame>((m, _) => m.Name == "get_SavegameIdentifier" ? WorldIdentity : throw new NotSupportedException(m.Name));
        var manager = Proxy.Create<IWorldManagerAPI>((m, _) => m.Name == "get_SaveGame" ? save : throw new NotSupportedException(m.Name));
        var playerData = Proxy.Create<IPlayerDataManager>((m, args) =>
        {
            if (m.Name != "GetPlayerDataByUid") throw new NotSupportedException(m.Name);
            string uid = (string)args![0]!;
            NameLookups.Add(uid);
            return Proxy.Create<IServerPlayerData>((method, _) => method.Name == "get_LastKnownPlayername" ? "Saved " + uid : throw new NotSupportedException(method.Name));
        });
        var logger = Proxy.Create<ILogger>((_, _) => null);
        Api = Proxy.Create<ICoreServerAPI>((m, args) =>
        {
            if (m.Name == "GetOrCreateDataPath") return Directory.CreateDirectory(Path.Combine(Root, (string)args![0]!)).FullName;
            return m.Name switch
            {
                "get_Network" => network, "get_Event" => events, "get_World" => world, "get_WorldManager" => manager,
                "get_PlayerData" => playerData, "get_Logger" => logger,
                _ => throw new NotSupportedException(m.Name)
            };
        });
        var clientEvents = Proxy.Create<IClientEventAPI>(EventCall);
        var clientNetwork = Proxy.Create<IClientNetworkAPI>((_, _) => client);
        ClientApi = Proxy.Create<ICoreClientAPI>((m, _) => m.Name switch
        {
            "get_Network" => clientNetwork, "get_Event" => clientEvents,
            _ => throw new NotSupportedException(m.Name)
        });
        ServerChannel = VRPGNetwork.RegisterServer(Api);
        ClientChannel = VRPGNetwork.RegisterClient(ClientApi);
        Network = new(() => Service, () => Players, uid => { NameLookups.Add(uid); return "Name " + uid; }, () => Now, () => Seconds);
        Network.Attach(ServerChannel);
        foreach (var player in Players) Network.Connect(player);
    }

    private object? EventCall(MethodInfo method, object?[]? args)
    {
        if (method.Name == "RegisterGameTickListener") { Events["Tick"] = (Delegate)args![0]!; return 1L; }
        if (method.Name == "UnregisterGameTickListener") { Events["Tick"] = null; return null; }
        string key = method.Name[(method.Name.IndexOf('_') + 1)..];
        Events.TryGetValue(key, out var current);
        if (method.Name.StartsWith("add_", StringComparison.Ordinal)) Events[key] = Delegate.Combine(current, (Delegate)args![0]!);
        else if (method.Name.StartsWith("remove_", StringComparison.Ordinal)) Events[key] = Delegate.Remove(current, (Delegate)args![0]!);
        else throw new NotSupportedException(method.Name);
        return null;
    }

    public void Deliver<T>(IServerPlayer player, T packet) => Assert.Contains(typeof(T), ServerHandlers).DynamicInvoke(player, packet);
    public void Receive<T>(T packet) => Assert.Contains(typeof(T), ClientHandlers).DynamicInvoke(packet);
    public T Last<T>(IServerPlayer player) => (T)Sent.Last(x => ReferenceEquals(x.Player, player) && x.Packet is T).Packet;
    public string Handshake(IServerPlayer player)
    {
        Query(player, "", Guid.NewGuid().ToString("N"));
        return Last<PartySessionPacket>(player).SessionId;
    }
    public void Query(IServerPlayer player, string session, string request, string search = "", bool includeTargets = false) =>
        Deliver(player, new PartyQueryPacket { SessionId = session, RequestId = request, Search = search, IncludeTargets = includeTargets,
            QuerySequence = ++querySequence });
    public void Mutate(IServerPlayer player, string session, string request, PartyAction action, string target = "", string invitation = "")
    {
        var view = Service.Read(player.PlayerUID, Now);
        Deliver(player, new PartyMutationPacket { SessionId = session, RequestId = request, PartyId = view.PartyId ?? "",
            ExpectedRevision = view.Revision, Action = action, TargetUid = target, InvitationId = invitation });
    }
    public void Dispose() { Network.Dispose(); Repository.Dispose(); Directory.Delete(Root, true); }

    internal sealed class Player(string uid) : ServerPlayer(null!, new ServerWorldPlayerData { EntityPlayer = new EntityPlayer() }), IServerPlayer
    {
        public EnumClientState State = EnumClientState.Playing;
        public override string PlayerUID => uid;
        public override EnumClientState ConnectionState => State;
        string IPlayer.PlayerName => "Name " + uid;
        protected override void Init() { }
    }

    public class Proxy : DispatchProxy
    {
        public System.Func<MethodInfo, object?[]?, object?> InvokeCall = null!;
        public static T Create<T>(System.Func<MethodInfo, object?[]?, object?> call) where T : class
        {
            T proxy = Create<T, Proxy>();
            ((Proxy)(object)proxy).InvokeCall = call;
            return proxy;
        }
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => InvokeCall(targetMethod!, args);
    }
}
