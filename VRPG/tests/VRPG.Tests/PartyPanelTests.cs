using System;
using System.Linq;
using VRPG.Client.PlayerMenu;
using VRPG.Client.PlayerMenu.Party;
using VRPG.Client.PlayerMenu.Inventory;
using VRPG.Network;
using VRPG.Parties;
using Xunit;

namespace VRPG.Tests;

public sealed class PartyPanelTests
{
    [Theory]
    [InlineData(PartyAction.Create, "create", "none", "")]
    [InlineData(PartyAction.Invite, "invite", "leader", "c")]
    [InlineData(PartyAction.Accept, "accept", "invited", "invite")]
    [InlineData(PartyAction.Decline, "decline", "invited", "invite")]
    [InlineData(PartyAction.Leave, "leave", "member", "a")]
    [InlineData(PartyAction.Remove, "remove", "leader", "b")]
    [InlineData(PartyAction.TransferLeader, "transfer", "leader", "b")]
    [InlineData(PartyAction.Disband, "disband", "leader", "a")]
    public void EachPermittedIntentSendsOneCurrentRevisionCommand(PartyAction action, string intent, string scenario, string selection)
    {
        using var f = new PartyPanelFixture(scenario);
        if (selection == "invite") selection = PartyPanelFixture.InvitationId;
        f.Panel.Select(selection);
        Assert.True(f.Panel.Handle(intent));
        if (action == PartyAction.Disband)
        {
            Assert.Null(f.Client.Pending);
            Assert.True(f.Panel.State.ConfirmDisband);
            f.Panel.Handle("confirm-disband");
        }
        var packet = Assert.IsType<PartyMutationPacket>(f.Transport.ClientSent.Last());
        Assert.Equal(action, packet.Action);
        Assert.Equal(7, packet.ExpectedRevision);
        Assert.Equal(action is PartyAction.Invite or PartyAction.Remove or PartyAction.TransferLeader ? selection : "", packet.TargetUid);
        Assert.Equal(action is PartyAction.Accept or PartyAction.Decline ? PartyPanelFixture.InvitationId : "", packet.InvitationId);
        int count = f.Transport.ClientSent.Count;
        f.Panel.Handle(intent);
        Assert.Equal(count, f.Transport.ClientSent.Count);
        Assert.Equal("party-pending", f.Panel.FeedbackKey);
    }

    [Theory]
    [InlineData("member", "remove", "b", "party-leader-required")]
    [InlineData("leader", "remove", "a", "party-invalid-target")]
    [InlineData("leader", "leave", "a", "party-transfer-required")]
    [InlineData("full", "invite", "c", "party-party-full")]
    [InlineData("unavailable", "create", "", "party-unavailable")]
    [InlineData("loading", "create", "", "party-loading")]
    public void InvalidActionsExplainRestrictionWithoutSending(string scenario, string intent, string selection, string reason)
    {
        using var f = new PartyPanelFixture(scenario);
        f.Panel.Select(selection);
        int count = f.Transport.ClientSent.Count;
        Assert.Equal(reason, f.Panel.DisabledReason(intent));
        f.Panel.Handle(intent);
        Assert.Equal(count, f.Transport.ClientSent.Count);
    }

    [Fact]
    public void DirectorySearchIsBoundedAndUsesServerPageAndRetainsSelectionOnRefresh()
    {
        using var f = new PartyPanelFixture("leader");
        f.Panel.Select("b");
        f.Panel.Search(new string('x', 80));
        var query = Assert.IsType<PartyQueryPacket>(f.Transport.ClientSent.Last());
        Assert.Equal(64, query.Search.Length);
        Assert.True(query.IncludeTargets);
        Assert.Equal(0, query.Page);
        Assert.Equal("b", f.Panel.State.SelectedId);
        f.Panel.Handle("next");
        Assert.Equal(1, Assert.IsType<PartyQueryPacket>(f.Transport.ClientSent.Last()).Page);
    }

