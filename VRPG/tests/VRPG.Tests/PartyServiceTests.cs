using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json;
using VRPG.Parties.Persistence;
using VRPG.Parties;
using Xunit;

namespace VRPG.Tests;

public sealed class PartyServiceTests
{
    [Fact]
    public void CreateJoinAndLeaderActionsUseStableUids()
    {
        var f = new PartyTestFixture();
        string id = f.Create("leader");
        f.Join("leader", "member");
        Assert.Equal("leader", f.Service.Read("member", f.Now).LeaderUid);
        Assert.Equal(id, f.Service.Read("member", f.Now).PartyId);
        Assert.Equal("leader-required", f.Act("member", PartyAction.Invite, "third").Code);
        Assert.Equal("leader-required", f.Act("member", PartyAction.Remove, "leader").Code);
        Assert.Equal("leader-required", f.Act("member", PartyAction.Disband).Code);
        Assert.Equal("transfer-required", f.Act("leader", PartyAction.Leave).Code);
        Assert.True(f.Act("leader", PartyAction.TransferLeader, "member").Success);
        Assert.True(f.Act("leader", PartyAction.Leave).Success);
        Assert.Equal("member", f.Service.Read("member", f.Now).LeaderUid);
        Assert.True(f.Act("member", PartyAction.Leave).Success);
        Assert.Null(f.Service.Read("member", f.Now).PartyId);
        Assert.NotEqual(id, f.Create("leader"));
    }

    [Fact]
    public void InviteRequiresOnlineSoloTargetAndBoundsBothSides()
    {
        var f = new PartyTestFixture();
        f.Create("leader");
        Assert.Equal("target-offline", f.Act("leader", PartyAction.Invite, "offline").Code);
        for (int i = 0; i < 8; i++) f.Invite("leader", "target" + i);
        f.Online.Add("ninth");
        Assert.Equal("invitation-limit", f.Act("leader", PartyAction.Invite, "ninth").Code);
        Assert.Equal("already-invited", f.Act("leader", PartyAction.Invite, "target0").Code);
        var g = new PartyTestFixture();
        for (int i = 0; i < 8; i++) { g.Create("leader" + i); g.Invite("leader" + i, "recipient"); }
        g.Create("leader8");
        Assert.Equal("invitation-limit", g.Act("leader8", PartyAction.Invite, "recipient").Code);
        g.Create("taken"); g.Online.Add("taken");
        Assert.Equal("already-member", g.Act("leader8", PartyAction.Invite, "taken").Code);
    }

    [Fact]
    public void InvitationsExpireDeclineAndCannotBeAcceptedByAnotherUid()
    {
        var f = new PartyTestFixture(); f.Create("leader");
        string first = f.Invite("leader", "member");
        Assert.False(f.Act("impostor", PartyAction.Accept, invitation: first).Success);
        Assert.True(f.Act("member", PartyAction.Decline, invitation: first).Success);
        Assert.False(f.Act("member", PartyAction.Accept, invitation: first).Success);
        string second = f.Invite("leader", "member");
        f.Now += TimeSpan.FromMinutes(5);
        Assert.Empty(f.Service.Read("member", f.Now).IncomingInvitations);
        int writes = f.Repository.Writes;
        Assert.Equal("invitation-expired", f.Act("member", PartyAction.Accept, invitation: second).Code);
        Assert.Equal(writes, f.Repository.Writes);
        f.Invite("leader", "member");
    }

    [Fact]
    public void InvitationsDoNotReserveSeatsAndAcceptanceRechecksMembership()
    {
        var f = new PartyTestFixture(); f.Create("leader");
        for (int i = 0; i < 6; i++) f.Join("leader", "m" + i);
        string a = f.Invite("leader", "a"), b = f.Invite("leader", "b");
        Assert.True(f.Act("a", PartyAction.Accept, invitation: a).Success);
        Assert.Equal("party-full", f.Act("b", PartyAction.Accept, invitation: b).Code);
        Assert.Equal(8, f.Service.Read("leader", f.Now).Members.Count);
        var g = new PartyTestFixture(); g.Create("one"); g.Create("two");
        string one = g.Invite("one", "member"), two = g.Invite("two", "member");
        Assert.True(g.Act("member", PartyAction.Accept, invitation: one).Success);
        Assert.False(g.Act("member", PartyAction.Accept, invitation: two).Success);
        Assert.Equal(g.Service.Read("one", g.Now).PartyId, g.Service.Read("member", g.Now).PartyId);
    }

