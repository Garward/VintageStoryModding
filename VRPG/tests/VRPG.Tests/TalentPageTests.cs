using System;
using System.Collections.Generic;
using System.Linq;
using Cairo;
using VRPG.Client.PlayerMenu.Hub;
using VRPG.Network;
using Vintagestory.API.Common;
using Xunit;

namespace VRPG.Tests;

[Collection("Inventory Cairo")]
public sealed class TalentPageTests
{
    [Theory]
    [InlineData(false)][InlineData(true)]
    public void AuthoritativeUpdatesRebaseFulfilledAllocationsAndObsoleteRefunds(bool viaHub)
    {
        using var f = new Fixture(new[] { "root" }); var p = f.Hub.Talents;
        p.QueueAllocation("bridge"); p.QueueAllocation("leaf");
        void Receive(string[] talents, int points)
        {
            if (viaHub) f.Hub.Receive(Fixture.Packet(talents, points, 2));
            else { f.Hub.Receive(new TalentProgressPacket { Talents = talents }); f.Hub.Receive(new RpgResourcePacket { UnspentTalentPoints = points, RespecPoints = 2 }); }
        }
        Receive(new[] { "root", "bridge" }, 1);
        Assert.Equal(new[] { "vrpg:leaf" }, p.Allocations); Assert.Equal(0, p.PlannedPoints);
        Receive(new[] { "root", "bridge", "leaf" }, 0);
        Assert.Empty(p.Allocations); p.QueueRefund("leaf"); Assert.Equal(1, p.PlannedPoints);
        Receive(new[] { "root", "bridge" }, 1);
        Assert.Empty(p.Refunds); Assert.Equal(1, p.PlannedPoints); Assert.Equal(2, p.RemainingRespec);
    }

    [Theory]
    [InlineData(false)][InlineData(true)]
    public void AuthoritativeBridgeRemovalPrunesDisconnectedAllocation(bool viaHub)
    {
        using var f = new Fixture(new[] { "root", "bridge" }); var p = f.Hub.Talents;
        p.QueueAllocation("leaf");
        if (viaHub) f.Hub.Receive(Fixture.Packet(new[] { "root" }, 3, 2));
        else f.Hub.Receive(new TalentProgressPacket { Talents = new[] { "root" } });
        Assert.Empty(p.Allocations); Assert.Equal(3, p.PlannedPoints);
    }
    [Theory]
    [InlineData(false)][InlineData(true)]
    public void AuthoritativeUpdatesRemoveObsoleteRefundCredit(bool viaHub)
    {
        using var f = new Fixture(new[] { "root", "bridge", "leaf" }, 0, 2); var p = f.Hub.Talents;
        p.QueueRefund("leaf");
        if (viaHub) f.Hub.Receive(Fixture.Packet(new[] { "root", "bridge" }, 1, 1));
        else { f.Hub.Receive(new TalentProgressPacket { Talents = new[] { "root", "bridge" } }); f.Hub.Receive(new RpgResourcePacket { UnspentTalentPoints = 1, RespecPoints = 1 }); }
        Assert.Empty(p.Refunds); Assert.Equal(1, p.PlannedPoints); Assert.Equal(1, p.RemainingRespec);
    }

    [Fact]
    public void PendingRequestArraysSurviveProgressAndTreeChangesUntilHubResponse()
    {
        using var f = new Fixture(new[] { "root", "bridge", "leaf" }); var p = f.Hub.Talents;
        p.QueueRefund("leaf"); p.QueueAllocation("expensive"); p.Apply();
        var sent = Assert.Single(f.Sent);
        f.Hub.Receive(new TalentProgressPacket { Talents = new[] { "root", "bridge", "expensive" } });
        var tree = Fixture.Tree(); tree.Nodes = tree.Nodes.Where(n => n.Code != "leaf" && n.Code != "expensive").ToArray();
        f.Hub.Receive(tree);
        Assert.True(p.Pending); Assert.Equal(sent.Allocate, p.Allocations); Assert.Equal(sent.Refund, p.Refunds);
        f.Hub.Receive(Fixture.Packet(new[] { "root", "bridge" }, 3, 2));
        Assert.False(p.Pending); Assert.Empty(p.Allocations); Assert.Empty(p.Refunds);
    }