    [Fact]
    public void BackRestoresExactInventoryContextAndCloseDoesNotResetPending()
    {
        using var f = new PartyPanelFixture("none");
        var controller = new PlayerMenuController(new PlayerItemClientService(), f.Panel);
        controller.Enter(PlayerMenuEntryPoint.InventoryMenu);
        controller.Session.Update(new(Search: "copper", SelectedEntry: "ore", ScrollAnchor: 8, Focus: "search", InspectorScroll: 17));
        var before = controller.Session.Current;
        controller.OpenParty();
        controller.Session.Update(controller.Session.Current with { Focus = "party:create" });
        controller.Back();
        Assert.Equal(before, controller.Session.Current);
        Assert.True(controller.Session.IsOpen);
        controller.OpenParty();
        f.Panel.Handle("create");
        controller.Close();
        Assert.NotNull(f.Client.Pending);
        Assert.False(f.Panel.State.IsOpen);
        controller.OpenParty();
        Assert.NotNull(f.Client.Pending);
        f.Client.ResetConnection();
        Assert.False(f.Panel.State.IsOpen);
        Assert.Empty(f.Panel.State.SelectedId);
    }

    [Fact]
    public void UnknownFeedbackUsesGenericLocalizedKeyAndCancelledDisbandSendsNothing()
    {
        using var f = new PartyPanelFixture("leader");
        f.Panel.Select("a");
        f.Panel.Handle("disband");
        f.Panel.Handle("cancel");
        Assert.Null(f.Client.Pending);
        Assert.False(f.Panel.State.ConfirmDisband);
        Assert.Equal("party-error", PartyPanel.ResultKey("future-internal-code"));
    }

    [Fact]
    public void InitialQueryIsNotMutationSuccessAndConfirmationClearsPreviousSuccess()
    {
        using var f = new PartyPanelFixture("leader");
        Assert.Empty(f.Panel.FeedbackKey);
        f.Panel.Select("b"); f.Panel.Handle("remove");
        f.Transport.Receive(new PartyResultPacket { SessionId = "session", RequestId = f.Client.Pending!.RequestId, Code = "ok", Success = true });
        Assert.Equal("party-ok", f.Panel.FeedbackKey);
        f.Panel.Select("a"); f.Panel.Handle("disband");
        Assert.Empty(f.Panel.FeedbackKey);
    }

    [Fact]
    public void OfflineMemberCanBecomeLeaderAsPermittedByAuthority()
    {
        using var f = new PartyPanelFixture("offline");
        f.Panel.Select("b"); f.Panel.Handle("transfer");
        Assert.Equal(PartyAction.TransferLeader, f.Client.Pending!.Action);
    }

    [Fact]
    public void TypedDirectoryTextIsLocalUntilFindAndRejectsControlCharacters()
    {
        using var f = new PartyPanelFixture("leader");
        int before = f.Transport.ClientSent.Count;
        f.Panel.SetSearchText("Copper\nWren");
        Assert.Equal(before, f.Transport.ClientSent.Count);
        f.Panel.Handle("find");
        Assert.Equal("CopperWren", Assert.IsType<PartyQueryPacket>(f.Transport.ClientSent.Last()).Search);
    }

    [Fact]
    public void PanelCreateAndDisbandRoundtripThroughRegisteredAuthenticatedServerHandlers()
    {
        using var f = new PartyTransportFixture();
        using var client = new PartyClientService(f.ClientChannel);
        using var panel = new PartyPanel(client);
        var controller = new PlayerMenuController(new PlayerItemClientService(), panel); controller.OpenParty();
        f.Deliver(f.A, Assert.IsType<PartyQueryPacket>(f.ClientSent.Last()));
        f.Receive(f.Last<PartySessionPacket>(f.A)); f.Receive(f.Last<PartySnapshotPacket>(f.A));
        panel.Handle("create");
        f.Deliver(f.A, Assert.IsType<PartyMutationPacket>(f.ClientSent.Last()));
        f.Receive(f.Last<PartyResultPacket>(f.A));
        Assert.Equal("a", Assert.Single(panel.View!.Members).Uid);
        Assert.Equal(panel.View.PartyId, f.Service.Read("a", f.Now).PartyId);
        panel.Select("a"); panel.Handle("disband"); panel.Handle("confirm-disband");
        f.Deliver(f.A, Assert.IsType<PartyMutationPacket>(f.ClientSent.Last()));
        f.Receive(f.Last<PartyResultPacket>(f.A));
        Assert.Null(panel.View.PartyId);
        Assert.Null(f.Service.Read("a", f.Now).PartyId);
        Assert.Null(client.Pending);
    }

