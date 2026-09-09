using System;
using System.Linq;
using ProtoBuf;
using VRPG.Client.PlayerMenu.Party;
using VRPG.Network;
using VRPG.Parties;
using Xunit;

namespace VRPG.Tests;

public sealed class PartyClientServiceTests
{
    [Fact]
    public void DelayedHandshakesAndSnapshotsCannotReviveResetOrSupersededQueries()
    {
        using var f = new PartyTransportFixture();
        using var client = new PartyClientService(f.ClientChannel, f.ClientApi.Event);
        client.RequestView();
        var old = Assert.IsType<PartyQueryPacket>(f.ClientSent.Last());
        client.ResetConnection();
        client.RequestView();
        var current = Assert.IsType<PartyQueryPacket>(f.ClientSent.Last());
        f.Receive(new PartySessionPacket { RequestId = old.RequestId, SessionId = "old-session" });
        f.Receive(Snapshot("old-session", old.RequestId, 100, 100));
        Assert.Null(client.View);
        f.Receive(new PartySessionPacket { RequestId = current.RequestId, SessionId = "new-session" });
        f.Receive(Snapshot("new-session", current.RequestId, 1, 2));
        Assert.Equal(2, client.View!.Revision);
        client.RequestView("new search");
        var search = Assert.IsType<PartyQueryPacket>(f.ClientSent.Last());
        f.Receive(Snapshot("new-session", current.RequestId, 2, 100));
        Assert.Equal(2, client.View.Revision);
        f.Receive(Snapshot("new-session", search.RequestId, 3, 3));
        Assert.Equal(3, client.View.Revision);
        f.Events["LeaveWorld"]!.DynamicInvoke();
        Assert.Null(client.View);
        f.Receive(Snapshot("new-session", "", 4, 4));
        Assert.Null(client.View);
    }

    [Fact]
    public void PendingSurvivesMenuAbsenceAndOnlyItsResultCompletesItWithoutDowngradingNewerView()
    {
        using var f = new PartyTransportFixture();
        using var client = new PartyClientService(f.ClientChannel);
        Establish(f, client);
        var command = new PartyCommand("create", 2, PartyAction.Create);
        Assert.True(client.Send(command));
        Assert.Same(command, client.Pending);
        int changed = 0;
        void OnChanged() => changed++;
        client.Changed += OnChanged;
        client.Changed -= OnChanged; // Menu close unsubscribes; service keeps request lifetime.
        f.Receive(new PartyResultPacket { SessionId = "session", RequestId = "old-request", Success = true, Code = "ok" });
        Assert.Same(command, client.Pending);
        f.Receive(Snapshot("session", "", 3, 4));
        f.Receive(new PartyResultPacket { SessionId = "old-session", RequestId = "create", Success = true, Code = "ok" });
        Assert.Same(command, client.Pending);
        f.Receive(new PartyResultPacket { SessionId = "session", RequestId = "create", Success = true, Code = "ok",
            Snapshot = Snapshot("session", "create", 2, 3) });
        Assert.Null(client.Pending);
        Assert.Equal(4, client.View!.Revision);
        Assert.Equal(0, changed);
        Assert.False(client.Send(command));
    }

    [Theory]
    [InlineData("roster")]
    [InlineData("invites")]
    [InlineData("targets")]
    [InlineData("null-member")]
    [InlineData("oversize-name")]
    [InlineData("leader-outside-roster")]
    [InlineData("null-request")]
    public void MalformedSnapshotIsRejectedBeforeReplacingView(string kind)
    {
        using var f = new PartyTransportFixture();
        using var client = new PartyClientService(f.ClientChannel);
        Establish(f, client);
        var bad = Snapshot("session", "", 2, 3);
        switch (kind)
        {
            case "roster": bad.Members = new PartyMemberPacket[9]; break;
            case "invites": bad.IncomingInvitations = new PartyInvitationPacket[9]; break;
            case "targets": bad.Targets = new PartyTargetPacket[17]; break;
            case "null-member": bad.Members = new PartyMemberPacket[] { null! }; break;
            case "oversize-name": bad.Targets = new[] { new PartyTargetPacket { Uid = "b", DisplayName = new string('n', 129) } }; break;
            case "leader-outside-roster": bad.PartyId = Guid.NewGuid().ToString("N"); bad.LeaderUid = "b"; break;
            case "null-request": bad.RequestId = null!; break;
        }
        f.Receive(bad);
        Assert.Equal(2, client.View!.Revision);
    }

    [Fact]
    public void RealClientAndServerHandlersPersistAndResetWithoutRetainingPacketArrays()
    {
        using var f = new PartyTransportFixture();
        using var client = new PartyClientService(f.ClientChannel, f.ClientApi.Event);
        client.RequestView();
        f.Deliver(f.A, Assert.IsType<PartyQueryPacket>(f.ClientSent.Last()));
        f.Receive(f.Last<PartySessionPacket>(f.A));
        f.Receive(f.Last<PartySnapshotPacket>(f.A));
        Assert.True(client.Send(new("create", 0, PartyAction.Create)));
        f.Deliver(f.A, Serializer.DeepClone(Assert.IsType<PartyMutationPacket>(f.ClientSent.Last())));
        var result = Serializer.DeepClone(f.Last<PartyResultPacket>(f.A));
        f.Receive(result);
        Assert.Equal("a", Assert.Single(client.View!.Members).Uid);
        result.Snapshot!.Members[0].Uid = "tampered";
        Assert.Equal("a", Assert.Single(client.View.Members).Uid);
        Assert.Null(client.Pending);
        client.Dispose();
        Assert.Null(f.Events["LeaveWorld"]);
        f.Receive(result);
        Assert.Null(client.View);
    }

    private static void Establish(PartyTransportFixture f, PartyClientService client)
    {
        client.RequestView();
        string request = Assert.IsType<PartyQueryPacket>(f.ClientSent.Last()).RequestId;
        f.Receive(new PartySessionPacket { SessionId = "session", RequestId = request });
        f.Receive(Snapshot("session", request, 1, 2));
    }

    private static PartySnapshotPacket Snapshot(string session, string request, long sequence, long revision) => new()
        { SessionId = session, RequestId = request, Sequence = sequence, Revision = revision, Health = PartyHealth.Healthy, SelfUid = "a" };
}