    [Theory]
    [InlineData(false, false)][InlineData(true, false)][InlineData(false, true)][InlineData(true, true)]
    public void TreeIdentityMismatchBlocksMutationUntilBothSnapshotsAgree(bool treeFirst, bool codeChanged)
    {
        using var f = new Fixture(new[] { "root" }); var p = f.Hub.Talents;
        var current = Fixture.Packet(new[] { "root" }, 3, 2); current.TalentTreeCode = "vrpg:test"; current.TalentTreeHash = "first"; current.CanEditTalentTree = true; f.Hub.Receive(current);
        p.QueueAllocation("bridge"); p.Graph.Select("bridge"); p.Pan(12, 5, 200, 100);
        double pan = p.Graph.PanX, zoom = p.Graph.Zoom;
        var next = Fixture.Tree(); next.ContentHash = codeChanged ? "first" : "next"; next.TreeCode = codeChanged ? "vrpg:replacement" : "vrpg:test";
        var packet = Fixture.Packet(new[] { "root" }, 3, 2); packet.TalentTreeCode = next.TreeCode; packet.TalentTreeHash = next.ContentHash; packet.CanEditTalentTree = true;
        if (treeFirst) f.Hub.Receive(next); else f.Hub.Receive(packet);
        Assert.False(p.CanEdit); Assert.False(p.CanApply); Assert.False(p.CanEditTree);
        p.QueueAllocation("leaf"); p.QueueRefund("root"); p.Apply();
        Assert.Equal(new[] { "vrpg:bridge" }, p.Allocations); Assert.Empty(f.Sent);
        Assert.Equal(treeFirst ? 0 : 1, f.TreeRequests.Count);
        Assert.Equal("bridge", p.Graph.SelectedNode!.Code); Assert.Equal(pan, p.Graph.PanX); Assert.Equal(zoom, p.Graph.Zoom);
        if (treeFirst) f.Hub.Receive(packet); else f.Hub.Receive(next);
        Assert.True(p.CanEdit); Assert.True(p.CanApply); Assert.True(p.CanEditTree);
    }

