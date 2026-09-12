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
    // Mirrors TalentAllocationService.ApplyPlan's combined wire-request limit.
    private const int MaximumChanges = 512;
    private readonly HubPageClientService hub;
    private readonly HashSet<string> allocations = new(StringComparer.OrdinalIgnoreCase), refunds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TalentTreeNodePacket> nodes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> costs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<string>> adjacency = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> starters = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> serverTalents = new(StringComparer.OrdinalIgnoreCase), effective = new(StringComparer.OrdinalIgnoreCase);
    private int points, respec, rawPoints, validationVersion, selectedVersion = -1;
    private bool connected, editorGranted, selectedAllocate, selectedRefund, feedbackError;
    private bool validationDirty = true, needsRebase = true;
    private string selectedValidationCode = "", feedback = "";
    public TalentPage(HubPageClientService hub) => this.hub = hub;
    public TalentGraphComponent Graph { get; private set; } = new() { CoordinateScale = 1, ShowPlayerSelection = true };
    public IReadOnlyCollection<string> Allocations => allocations.ToArray();
    public IReadOnlyCollection<string> Refunds => refunds.ToArray();
    public IReadOnlyCollection<string> EffectiveTalents => effective.ToArray();
    public int PlannedPoints => Math.Max(0, rawPoints);
    public int RemainingRespec => Math.Max(0, respec - refunds.Count);
    public int Changes => allocations.Count + refunds.Count;
    public bool Pending { get; private set; }
    public bool CanEditTree => editorGranted && hub.TalentTreeReady && !hub.Loading && hub.ErrorKey.Length == 0 && !Pending;
    public bool CanEdit => hub.HasSnapshot && hub.TalentTreeReady && !hub.Loading && hub.ErrorKey.Length == 0 && !Pending && !hub.Stats.Pending && !hub.Skills.Pending;
    public bool CanApply => CanEdit && Changes is > 0 and <= MaximumChanges && rawPoints >= 0 && refunds.Count <= respec && connected;
    public string Feedback => Changes > MaximumChanges && !Pending ? Shell.PlayerMenuShell.T("talent-plan-limit") : feedback;
    public bool FeedbackError => feedbackError || Changes > MaximumChanges && !Pending;
    private static string Canonical(string code) => code.Trim().Contains(':') ? code.Trim().ToLowerInvariant() : "vrpg:" + code.Trim().ToLowerInvariant();
    public int Cost(string code) => costs.GetValueOrDefault(Canonical(code), 1);
    private int CalculatePoints() => points + refunds.Sum(code => costs.GetValueOrDefault(code, 1)) - allocations.Sum(code => costs.GetValueOrDefault(code, 1));
    private HashSet<string> Effective()
    {
        var result = new HashSet<string>(serverTalents, StringComparer.OrdinalIgnoreCase);
        result.ExceptWith(refunds); result.UnionWith(allocations); return result;
    }
    public void QueueAllocation(string code) => Queue(code, false);
    public void QueueRefund(string code) => Queue(code, true);
    public bool CanQueueSelected(bool refund)
    {
        if (!CanEdit || Graph.SelectedNode is not { } node) return false;
        string code = Canonical(node.Code);
        if (selectedVersion != validationVersion || selectedValidationCode != code)
        {
            selectedValidationCode = code; selectedVersion = validationVersion;
            selectedAllocate = CanQueue(code, false); selectedRefund = CanQueue(code, true);
        }
        return refund ? selectedRefund : selectedAllocate;
    }
    private bool CanQueue(string code, bool refund)
    {
        if (!nodes.TryGetValue(code, out var node)) return false;
        if (allocations.Contains(code)) return true;
        if (refunds.Contains(code))
        { var restored = new HashSet<string>(effective, StringComparer.OrdinalIgnoreCase); restored.Add(code); return rawPoints >= costs[code] && Connected(restored); }
        if (!refund) return !effective.Contains(code)
            && (effective.Count == 0 ? node.Starter : !node.Starter && adjacency[code].Any(effective.Contains))
            && (node.Starter || Math.Max(0, rawPoints) >= costs[code]);
        if (!serverTalents.Contains(code) || refunds.Count >= respec) return false;
        var remaining = new HashSet<string>(effective, StringComparer.OrdinalIgnoreCase); remaining.Remove(code); return Connected(remaining);
    }
    private void Queue(string code, bool refund)
    {
        code = Canonical(code);
        if (!CanEdit || !CanQueue(code, refund)) return;
        validationDirty = true;
        if (allocations.Remove(code)) Prune();
        else if (!refunds.Remove(code)) { if (refund) refunds.Add(code); else allocations.Add(code); }
        UpdateGraph();
    }
    private HashSet<string> Reachable(HashSet<string> plan)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? root = null;
        foreach (string starter in starters)
            if (plan.Contains(starter)) { if (root != null) return visited; root = starter; }
        if (root == null) return visited;
        var queue = new Queue<string>(); queue.Enqueue(root); visited.Add(root);
        while (queue.TryDequeue(out var current))
            foreach (string neighbor in adjacency[current])
                if (plan.Contains(neighbor) && visited.Add(neighbor)) queue.Enqueue(neighbor);
        return visited;
    }
    private bool Connected(HashSet<string> plan) => plan.Count == 0 || Reachable(plan).Count == plan.Count;
    private void Prune() { var reachable = Reachable(Effective()); allocations.RemoveWhere(code => !reachable.Contains(code)); }
    internal void Revalidate()
    {
        // In-flight arrays describe the exact submitted request until its Hub response arrives.
        if (needsRebase && !Pending && hub.TalentTreeReady)
        {
            allocations.RemoveWhere(code => !nodes.ContainsKey(code) || serverTalents.Contains(code));
            refunds.RemoveWhere(code => !nodes.ContainsKey(code) || !serverTalents.Contains(code));
            Prune();
            needsRebase = false; validationDirty = true;
        }
        UpdateGraph();
    }
    public void Apply()
    {
        if (!CanApply) return;
        Pending = true; feedback = ""; feedbackError = false;
        try { hub.Actions.ApplyTalentPlan(allocations.ToArray(), refunds.ToArray()); }
        catch (Exception error) { Pending = false; feedbackError = true; feedback = Shell.PlayerMenuShell.T("feedback-talents-failed"); hub.Actions.ReportError(error); }
    }
    public void ResetDraft() { if (Pending) return; allocations.Clear(); refunds.Clear(); feedback = ""; feedbackError = false; validationDirty = true; UpdateGraph(); }
    public bool SelectAt(double x, double y, EnumMouseButton button)
    {
        if (!hub.TalentTreeReady || !Graph.SelectAt(x, y)) return false;
        string code = Graph.SelectedNode!.Code;
        if (button == EnumMouseButton.Left) QueueAllocation(code); else if (button == EnumMouseButton.Right) QueueRefund(code);
        SaveView(); return true;
    }
    public void Pan(double x, double y, double width, double height) { if (!hub.TalentTreeReady) return; Graph.Pan(x, y, width, height); SaveView(); }
    public void ZoomAt(int direction, double x, double y, double width, double height) { if (!hub.TalentTreeReady) return; Graph.ZoomAt(direction, x, y, width, height); SaveView(); }
    public void SaveView() => hub.State = hub.State with { TalentPanX = Graph.PanX, TalentPanY = Graph.PanY, TalentZoom = Graph.Zoom,
        TalentSelection = Graph.SelectedNode?.Code ?? "", TalentAllocations = allocations.ToImmutableArray(), TalentRefunds = refunds.ToImmutableArray() };
    private void UpdateGraph()
    {
        if (!validationDirty) return;
        effective = Effective(); rawPoints = CalculatePoints(); connected = Connected(effective); validationVersion++;
        validationDirty = false;
        Graph.SetPlayerPlan(serverTalents.ToArray(), effective.ToArray(), allocations.ToArray(), refunds.ToArray(), PlannedPoints); SaveView();
    }
    internal void Receive(OpenHubPacket packet)
    {
        UpdateServerTalents(packet.Talents); editorGranted = packet.CanEditTalentTree; UpdateResources(packet.Resources);
        if (Pending)
        {
            Pending = false; feedbackError = packet.FeedbackError;
            feedback = HubPageFeedback.Present(packet.FeedbackMessage, packet.FeedbackError, PlayerMenuPage.Talents);
            if (!packet.FeedbackError) { allocations.Clear(); refunds.Clear(); validationDirty = true; }
        }
        Revalidate();
    }
    internal void Receive(TalentTreeSnapshotPacket packet)
    {
        if (!Graph.SetTree(packet, preserveView: Graph.Count > 0)) return;
        validationDirty = true; needsRebase = true;
        nodes.Clear(); costs.Clear(); adjacency.Clear(); starters.Clear();
        foreach (var node in packet.Nodes)
        {
            string code = Canonical(node.Code); nodes[code] = node; costs[code] = node.Starter ? 0 : Math.Max(1, node.Cost);
            adjacency[code] = new(StringComparer.OrdinalIgnoreCase); if (node.Starter) starters.Add(code);
        }
        foreach (var (code, node) in nodes)
            foreach (string link in node.Links)
            {
                string other = Canonical(link); if (!adjacency.ContainsKey(other)) continue;
                adjacency[code].Add(other); adjacency[other].Add(code);
            }
        Revalidate();
    }
    private void UpdateServerTalents(string[] values)
    {
        var next = new HashSet<string>(values.Select(Canonical), StringComparer.OrdinalIgnoreCase);
        if (serverTalents.SetEquals(next)) return;
        serverTalents = next; validationDirty = true; needsRebase = true;
    }
    internal void Receive(TalentProgressPacket packet) { UpdateServerTalents(packet.Talents); Revalidate(); }
    internal void UpdateResources(RpgResourcePacket? packet)
    {
        int nextPoints = Math.Max(0, packet?.UnspentTalentPoints ?? 0), nextRespec = Math.Max(0, packet?.RespecPoints ?? 0);
        if (points == nextPoints && respec == nextRespec) return;
        points = nextPoints; respec = nextRespec; validationDirty = true; UpdateGraph();
    }
    internal void Reset()
    {
        Pending = false; editorGranted = false; feedback = ""; feedbackError = false; points = 0; respec = 0; serverTalents.Clear();
        allocations.Clear(); refunds.Clear(); nodes.Clear(); costs.Clear(); adjacency.Clear(); starters.Clear();
        validationDirty = true; needsRebase = true;
        Graph = new() { CoordinateScale = 1, ShowPlayerSelection = true }; UpdateGraph();
    }
}
