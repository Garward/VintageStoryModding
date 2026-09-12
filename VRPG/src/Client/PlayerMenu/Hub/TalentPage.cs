using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using VRPG.Client.UI;
using VRPG.Network;
using Vintagestory.API.Common;

namespace VRPG.Client.PlayerMenu.Hub;

/// <summary>Retained talent viewport and draft; the server owns committed progression.</summary>
public sealed class TalentPage
{
    private readonly HubPageClientService hub;
    private readonly HashSet<string> allocations = new(StringComparer.OrdinalIgnoreCase), refunds = new(StringComparer.OrdinalIgnoreCase);
    private string[] serverTalents = Array.Empty<string>();
    private int points, respec;
    public TalentPage(HubPageClientService hub) => this.hub = hub;
    public TalentGraphComponent Graph { get; private set; } = new() { CoordinateScale = 1, ShowPlayerSelection = true };
    public IReadOnlyCollection<string> Allocations => allocations.ToArray();
    public IReadOnlyCollection<string> Refunds => refunds.ToArray();
    public IReadOnlyCollection<string> EffectiveTalents => Effective();
    public int PlannedPoints => Math.Max(0, RawPoints);
    public int RemainingRespec => Math.Max(0, respec - refunds.Count);
    public int Changes => allocations.Count + refunds.Count;
    public bool Pending { get; private set; }
    public bool CanEditTree { get; private set; }
    public bool CanEdit => hub.HasSnapshot && !hub.Loading && hub.ErrorKey.Length == 0 && !Pending && !hub.Stats.Pending && !hub.Skills.Pending;
    public bool CanApply => CanEdit && Changes > 0 && RawPoints >= 0 && refunds.Count <= respec && Connected();
    public string Feedback { get; private set; } = "";
    public bool FeedbackError { get; private set; }
    private int RawPoints => points + refunds.Sum(Cost) - allocations.Sum(Cost);
    private static string Canonical(string code) => code.Trim().Contains(':') ? code.Trim().ToLowerInvariant() : "vrpg:" + code.Trim().ToLowerInvariant();
    private TalentTreeNodePacket? Node(string code) => Array.Find(Graph.Snapshot.Nodes, n => Canonical(n.Code) == Canonical(code));
    public int Cost(string code) => Node(code)?.Starter == true ? 0 : Math.Max(1, Node(code)?.Cost ?? 1);
    private HashSet<string> Effective()
    {
        var result = new HashSet<string>(serverTalents.Select(Canonical), StringComparer.OrdinalIgnoreCase);
        result.ExceptWith(refunds); result.UnionWith(allocations); return result;
    }
    public void QueueAllocation(string code) => Queue(code, false);
    public void QueueRefund(string code) => Queue(code, true);
    public bool CanQueueSelected(bool refund)
    {
        if (!CanEdit || Graph.SelectedNode is not { } node) return false;
        string code = Canonical(node.Code);
        if (allocations.Contains(code)) return true;
        if (refunds.Contains(code))
        { var restored = Effective(); restored.Add(code); return RawPoints >= Cost(code) && Reachable(restored).Count == restored.Count; }
        if (!refund) return Graph.StateFor(node) == TalentGraphNodeState.Available;
        if (Graph.StateFor(node) != TalentGraphNodeState.Allocated || refunds.Count >= respec) return false;
        var remaining = Effective(); remaining.Remove(code); return remaining.Count == 0 || Reachable(remaining).Count == remaining.Count;
    }
    private void Queue(string code, bool refund)
    {
        if (!CanEdit || Node(code) is not { } node) return;
        code = Canonical(code);
        if (allocations.Remove(code)) Prune();
        else if (refunds.Remove(code)) { if (RawPoints < 0 || !Connected()) refunds.Add(code); }
        else if (!refund && Graph.StateFor(node) == TalentGraphNodeState.Available) allocations.Add(code);
        else if (refund && Graph.StateFor(node) == TalentGraphNodeState.Allocated && refunds.Count < respec)
        { refunds.Add(code); if (!Connected()) refunds.Remove(code); }
        UpdateGraph();
    }
    private HashSet<string> Reachable(HashSet<string> effective)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var roots = Graph.Snapshot.Nodes.Where(n => n.Starter && effective.Contains(Canonical(n.Code))).ToArray();
        if (roots.Length != 1) return visited;
        var queue = new Queue<TalentTreeNodePacket>(); queue.Enqueue(roots[0]); visited.Add(Canonical(roots[0].Code));
        while (queue.TryDequeue(out var current))
            foreach (var candidate in Graph.Snapshot.Nodes)
            {
                string code = Canonical(candidate.Code);
                if (!effective.Contains(code) || visited.Contains(code)) continue;
                if (!current.Links.Any(link => Canonical(link) == code) && !candidate.Links.Any(link => Canonical(link) == Canonical(current.Code))) continue;
                visited.Add(code); queue.Enqueue(candidate);
            }
        return visited;
    }
    private bool Connected() { var effective = Effective(); return effective.Count == 0 || Reachable(effective).Count == effective.Count; }
    private void Prune() { var reachable = Reachable(Effective()); allocations.RemoveWhere(code => !reachable.Contains(code)); }
    public void Apply()
    {
        if (!CanApply) return;
        Pending = true; Feedback = ""; FeedbackError = false;
        try { hub.Actions.ApplyTalentPlan(allocations.ToArray(), refunds.ToArray()); }
        catch (Exception error) { Pending = false; FeedbackError = true; Feedback = Shell.PlayerMenuShell.T("feedback-talents-failed"); hub.Actions.ReportError(error); }
    }
    public void ResetDraft() { if (Pending) return; allocations.Clear(); refunds.Clear(); Feedback = ""; FeedbackError = false; UpdateGraph(); }
    public bool SelectAt(double x, double y, EnumMouseButton button)
    {
        if (!Graph.SelectAt(x, y)) return false;
        string code = Graph.SelectedNode!.Code;
        if (button == EnumMouseButton.Left) QueueAllocation(code); else if (button == EnumMouseButton.Right) QueueRefund(code);
        SaveView(); return true;
    }
    public void Pan(double x, double y, double width, double height) { Graph.Pan(x, y, width, height); SaveView(); }
    public void ZoomAt(int direction, double x, double y, double width, double height) { Graph.ZoomAt(direction, x, y, width, height); SaveView(); }
    public void SaveView() => hub.State = hub.State with { TalentPanX = Graph.PanX, TalentPanY = Graph.PanY, TalentZoom = Graph.Zoom,
        TalentSelection = Graph.SelectedNode?.Code ?? "", TalentAllocations = allocations.ToImmutableArray(), TalentRefunds = refunds.ToImmutableArray() };
    private void UpdateGraph()
    {
        Graph.SetPlayerPlan(serverTalents, Effective().ToArray(), allocations.ToArray(), refunds.ToArray(), PlannedPoints); SaveView();
    }
    internal void Receive(OpenHubPacket packet)
    {
        serverTalents = (string[])packet.Talents.Clone(); CanEditTree = packet.CanEditTalentTree; UpdateResources(packet.Resources);
        if (Pending)
        {
            Pending = false; FeedbackError = packet.FeedbackError;
            Feedback = HubPageFeedback.Present(packet.FeedbackMessage, packet.FeedbackError, PlayerMenuPage.Talents);
            if (!packet.FeedbackError) { allocations.Clear(); refunds.Clear(); }
        }
        UpdateGraph();
    }
    internal void Receive(TalentTreeSnapshotPacket packet)
    {
        if (!Graph.SetTree(packet, preserveView: Graph.Count > 0)) return;
        allocations.RemoveWhere(code => Node(code) == null); refunds.RemoveWhere(code => Node(code) == null);
        Prune(); UpdateGraph();
    }
    internal void Receive(TalentProgressPacket packet) { serverTalents = (string[])packet.Talents.Clone(); UpdateGraph(); }
    internal void UpdateResources(RpgResourcePacket? packet)
    { points = Math.Max(0, packet?.UnspentTalentPoints ?? 0); respec = Math.Max(0, packet?.RespecPoints ?? 0); UpdateGraph(); }
    internal void Reset()
    {
        Pending = false; CanEditTree = false; Feedback = ""; FeedbackError = false; points = 0; respec = 0; serverTalents = Array.Empty<string>();
        allocations.Clear(); refunds.Clear(); Graph = new() { CoordinateScale = 1, ShowPlayerSelection = true }; UpdateGraph();
    }
}