    [Fact]
    public void ConnectedMaximumTreeUsesBoundedAllocationWorkAndCachedValidation()
    {
        using var f = new Fixture(); var tree = LargeTree(2048); f.Hub.Receive(tree);
        f.Hub.Receive(Fixture.Packet(tree.Nodes.Select(n => n.Code).ToArray(), 2, 2)); var p = f.Hub.Talents;
        p.Graph.Select("node-2047");
        long start = GC.GetAllocatedBytesForCurrentThread();
        p.QueueRefund("node-2047"); p.QueueRefund("node-2047"); p.QueueRefund("node-2047");
        Assert.True(p.CanApply); Assert.Equal(3, p.PlannedPoints); Assert.Equal(1, p.RemainingRespec);
        long mutationBytes = GC.GetAllocatedBytesForCurrentThread() - start;
        Assert.True(mutationBytes < 8_000_000, $"Three leaf edits allocated {mutationBytes:N0} bytes.");
        _ = p.CanQueueSelected(true); start = GC.GetAllocatedBytesForCurrentThread();
        bool allowed = true; int pointTotal = 0, costTotal = 0;
        for (int i = 0; i < 100; i++) { allowed &= p.CanApply && p.CanQueueSelected(true); pointTotal += p.PlannedPoints; costTotal += p.Cost("node-2047"); }
        long queryBytes = GC.GetAllocatedBytesForCurrentThread() - start;
        Assert.True(allowed); Assert.Equal(300, pointTotal); Assert.Equal(100, costTotal);
        Assert.True(queryBytes < 64_000, $"Unchanged validation queries allocated {queryBytes:N0} bytes.");
        start = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10; i++) f.Hub.Receive(new RpgResourcePacket { UnspentTalentPoints = 2, RespecPoints = 2, Health = i });
        long unrelatedBytes = GC.GetAllocatedBytesForCurrentThread() - start;
        Assert.True(unrelatedBytes < 1_000_000, $"Unrelated health updates allocated {unrelatedBytes:N0} bytes.");
        f.Hub.Receive(new TalentProgressPacket { Talents = tree.Nodes.Take(2047).Select(n => n.Code).ToArray() });
        Assert.Empty(p.Refunds); p.QueueAllocation("node-2047");
        Assert.Equal(new[] { "vrpg:node-2047" }, p.Allocations); Assert.Equal(1, p.PlannedPoints); Assert.True(p.CanApply);
    }

    [Theory]
    [InlineData(511, true)][InlineData(512, false)]
    public void CombinedRequestLimitAccepts512AndRejects513(int allocations, bool expectedSend)
    {
        using var scope = new InventoryCompositionTestScope(); using var f = new Fixture();
        var tree = LargeTree(514, star: true); f.Hub.Receive(tree);
        f.Hub.Receive(Fixture.Packet(new[] { "node-0", "node-513" }, 600, 2));
        var p = f.Hub.Talents; p.QueueRefund("node-513");
        for (int i = 1; i <= allocations; i++) p.QueueAllocation("node-" + i);
        Assert.Equal(allocations + 1, p.Changes); Assert.Equal(expectedSend, p.CanApply); p.Apply();
        Assert.Equal(expectedSend ? 1 : 0, f.Sent.Count);
        if (!expectedSend) Assert.Equal("Apply up to 512 changes at a time. Remove some changes first.", p.Feedback);
    }

    private static TalentTreeSnapshotPacket LargeTree(int count, bool star = false) => new() { Nodes = Enumerable.Range(0, count).Select(i => new TalentTreeNodePacket {
        Code = "node-" + i, Starter = i == 0, Cost = 1,
        Links = i == 0 && star ? Enumerable.Range(1, count - 1).Select(n => "node-" + n).ToArray() : !star && i < count - 1 ? new[] { "node-" + (i + 1) } : Array.Empty<string>() }).ToArray() };
    [Fact]
    public void PendingTalentPlanBlocksOtherHubMutations()
    {
        using var f = new Fixture(); var packet = StatsSkillsPageTests.Fixture.Packet(); f.Hub.Receive(packet);
        f.Hub.Talents.QueueAllocation("root"); f.Hub.Talents.Apply();
        Assert.False(f.Hub.Stats.CanAdjust("strength", 1)); Assert.False(f.Hub.Skills.CanEquip);
    }
    [Fact]
    public void OnlyOneFreeStarterAndConnectedAffordableAllocationsEnterDraft()
    {
        using var f = new Fixture(); var p = f.Hub.Talents;
        p.QueueAllocation("leaf"); Assert.Empty(p.EffectiveTalents);
        p.QueueAllocation("root"); p.QueueAllocation("other-root");
        Assert.Equal(new[] { "vrpg:root" }, p.EffectiveTalents);
        Assert.Equal(3, p.PlannedPoints);
        p.QueueAllocation("bridge"); Assert.Equal(1, p.PlannedPoints);
        p.QueueAllocation("expensive"); Assert.DoesNotContain("vrpg:expensive", p.EffectiveTalents);
        p.QueueAllocation("leaf"); Assert.Equal(0, p.PlannedPoints);
        Assert.Equal(3, p.Allocations.Count);
    }

    [Fact]
    public void RefundCreditsCostSpendsOneRespecAndMustPreserveConnectivity()
    {
        using var f = new Fixture(new[] { "ROOT", "vrpg:bridge", "leaf" }, 0, 1); var p = f.Hub.Talents;
        p.QueueRefund("bridge"); Assert.Empty(p.Refunds);
        p.QueueRefund("leaf"); Assert.Equal(1, p.PlannedPoints); Assert.Equal(0, p.RemainingRespec);
        p.QueueRefund("bridge"); Assert.Single(p.Refunds);
        p.QueueRefund("leaf"); Assert.Empty(p.Refunds); Assert.Equal(0, p.PlannedPoints);
    }

    [Fact]
    public void CancelingQueuedBridgePrunesDisconnectedQueuedNodes()
    {
        using var f = new Fixture(); var p = f.Hub.Talents;
        p.QueueAllocation("root"); p.QueueAllocation("bridge"); p.QueueAllocation("leaf");
        p.QueueAllocation("bridge");
        Assert.Equal(new[] { "vrpg:root" }, p.EffectiveTalents); Assert.Equal(3, p.PlannedPoints);
    }

    [Fact]
    public void SpentRefundCannotBeUndoneUntilAllocationIsRemoved()
    {
        using var f = new Fixture(new[] { "root", "bridge", "leaf" }, 0, 2); var p = f.Hub.Talents;
        p.QueueRefund("leaf"); p.QueueRefund("bridge"); p.QueueAllocation("expensive");
        Assert.Equal(0, p.PlannedPoints); p.QueueAllocation("bridge");
        Assert.Contains("vrpg:bridge", p.Refunds);
        p.QueueRefund("expensive"); p.QueueRefund("bridge");
        Assert.DoesNotContain("vrpg:bridge", p.Refunds); Assert.Equal(1, p.PlannedPoints);
    }

    [Theory]
    [InlineData(false)][InlineData(true)]
    public void ApplySendsOnceAndOnlySuccessfulHubResponseClearsDraft(bool reject)
    {
        using var f = new Fixture(); var p = f.Hub.Talents;
        p.QueueAllocation("root"); p.QueueAllocation("bridge"); p.Apply(); p.Apply(); p.ResetDraft(); p.QueueAllocation("leaf");
        Assert.Single(f.Sent); Assert.Equal(new[] { "vrpg:bridge", "vrpg:root" }, f.Sent[0].Allocate.OrderBy(x => x));
        Assert.True(p.Pending); Assert.Equal(2, p.Allocations.Count);
        f.Hub.Receive(new TalentProgressPacket { Talents = new[] { "root", "bridge" } });
        f.Hub.Receive(new RpgResourcePacket { UnspentTalentPoints = 1 }); Assert.True(p.Pending);
        var packet = Fixture.Packet(reject ? Array.Empty<string>() : new[] { "root", "bridge" }, reject ? 3 : 1, 2);
        packet.FeedbackError = reject; packet.FeedbackMessage = reject ? "Unavailable while in combat." : "Talent plan applied.";
        f.Hub.Receive(packet); Assert.False(p.Pending); Assert.Equal(reject ? 2 : 0, p.Allocations.Count);
        Assert.Equal(reject, p.FeedbackError); Assert.Equal(packet.FeedbackMessage, p.Feedback);
    }

    [Fact]
    public void ChangedSnapshotPrunesMissingAndDisconnectedDraftAndKeepsSelectionAndView()
    {
        using var f = new Fixture(); var p = f.Hub.Talents;
        p.QueueAllocation("root"); p.QueueAllocation("bridge"); p.QueueAllocation("leaf"); p.Graph.Select("root");
        var tree = Fixture.Tree(); tree.ContentHash = "changed"; tree.Nodes = tree.Nodes.Where(n => n.Code != "bridge").ToArray();
        f.Hub.Receive(tree);
        Assert.Equal(new[] { "vrpg:root" }, p.Allocations); Assert.Equal(3, p.PlannedPoints);
        Assert.Equal("root", p.Graph.SelectedNode!.Code); Assert.True(p.CanApply);
        f.Hub.ResetConnection(); Assert.Empty(p.Allocations); Assert.Equal(0, p.Graph.Count); Assert.False(p.CanApply);
    }

    [Fact]
    public void ResourceLossDisablesOverBudgetPlanUntilResetOrAffordableAgain()
    {
        using var f = new Fixture(new[] { "root" }); var p = f.Hub.Talents;
        p.QueueAllocation("bridge"); f.Hub.Receive(new RpgResourcePacket { UnspentTalentPoints = 0 });
        Assert.False(p.CanApply); p.Apply(); Assert.Empty(f.Sent); Assert.Single(p.Allocations);
        p.ResetDraft(); Assert.Empty(p.Allocations);
    }

    [Fact]
    public void RightClickSelectsAndRefundsAndViewportPersistsInRetainedState()
    {
        using var scope = new InventoryCompositionTestScope(); using var f = new Fixture(new[] { "root", "bridge", "leaf" });
        using var surface = new ImageSurface((Format)0, 500, 400); using var ctx = new Context(surface);
        var p = f.Hub.Talents; p.Graph.Draw(ctx, 0, 0, 500, 400);
        var leaf = p.Graph.VisibleNodes.Single(n => n.Node.Code == "leaf");
        Assert.True(p.SelectAt(leaf.X, leaf.Y, EnumMouseButton.Right)); Assert.Contains("vrpg:leaf", p.Refunds);
        p.ZoomAt(1, 30, 20, 500, 400); p.Pan(20, 15, 500, 400);
        Assert.Equal(p.Graph.Zoom, f.Hub.State.TalentZoom); Assert.Equal("leaf", f.Hub.State.TalentSelection);
        f.Hub.ResetConnection(); Assert.Equal(1, p.Graph.Zoom); Assert.Equal(0, p.Graph.PanX); Assert.Equal(new HubPageState(), f.Hub.State);
    }

    internal sealed class Fixture : IDisposable
    {
        public List<(string[] Allocate, string[] Refund)> Sent { get; } = new();
        public List<TalentTreeRequestPacket> TreeRequests { get; } = new();
        public HubPageClientService Hub { get; }
        public Fixture(string[]? talents = null, int points = 3, int respec = 2)
        {
            Hub = new(_ => { }, new() { ApplyTalentPlan = (a, r) => Sent.Add((a, r)), RequestTalentTree = TreeRequests.Add });
            Hub.Receive(Tree()); Hub.Receive(Packet(talents ?? Array.Empty<string>(), points, respec));
        }
        public static OpenHubPacket Packet(string[] talents, int points, int respec) => new() {
            Talents = talents, Resources = new() { UnspentTalentPoints = points, RespecPoints = respec } };
        public static TalentTreeSnapshotPacket Tree() => new() { TreeCode = "vrpg:test", ContentHash = "first", TreeName = "Way of the Sentinel", Nodes = new[] {
            new TalentTreeNodePacket { Code = "root", Name = "Sentinel", Starter = true, Cost = 99, X = -180, Links = new[] { "bridge", "expensive" } },
            new TalentTreeNodePacket { Code = "bridge", Name = "Iron resolve", Cost = 2, X = 0, Links = new[] { "leaf" }, Modifiers = new[] { "+10 maximum health" } },
            new TalentTreeNodePacket { Code = "leaf", Name = "Steady guard", Cost = 0, X = 180, Description = "Hold your ground." },
            new TalentTreeNodePacket { Code = "expensive", Name = "Unbroken", Cost = 3, X = -80, Y = 130 },
            new TalentTreeNodePacket { Code = "other-root", Name = "Wanderer", Starter = true, X = 180, Y = 130 } } };
        public void Dispose() => Hub.Dispose();
    }
}
