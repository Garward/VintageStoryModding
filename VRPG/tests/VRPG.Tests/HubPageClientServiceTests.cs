using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using VRPG.Client.PlayerMenu.Hub;
using VRPG.Network;
using Xunit;

namespace VRPG.Tests;

public sealed class HubPageClientServiceTests
{
    [Fact]
    public void RequestsCoalesceAndResponseReleasesPendingState()
    {
        var requests = new List<OpenHubRequestPacket>();
        using var service = new HubPageClientService(requests.Add, new());
        service.RequestSnapshot(); service.RequestSnapshot();
        Assert.True(service.Loading); Assert.Single(requests);
        service.Receive(new OpenHubPacket());
        Assert.False(service.Loading);
        service.RequestSnapshot(); Assert.Equal(2, requests.Count);
    }

    [Fact]
    public void ClosedMenuStillAcceptsResourceUpdatesAndSnapshotsAreIsolated()
    {
        using var service = new HubPageClientService(_ => { }, new());
        var source = new OpenHubPacket { BaseStats = new[] { new HubStatPacket { Value = 4 } } };
        service.Receive(source);
        source.BaseStats[0].Value = 999;
        var resource = new RpgResourcePacket { Level = 12 };
        service.Receive(resource); resource.Level = 99;
        var read = service.Packet!; read.Resources!.Level = 87; read.BaseStats[0].Value = 123;
        Assert.Equal(12, service.Packet!.Resources!.Level);
        Assert.Equal(4, service.Packet.BaseStats[0].Value);
    }

    [Fact]
    public void UpdatesBeforeHubSnapshotDoNotPretendFullHubStateExists()
    {
        using var service = new HubPageClientService(_ => { }, new());
        service.Receive(new RpgResourcePacket { Level = 12 });
        service.Receive(new TalentProgressPacket { Talents = new[] { "root" } });
        Assert.Null(service.Packet);
        service.Receive(new OpenHubPacket());
        Assert.Equal(12, service.Packet!.Resources!.Level);
    }

    [Fact]
    public void NullCollectionsNormalizeAndAcceptedUpdatesNotifyOnce()
    {
        using var service = new HubPageClientService(_ => { }, new());
        int changed = 0; service.Changed += () => changed++;
        service.Receive(new OpenHubPacket { Entries = null!, BaseStats = null!, Classes = new[] { new HubClassPacket { Skills = null!, Tags = null! } }, Talents = null!, Options = null! });
        Assert.Empty(service.Packet!.Entries); Assert.Empty(service.Packet.BaseStats);
        Assert.Empty(service.Packet.Classes[0].Skills); Assert.Empty(service.Packet.Classes[0].Tags);
        Assert.NotNull(service.Packet.Options); Assert.Empty(service.Packet.Talents);
        service.Receive(new TalentProgressPacket { Talents = null! });
        service.Receive(new RpgResourcePacket { Level = 3 });
        service.Receive(new TalentTreeSnapshotPacket());
        Assert.Equal(4, changed);
    }

    [Fact]
    public void StaleTreeRequestsRefreshWithKnownIdentityAndCoalesces()
    {
        var requests = new List<TalentTreeRequestPacket>();
        using var service = new HubPageClientService(_ => { }, new() { RequestTalentTree = requests.Add });
        service.Receive(new TalentTreeSnapshotPacket { TreeCode = "tree", ContentHash = "old" });
        service.Receive(new OpenHubPacket { TalentTreeCode = "tree", TalentTreeHash = "new" });
        service.Receive(new OpenHubPacket { TalentTreeCode = "tree", TalentTreeHash = "new" });
        Assert.Single(requests); Assert.Equal("old", requests[0].KnownContentHash);
        service.Receive(new TalentTreeSnapshotPacket { TreeCode = "tree", ContentHash = "new" });
        service.Receive(new OpenHubPacket { TalentTreeCode = "tree", TalentTreeHash = "new" });
        Assert.Single(requests); Assert.Equal("new", service.TalentTree!.ContentHash);
    }

    [Fact]
    public void InvalidTreePreservesAcceptedStateAndRetryClearsError()
    {
        using var service = new HubPageClientService(_ => { }, new());
        service.Receive(new TalentTreeSnapshotPacket { TreeCode = "valid" });
        service.Receive(new TalentTreeSnapshotPacket { SchemaVersion = -1 });
        Assert.Equal("valid", service.TalentTree!.TreeCode);
        Assert.Equal("hub-error", service.ErrorKey);
        service.RequestSnapshot(); Assert.Equal("", service.ErrorKey);
    }

    [Fact]
    public void ResetClearsPlayerDataAndRetainedPageStateAndDisposeDetaches()
    {
        var service = new HubPageClientService(_ => { }, new());
        service.Receive(new OpenHubPacket());
        service.Receive(new TalentTreeSnapshotPacket());
        service.State = new(SelectedClass: "warrior", SelectedSkill: "strike", OptionsCategory: "combat", LibraryQuery: "axe", LibrarySelection: "entry",
            TalentPanX: 23, TalentZoom: 0.5, TalentAllocations: ImmutableArray.Create("root"));
        service.RequestSnapshot(); service.ResetConnection();
        Assert.Null(service.Packet); Assert.Null(service.TalentTree); Assert.False(service.Loading);
        Assert.Equal(new HubPageState(), service.State);
        int changed = 0; service.Changed += () => changed++;
        service.Dispose(); int afterDispose = changed;
        service.Receive(new OpenHubPacket()); service.RequestSnapshot();
        Assert.Null(service.Packet); Assert.Equal(afterDispose, changed);
    }

    [Fact]
    public void FailedSendCanBeRetried()
    {
        int attempts = 0;
        using var service = new HubPageClientService(_ => { if (++attempts == 1) throw new InvalidOperationException("offline"); }, new());
        service.RequestSnapshot(); Assert.False(service.Loading); Assert.Equal("hub-error", service.ErrorKey);
        service.RequestSnapshot(); Assert.True(service.Loading); Assert.Equal(2, attempts);
    }

    [Fact]
    public void TreeArrivingAheadOfHubDoesNotRequestThePreviousHubTree()
    {
        var requests = new List<TalentTreeRequestPacket>();
        using var service = new HubPageClientService(_ => { }, new() { RequestTalentTree = requests.Add });
        service.Receive(new TalentTreeSnapshotPacket { TreeCode = "tree", ContentHash = "old" });
        service.Receive(new OpenHubPacket { TalentTreeCode = "tree", TalentTreeHash = "old" });
        service.Receive(new TalentTreeSnapshotPacket { TreeCode = "tree", ContentHash = "new" });
        Assert.Empty(requests);
        service.Receive(new OpenHubPacket { TalentTreeCode = "tree", TalentTreeHash = "new" });
        Assert.Empty(requests);
    }

    [Fact]
    public void DefensiveCopiesPreserveExplicitValuesThatDifferFromDtoInitializers()
    {
        using var service = new HubPageClientService(_ => { }, new());
        service.Receive(new OpenHubPacket { Options = new HubOptionsPacket
            { SkillHotbarLocked = false, HoldToRepeatChargedSkills = false, SkillHotbarSlots = 0 } });
        Assert.False(service.Packet!.Options.SkillHotbarLocked);
        Assert.False(service.Packet.Options.HoldToRepeatChargedSkills);
        Assert.Equal(0, service.Packet.Options.SkillHotbarSlots);
    }
}