    [Fact]
    public void TransferDisbandAndRemoveInvalidateTheRightAuthority()
    {
        var f = new PartyTestFixture(); string id = f.Create("leader"); f.Join("leader", "member");
        string invitation = f.Invite("leader", "pending");
        Assert.True(f.Act("leader", PartyAction.TransferLeader, "member").Success);
        Assert.False(f.Act("pending", PartyAction.Accept, invitation: invitation).Success);
        Assert.False(f.Act("member", PartyAction.Remove, "member").Success);
        Assert.True(f.Act("member", PartyAction.Remove, "leader").Success);
        Assert.Null(f.Service.Read("leader", f.Now).PartyId);
        Assert.True(f.Act("member", PartyAction.Disband).Success);
        Assert.NotEqual(id, f.Create("member"));
    }

    [Fact]
    public void ReplayIsIdempotentPayloadBoundAndStaleAfterCacheEviction()
    {
        var f = new PartyTestFixture();
        var command = new PartyCommand("create", 0, PartyAction.Create);
        var created = f.Service.Execute("leader", command, f.Now);
        Assert.True(created.Success);
        int writes = f.Repository.Writes;
        Assert.True(f.Service.Execute("leader", command, f.Now).Success);
        Assert.Equal(writes, f.Repository.Writes);
        Assert.Equal("request-conflict", f.Service.Execute("leader", command with { Action = PartyAction.Disband }, f.Now).Code);
        Assert.Equal("stale-revision", f.Service.Execute("leader", new("old", 0, PartyAction.Disband), f.Now).Code);
        Assert.True(f.Act("leader", PartyAction.Disband).Success);
        for (int i = 0; i < 20; i++) { f.Create("leader"); Assert.True(f.Act("leader", PartyAction.Disband).Success); }
        Assert.Equal("stale-revision", f.Service.Execute("leader", command, f.Now).Code);
        Assert.Null(f.Service.Read("leader", f.Now).PartyId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SaveFailureHidesRosterUntilValidatedReload(bool afterWrite)
    {
        var f = new PartyTestFixture(); f.Create("leader"); f.Join("leader", "member");
        f.Repository.FailAfterWrite = afterWrite; f.Repository.FailBeforeWrite = !afterWrite;
        Assert.False(f.Act("leader", PartyAction.Remove, "member").Success);
        var view = f.Service.Read("member", f.Now);
        Assert.Equal(PartyHealth.Unavailable, view.Health);
        Assert.Empty(view.Members);
        f.Repository.FailAfterWrite = f.Repository.FailBeforeWrite = false;
        Assert.Equal(PartyHealth.Healthy, f.Service.Load());
        Assert.Equal(afterWrite, f.Service.Read("member", f.Now).PartyId == null);
    }

    [Fact]
    public async Task ConcurrentAcceptancesCannotTakeTheLastSeatTwice()
    {
        var f = new PartyTestFixture(); f.Create("leader");
        for (int i = 0; i < 6; i++) f.Join("leader", "member" + i);
        string a = f.Invite("leader", "a"), b = f.Invite("leader", "b");
        long revision = f.Service.Read("leader", f.Now).Revision;
        var results = await Task.WhenAll(
            Task.Run(() => f.Service.Execute("a", new("race-a", revision, PartyAction.Accept, InvitationId: a), f.Now)),
            Task.Run(() => f.Service.Execute("b", new("race-b", revision, PartyAction.Accept, InvitationId: b), f.Now)));
        Assert.Single(results, r => r.Success);
        Assert.Single(results, r => r.Code == "stale-revision");
        string loser = results[0].Success ? "b" : "a", invitation = results[0].Success ? b : a;
        Assert.Equal("party-full", f.Act(loser, PartyAction.Accept, invitation: invitation).Code);
        Assert.Equal(8, f.Service.Read("leader", f.Now).Members.Count);
    }

    [Fact]
    public void ViewsAndCallerCommandsCannotMutatePublishedMembership()
    {
        var f = new PartyTestFixture(); f.Create("leader");
        var view = f.Service.Read("leader", f.Now);
        Assert.Throws<NotSupportedException>(() => ((IList<PartyMemberView>)view.Members).Clear());
        f.Join("leader", "member");
        Assert.Single(view.Members);
        Assert.Equal(2, f.Service.Read("leader", f.Now).Members.Count);
        Assert.Empty(f.Service.Read("stranger", f.Now).Members);
    }

    [Fact]
    public void OverflowCannotWrapRevisionAndRestoreOldRequestAuthority()
    {
        var f = new PartyTestFixture();
        f.Repository.Bytes = JsonConvert.SerializeObject(new PartyArchive { Generation = long.MaxValue });
        Assert.Equal(PartyHealth.Healthy, f.Service.Load());
        int writes = f.Repository.Writes;
        Assert.False(f.Act("leader", PartyAction.Create).Success);
        Assert.Equal(writes, f.Repository.Writes);
        Assert.Equal(PartyHealth.Unavailable, f.Service.Read("leader", f.Now).Health);
    }

    [Fact]
    public void ReplaySurvivesRestartAndAlwaysReturnsCurrentAuthorizedView()
    {
        var f = new PartyTestFixture();
        var create = new PartyCommand("create", 0, PartyAction.Create);
        Assert.True(f.Service.Execute("leader", create, f.Now).Success);
        Assert.True(f.Act("leader", PartyAction.Disband).Success);
        var restarted = new PartyService(f.Repository, _ => true); restarted.Load();
        int writes = f.Repository.Writes;
        var replay = restarted.Execute("leader", create, f.Now);
        Assert.True(replay.Success); Assert.Null(replay.View.PartyId);
        Assert.Equal(writes, f.Repository.Writes);
    }

    [Fact]
    public void FailedReadLatchesUnavailableUntilExplicitReload()
    {
        var f = new PartyTestFixture(); f.Create("leader");
        f.Repository.FailLoad = true;
        Assert.Equal(PartyHealth.Unavailable, f.Service.Read("leader", f.Now).Health);
        f.Repository.FailLoad = false;
        Assert.Equal(PartyHealth.Unavailable, f.Service.Read("leader", f.Now).Health);
        Assert.Equal(PartyHealth.Healthy, f.Service.Load());
        Assert.Equal("leader", f.Service.Read("leader", f.Now).LeaderUid);
    }

    [Fact]
    public void StartupPreservesMembershipAndClearsInvitesAndPresenceIsNotMembership()
    {
        var f = new PartyTestFixture(); string id = f.Create("leader"); f.Join("leader", "member");
        f.Invite("leader", "pending");
        f.Online.Clear();
        var restarted = new PartyService(f.Repository, f.Online.Contains);
        Assert.Equal(PartyHealth.Healthy, restarted.Load());
        var view = restarted.Read("member", f.Now);
        Assert.Equal(id, view.PartyId); Assert.Equal("leader", view.LeaderUid);
        Assert.All(view.Members, m => Assert.False(m.IsOnline));
        Assert.Empty(restarted.Read("pending", f.Now).IncomingInvitations);
    }

    [Theory]
    [InlineData("", "valid", 0)]
    [InlineData("actor", "", 0)]
    [InlineData("actor", "valid", -1)]
    public void InvalidCommandsNeverWrite(string actor, string request, long revision)
    {
        var f = new PartyTestFixture(); int writes = f.Repository.Writes;
        Assert.False(f.Service.Execute(actor, new(request, revision, PartyAction.Create), f.Now).Success);
        Assert.False(f.Service.Execute("actor", new("bad", 0, (PartyAction)999), f.Now).Success);
        Assert.False(f.Service.Execute("actor", new(new string('x', 129), 0, PartyAction.Create), f.Now).Success);
        Assert.Equal(writes, f.Repository.Writes);
    }
}
