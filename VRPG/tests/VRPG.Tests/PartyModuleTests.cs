using System;
using System.IO;
using System.Linq;
using VRPG.Network;
using VRPG.Parties;
using VRPG.Parties.Persistence;
using Xunit;

namespace VRPG.Tests;

public sealed class PartyModuleTests
{
    [Fact]
    public void SaveGameLoadedStartsFreshAuthorityAndDisposalReleasesHandlersAndLease()
    {
        using var f = new PartyTransportFixture();
        f.Network.Dispose();
        using var module = new PartyModule(f.ServerChannel);
        module.StartServerSide(f.Api);
        Assert.Null(module.Service);
        f.Query(f.A, "", "before-load");
        Assert.Equal(PartyHealth.Unavailable, f.Last<PartySnapshotPacket>(f.A).Health);
        Assert.Contains("SaveGameLoaded", f.Events)!.DynamicInvoke();
        Assert.NotNull(module.Service);
        string session = f.Handshake(f.A);
        f.Deliver(f.A, new PartyMutationPacket { SessionId = session, RequestId = "create", Action = PartyAction.Create });
        Assert.True(f.Last<PartyResultPacket>(f.A).Success);
        Assert.Equal("Saved a", Assert.Single(f.Last<PartyResultPacket>(f.A).Snapshot!.Members).DisplayName);
        Assert.DoesNotContain("b", f.NameLookups);
        var priorService = module.Service;
        Assert.Equal(PartyHealth.Healthy, priorService.Read("a", f.Now).Health);
        module.Dispose();
        Assert.Null(module.Service);
        Assert.All(f.Events.Values, handler => Assert.Null(handler));
        int count = f.Sent.Count;
        f.Query(f.A, session, "after-disposal");
        Assert.Equal(count, f.Sent.Count);
        Assert.Equal(PartyHealth.Unavailable, priorService.Read("a", f.Now).Health);
        using var reopened = new WorldScopedPartyRepository(Path.Combine(f.Root, "VRPG"), "module-world");
        Assert.Equal(PartyRepositoryLoadStatus.Loaded, reopened.Load().Status);
    }

    [Fact]
    public void CorruptStartupRemainsUnavailableAndDoesNotReplaceStoredBytes()
    {
        using var f = new PartyTransportFixture();
        f.Network.Dispose();
        string root = Directory.CreateDirectory(Path.Combine(f.Root, "VRPG")).FullName;
        using var archive = new WorldScopedPartyRepository(root, "module-world");
        var service = new PartyService(archive, _ => true);
        service.Load();
        Assert.True(service.Execute("a", new("create", 0, PartyAction.Create), f.Now).Success);
        string path = archive.ArchivePath;
        archive.Dispose();
        File.WriteAllText(path, "broken party data");
        using var module = new PartyModule(f.ServerChannel);
        module.StartServerSide(f.Api);
        f.Events["SaveGameLoaded"]!.DynamicInvoke();
        string session = f.Handshake(f.A);
        Assert.Equal(PartyHealth.Unavailable, f.Last<PartySnapshotPacket>(f.A).Health);
        f.Deliver(f.A, new PartyMutationPacket { SessionId = session, RequestId = "cannot-create", Action = PartyAction.Create });
        Assert.Equal("unavailable", f.Last<PartyResultPacket>(f.A).Code);
        Assert.Equal("broken party data", File.ReadAllText(path));
    }

    [Fact]
    public void RejoinRestoresMembershipWithNewSessionAndDifferentWorldHasNoMembership()
    {
        using var f = new PartyTransportFixture();
        f.Network.Dispose();
        using var module = new PartyModule(f.ServerChannel);
        module.StartServerSide(f.Api);
        f.Events["SaveGameLoaded"]!.DynamicInvoke();
        string oldSession = f.Handshake(f.A);
        f.Deliver(f.A, new PartyMutationPacket { SessionId = oldSession, RequestId = "create", Action = PartyAction.Create });
        string party = f.Last<PartyResultPacket>(f.A).Snapshot!.PartyId;
        f.Events["PlayerDisconnect"]!.DynamicInvoke(f.A);
        f.Events["PlayerJoin"]!.DynamicInvoke(f.A);
        f.Events["PlayerNowPlaying"]!.DynamicInvoke(f.A);
        string rejoined = f.Handshake(f.A);
        Assert.NotEqual(oldSession, rejoined);
        Assert.Equal(party, f.Last<PartySnapshotPacket>(f.A).PartyId);
        module.Dispose();
        f.WorldIdentity = "another-world";
        using (var other = new PartyModule(f.ServerChannel))
        {
            other.StartServerSide(f.Api);
            f.Events["SaveGameLoaded"]!.DynamicInvoke();
            f.Handshake(f.A);
            Assert.Empty(f.Last<PartySnapshotPacket>(f.A).Members);
        }
        f.WorldIdentity = "module-world";
        using var returned = new PartyModule(f.ServerChannel);
        returned.StartServerSide(f.Api);
        f.Events["SaveGameLoaded"]!.DynamicInvoke();
        f.Handshake(f.A);
        Assert.Equal(party, f.Last<PartySnapshotPacket>(f.A).PartyId);
    }

    [Fact]
    public void RuntimeHealthLossHidesAllSubscribedViewsAndOrdinaryQueriesDoNotClearInvites()
    {
        using var f = new PartyTransportFixture();
        string a = f.Handshake(f.A), b = f.Handshake(f.B);
        f.Mutate(f.A, a, "create", PartyAction.Create);
        f.Mutate(f.A, a, "invite", PartyAction.Invite, "b");
        f.Query(f.B, b, "refresh");
        Assert.Single(f.Last<PartySnapshotPacket>(f.B).IncomingInvitations);
        File.WriteAllText(f.Repository.ArchivePath, "broken after startup");
        f.Query(f.A, a, "lost-health");
        Assert.Equal(PartyHealth.Unavailable, f.Last<PartySnapshotPacket>(f.A).Health);
        Assert.Equal(PartyHealth.Unavailable, f.Last<PartySnapshotPacket>(f.B).Health);
        Assert.Empty(f.Last<PartySnapshotPacket>(f.B).IncomingInvitations);
        Assert.Equal("broken after startup", File.ReadAllText(f.Repository.ArchivePath));
    }
}