    [Fact]
    public void RemovedMemberIsNoLongerSelectedAfterAuthoritativeResult()
    {
        using var f = new PartyPanelFixture("long-unbroken");
        f.Panel.Open(new()); f.Panel.Select("b"); f.Panel.Handle("remove");
        f.CompleteRemove();
        Assert.Empty(f.Panel.State.SelectedId);
        Assert.True(f.Panel.State.ShowDetails);
        Assert.Equal("party-ok", f.Panel.FeedbackKey);
    }
}

internal sealed class PartyPanelFixture : IDisposable
{
    public const string InvitationId = "11111111111111111111111111111111";
    public PartyTransportFixture Transport { get; } = new();
    public PartyClientService Client { get; }
    public PartyPanel Panel { get; }
    public PartyPanelFixture(string scenario)
    {
        Client = new(Transport.ClientChannel);
        Panel = new(Client);
        Client.RequestView();
        var query = (PartyQueryPacket)Transport.ClientSent.Last();
        Transport.Receive(new PartySessionPacket { RequestId = query.RequestId, SessionId = "session" });
        if (scenario == "loading") return;
        var snapshot = new PartySnapshotPacket { SessionId = "session", RequestId = query.RequestId, Sequence = 1,
            Revision = 7, SelfUid = "a", Health = scenario == "unavailable" ? PartyHealth.Unavailable : PartyHealth.Healthy, HasMoreTargets = true };
        if (scenario is not ("none" or "invited" or "many-invites" or "expired" or "unavailable"))
        {
            snapshot.PartyId = "22222222222222222222222222222222"; snapshot.LeaderUid = scenario == "member" ? "b" : "a";
            snapshot.Members = Enumerable.Range(0, scenario == "full" ? 8 : scenario == "solo" ? 1 : 2)
                .Select(i => new PartyMemberPacket { Uid = i == 0 ? "a" : i == 1 ? "b" : "member" + i,
                    DisplayName = i == 0 ? "Rowan" : scenario == "long-unbroken" ? new string('W', 128) : scenario == "long-name"
                        ? ("Ash of the weathered northern woodland and the forgotten copper foundry beyond the old eastern cliffs, traveller of winter paths ")[..128]
                        : "Ash of the weathered northern woodland", IsOnline = i == 0 || scenario != "offline" }).ToArray();
            snapshot.Targets = new[] { new PartyTargetPacket { Uid = "c", DisplayName = "Copper Wren" } };
            if (scenario == "online-many") snapshot.Targets = Enumerable.Range(0, 16).Select(i => new PartyTargetPacket { Uid = "target" + i, DisplayName = "Copper Wren " + (i + 1) }).ToArray();
        }
        if (scenario is "invited" or "expired") snapshot.IncomingInvitations = new[] { new PartyInvitationPacket {
            InvitationId = InvitationId, PartyId = "33333333333333333333333333333333", LeaderUid = "b", LeaderName = "Ash", ExpiresAtUnixSeconds = DateTimeOffset.UtcNow.AddMinutes(scenario == "expired" ? -5 : 5).ToUnixTimeSeconds() } };
        if (scenario == "many-invites") snapshot.IncomingInvitations = Enumerable.Range(1, 8).Select(i => new PartyInvitationPacket {
            InvitationId = i.ToString("x32"), PartyId = (i + 10).ToString("x32"), LeaderUid = "leader" + i, LeaderName = "Ash " + i,
            ExpiresAtUnixSeconds = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds() }).ToArray();
        Transport.Receive(snapshot);
    }
    public void Dispose() { Panel.Dispose(); Client.Dispose(); Transport.Dispose(); }
    public void RejectDirectory(string intent)
    {
        Panel.Handle(intent);
        var query = (PartyQueryPacket)Transport.ClientSent.Last();
        Transport.Receive(new PartySnapshotPacket { SessionId = "session", RequestId = query.RequestId, Sequence = 2,
            Revision = 7, Health = PartyHealth.Healthy, SelfUid = "a", Code = "rate-limit" });
    }
    public void CompleteRemove() => Transport.Receive(new PartyResultPacket {
        SessionId = "session", RequestId = Client.Pending!.RequestId, Code = "ok", Success = true,
        Snapshot = new PartySnapshotPacket { SessionId = "session", RequestId = Client.Pending.RequestId, Sequence = 2, Revision = 8,
            Health = PartyHealth.Healthy, SelfUid = "a", PartyId = "22222222222222222222222222222222", LeaderUid = "a",
            Members = new[] { new PartyMemberPacket { Uid = "a", DisplayName = "Rowan", IsOnline = true } } } });
}
