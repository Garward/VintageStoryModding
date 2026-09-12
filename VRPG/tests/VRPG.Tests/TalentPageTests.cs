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
        public HubPageClientService Hub { get; }
        public Fixture(string[]? talents = null, int points = 3, int respec = 2)
        {
            Hub = new(_ => { }, new() { ApplyTalentPlan = (a, r) => Sent.Add((a, r)) });
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
