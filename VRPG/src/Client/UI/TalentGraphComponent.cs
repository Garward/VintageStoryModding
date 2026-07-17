using System;
using System.Collections.Generic;
using Cairo;
using VRPG.Network;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace VRPG.Client.UI;

/// <summary>
/// Reusable, server-snapshot-driven talent graph viewport shared by player and authoring UIs.
/// </summary>
public sealed class TalentGraphComponent
{
    public const int MaximumNodeCount = 2048;
    private const double MinimumZoom = 0.06;
    private const double MaximumZoom = 2.25;

    private TalentTreeSnapshotPacket snapshot = new TalentTreeSnapshotPacket();
    private readonly Dictionary<string, TalentTreeNodePacket> nodesByCode = new Dictionary<string, TalentTreeNodePacket>(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> allocated = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> serverAllocated = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> pendingAllocate = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> pendingRefund = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly List<TalentGraphNodeVisual> visibleNodes = new List<TalentGraphNodeVisual>();
    private string selectedCode = "";
    private int unspentPoints;
    private bool fitRequired = true;
    private double panX;
    private double panY;
    private double zoom = 1.0;

    public TalentTreeSnapshotPacket Snapshot => snapshot;
    public IReadOnlyList<TalentGraphNodeVisual> VisibleNodes => visibleNodes;
    public double Zoom => zoom;
    public int Count => snapshot.Nodes.Length;
    public bool AuthoringMode { get; set; }

    public TalentTreeNodePacket? SelectedNode => nodesByCode.TryGetValue(selectedCode, out TalentTreeNodePacket? node) ? node : null;

    public bool SetTree(TalentTreeSnapshotPacket? next, bool preserveView = false)
    {
        next ??= new TalentTreeSnapshotPacket();
        if (next.SchemaVersion != TalentTreeSnapshotPacket.CurrentSchemaVersion)
        {
            return false;
        }

        TalentTreeNodePacket[] nodes = next.Nodes ?? Array.Empty<TalentTreeNodePacket>();
        if (nodes.Length > MaximumNodeCount)
        {
            return false;
        }

        bool identityChanged = !string.Equals(snapshot.TreeCode, next.TreeCode, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(snapshot.ContentHash, next.ContentHash, StringComparison.Ordinal);
        snapshot = next;
        snapshot.Nodes = nodes;
        nodesByCode.Clear();
        for (int i = 0; i < nodes.Length; i++)
        {
            TalentTreeNodePacket node = nodes[i];
            node.Links ??= Array.Empty<string>();
            node.Modifiers ??= Array.Empty<string>();
            if (!string.IsNullOrWhiteSpace(node.Code))
            {
                nodesByCode[NormalizeCode(node.Code)] = node;
            }
        }

        if (identityChanged && !preserveView)
        {
            selectedCode = "";
        }
        if (SelectedNode == null)
        {
            TalentTreeNodePacket? starter = Array.Find(nodes, node => node.Starter);
            selectedCode = starter != null ? NormalizeCode(starter.Code) : nodes.Length > 0 ? NormalizeCode(nodes[0].Code) : "";
        }

        fitRequired |= identityChanged && !preserveView;
        return true;
    }

    public void SetPlayerState(string[]? allocatedTalents, int availablePoints)
    {
        SetPlayerPlan(allocatedTalents, allocatedTalents, Array.Empty<string>(), Array.Empty<string>(), availablePoints);
    }

    public void SetPlayerPlan(string[]? serverTalents, string[]? effectiveTalents, string[]? queuedAllocate, string[]? queuedRefund, int availablePoints)
    {
        allocated.Clear();
        serverAllocated.Clear();
        pendingAllocate.Clear();
        pendingRefund.Clear();
        string[] values = effectiveTalents ?? Array.Empty<string>();
        for (int i = 0; i < values.Length; i++)
        {
            allocated.Add(NormalizeCode(values[i]));
        }
        AddNormalized(serverAllocated, serverTalents);
        AddNormalized(pendingAllocate, queuedAllocate);
        AddNormalized(pendingRefund, queuedRefund);

        unspentPoints = Math.Max(0, availablePoints);
    }

    public TalentGraphNodeState StateFor(TalentTreeNodePacket node)
    {
        if (AuthoringMode)
        {
            return TalentGraphNodeState.Available;
        }
        string code = NormalizeCode(node.Code);
        if (pendingRefund.Contains(code)) return TalentGraphNodeState.PendingRefund;
        if (pendingAllocate.Contains(code)) return TalentGraphNodeState.PendingAllocation;
        if (allocated.Contains(code))
        {
            return TalentGraphNodeState.Allocated;
        }

        bool connected = allocated.Count == 0 ? node.Starter : !node.Starter && HasAllocatedNeighbor(node);
        if (!connected)
        {
            return TalentGraphNodeState.Blocked;
        }

        return node.Starter || unspentPoints >= Math.Max(1, node.Cost)
            ? TalentGraphNodeState.Available
            : TalentGraphNodeState.ConnectedNoPoints;
    }

    public bool SelectAt(double x, double y)
    {
        for (int i = visibleNodes.Count - 1; i >= 0; i--)
        {
            TalentGraphNodeVisual visual = visibleNodes[i];
            double dx = x - visual.X;
            double dy = y - visual.Y;
            if (dx * dx + dy * dy <= visual.Radius * visual.Radius)
            {
                selectedCode = NormalizeCode(visual.Node.Code);
                return true;
            }
        }

        return false;
    }

    public bool Select(string code)
    {
        string normalized = NormalizeCode(code);
        if (!nodesByCode.ContainsKey(normalized))
        {
            return false;
        }

        selectedCode = normalized;
        return true;
    }

    public void Pan(double deltaX, double deltaY, double viewportWidth, double viewportHeight)
    {
        panX += deltaX;
        panY += deltaY;
        ClampPan(viewportWidth, viewportHeight);
    }

    public void ZoomAt(int direction, double mouseXFromCenter, double mouseYFromCenter, double viewportWidth, double viewportHeight)
    {
        if (direction == 0 || snapshot.Nodes.Length == 0)
        {
            return;
        }

        double oldZoom = zoom;
        double factor = direction > 0 ? 1.22 : 1.0 / 1.22;
        zoom = GameMath.Clamp(zoom * factor, MinimumZoom, MaximumZoom);
        double graphX = (mouseXFromCenter - panX) / oldZoom;
        double graphY = (mouseYFromCenter - panY) / oldZoom;
        panX = mouseXFromCenter - graphX * zoom;
        panY = mouseYFromCenter - graphY * zoom;
        ClampPan(viewportWidth, viewportHeight);
    }

    public void Draw(Context ctx, double x, double y, double width, double height)
    {
        visibleNodes.Clear();
        if (width <= 0.0 || height <= 0.0 || snapshot.Nodes.Length == 0)
        {
            return;
        }

        EnsureFit(width, height);
        double centerX = x + width / 2.0 + panX;
        double centerY = y + height / 2.0 + panY;
        var positions = new Dictionary<string, TalentGraphPoint>(StringComparer.OrdinalIgnoreCase);
        var radii = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < snapshot.Nodes.Length; i++)
        {
            TalentTreeNodePacket node = snapshot.Nodes[i];
            string code = NormalizeCode(node.Code);
            positions[code] = new TalentGraphPoint(centerX + S(node.X) * zoom, centerY + S(node.Y) * zoom);
            radii[code] = NodeRadius(node) * zoom;
        }

        ctx.Save();
        ctx.Rectangle(x, y, width, height);
        ctx.Clip();
        DrawEdges(ctx, positions, radii);
        for (int i = 0; i < snapshot.Nodes.Length; i++)
        {
            TalentTreeNodePacket node = snapshot.Nodes[i];
            string code = NormalizeCode(node.Code);
            if (!positions.TryGetValue(code, out TalentGraphPoint point))
            {
                continue;
            }

            double radius = radii[code];
            TalentGraphNodeState state = StateFor(node);
            bool selected = AuthoringMode && string.Equals(code, selectedCode, StringComparison.OrdinalIgnoreCase);
            DrawNode(ctx, point.X, point.Y, radius, node, state, selected);
            visibleNodes.Add(new TalentGraphNodeVisual(node, state, point.X, point.Y, radius));
        }
        ctx.Restore();
    }

    private void DrawEdges(Context ctx, Dictionary<string, TalentGraphPoint> positions, Dictionary<string, double> radii)
    {
        var drawn = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < snapshot.Nodes.Length; i++)
        {
            TalentTreeNodePacket fromNode = snapshot.Nodes[i];
            string fromCode = NormalizeCode(fromNode.Code);
            if (!positions.TryGetValue(fromCode, out TalentGraphPoint from))
            {
                continue;
            }

            for (int linkIndex = 0; linkIndex < fromNode.Links.Length; linkIndex++)
            {
                string toCode = NormalizeCode(fromNode.Links[linkIndex]);
                if (!positions.TryGetValue(toCode, out TalentGraphPoint to) || !drawn.Add(EdgeKey(fromCode, toCode)))
                {
                    continue;
                }

                TalentTreeNodePacket? toNode = nodesByCode.TryGetValue(toCode, out TalentTreeNodePacket? found) ? found : null;
                bool selected = AuthoringMode && (string.Equals(fromCode, selectedCode, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(toCode, selectedCode, StringComparison.OrdinalIgnoreCase));
                SetEdgeStyle(ctx, fromNode, toNode, selected);
                TalentGraphPoint start = EdgePoint(from, to, radii[fromCode] + S(2.0));
                TalentGraphPoint end = EdgePoint(to, from, radii[toCode] + S(2.0));
                ctx.MoveTo(start.X, start.Y);
                ctx.LineTo(end.X, end.Y);
                ctx.Stroke();
            }
        }
    }

    private void SetEdgeStyle(Context ctx, TalentTreeNodePacket from, TalentTreeNodePacket? to, bool selected)
    {
        TalentGraphNodeState fromState = StateFor(from);
        TalentGraphNodeState toState = to == null ? TalentGraphNodeState.Blocked : StateFor(to);
        if (IsPathAllocated(fromState) && IsPathAllocated(toState))
        {
            ctx.SetSourceRGBA(0.35, 0.88, 0.18, selected ? 0.92 : 0.62);
            ctx.LineWidth = S(selected ? 4.0 : 2.8);
            return;
        }

        bool activeFrontier = (IsPathAllocated(fromState) && toState == TalentGraphNodeState.Available)
            || (IsPathAllocated(toState) && fromState == TalentGraphNodeState.Available);
        ctx.SetSourceRGBA(1.0, 0.62, 0.06, activeFrontier ? (selected ? 0.82 : 0.50) : (selected ? 0.40 : 0.18));
        ctx.LineWidth = S(activeFrontier ? (selected ? 3.4 : 2.2) : (selected ? 2.6 : 1.6));
    }

    private static void DrawNode(Context ctx, double x, double y, double radius, TalentTreeNodePacket node, TalentGraphNodeState state, bool selected)
    {
        string tier = NormalizeCode(node.VisualTier);
        bool starter = node.Starter || tier == "start";
        bool major = tier == "major";
        bool gamechanger = tier == "gamechanger";
        bool allocatedNode = state == TalentGraphNodeState.Allocated;
        bool queuedAllocation = state == TalentGraphNodeState.PendingAllocation;
        bool queuedRefund = state == TalentGraphNodeState.PendingRefund;
        bool available = state == TalentGraphNodeState.Available;
        bool connected = state == TalentGraphNodeState.ConnectedNoPoints;

        if (starter)
        {
            ctx.Arc(x, y, radius * 1.42, 0, Math.PI * 2.0);
            ctx.SetSourceRGBA(0.26, 0.13, 0.02, state == TalentGraphNodeState.Blocked ? 0.48 : 0.96);
            ctx.FillPreserve();
            ctx.LineWidth = S(selected ? 4.0 : 2.6);
            ctx.SetSourceRGBA(1.0, 0.72, 0.10, state == TalentGraphNodeState.Blocked ? 0.28 : 0.95);
            ctx.Stroke();
            ctx.Arc(x, y, radius * 1.16, 0, Math.PI * 2.0);
            ctx.SetSourceRGBA(1.0, 0.62, 0.05, state == TalentGraphNodeState.Blocked ? 0.22 : 0.72);
            ctx.LineWidth = S(1.8);
            ctx.Stroke();
        }

        if (gamechanger)
        {
            double square = radius * 1.92;
            RoundedRectangle(ctx, x - square / 2.0, y - square / 2.0, square, square, S(3.0));
            ctx.SetSourceRGBA(0.18, 0.10, 0.055, state == TalentGraphNodeState.Blocked ? 0.58 : 1.0);
            ctx.FillPreserve();
            ctx.LineWidth = S(selected ? 3.0 : 1.8);
            ctx.SetSourceRGBA(1.0, 0.62, 0.06, selected ? 0.98 : available ? 0.88 : 0.46);
            ctx.Stroke();
        }

        if (major || gamechanger)
        {
            DrawGearRing(ctx, x, y, radius * (gamechanger ? 1.32 : 1.28), radius * 1.05, gamechanger ? 16 : 12);
            ctx.SetSourceRGBA(0.20, 0.10, 0.055, state == TalentGraphNodeState.Blocked ? 0.55 : 1.0);
            ctx.FillPreserve();
            ctx.SetSourceRGBA(1.0, 0.62, 0.06, selected ? 0.95 : available ? 0.72 : 0.36);
            ctx.LineWidth = S(selected ? 2.5 : 1.4);
            ctx.Stroke();
        }

        ctx.Arc(x, y, radius, 0, Math.PI * 2.0);
        if (queuedAllocation) ctx.SetSourceRGBA(0.12, 0.48, 0.72, 1.0);
        else if (queuedRefund) ctx.SetSourceRGBA(0.62, 0.12, 0.08, 1.0);
        else if (allocatedNode) ctx.SetSourceRGBA(0.25, 0.58, 0.13, 1.0);
        else if (available) ctx.SetSourceRGBA(0.24, 0.18, 0.04, 1.0);
        else if (connected) ctx.SetSourceRGBA(0.13, 0.11, 0.06, 0.95);
        else ctx.SetSourceRGBA(0.055, 0.045, 0.045, 0.86);
        ctx.FillPreserve();
        ctx.LineWidth = S(selected ? 3.0 : 1.4);
        ctx.SetSourceRGBA(1.0, 0.62, 0.06, selected ? 0.95 : available ? 0.78 : connected ? 0.42 : 0.24);
        ctx.Stroke();

        ctx.Arc(x, y, radius * (starter ? 0.38 : gamechanger ? 0.34 : major ? 0.28 : 0.22), 0, Math.PI * 2.0);
        if (starter) ctx.SetSourceRGBA(1.0, 0.74, 0.12, state == TalentGraphNodeState.Blocked ? 0.28 : 1.0);
        else if (gamechanger) ctx.SetSourceRGBA(0.78, 0.06, 0.48, state == TalentGraphNodeState.Blocked ? 0.35 : 0.92);
        else if (major) ctx.SetSourceRGBA(0.25, 0.60, 0.95, state == TalentGraphNodeState.Blocked ? 0.32 : 0.88);
        else ctx.SetSourceRGBA(0.90, 0.45, 0.10, state == TalentGraphNodeState.Blocked ? 0.25 : 0.80);
        ctx.Fill();
    }

    private bool HasAllocatedNeighbor(TalentTreeNodePacket node)
    {
        for (int i = 0; i < node.Links.Length; i++)
        {
            if (allocated.Contains(NormalizeCode(node.Links[i])))
            {
                return true;
            }
        }

        string code = NormalizeCode(node.Code);
        foreach (string allocatedCode in allocated)
        {
            if (!nodesByCode.TryGetValue(allocatedCode, out TalentTreeNodePacket? other))
            {
                continue;
            }

            for (int i = 0; i < other.Links.Length; i++)
            {
                if (string.Equals(NormalizeCode(other.Links[i]), code, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private void EnsureFit(double width, double height)
    {
        if (!fitRequired)
        {
            ClampPan(width, height);
            return;
        }

        GetBounds(out double minX, out double minY, out double maxX, out double maxY);
        double contentWidth = Math.Max(S(1.0), maxX - minX + S(96.0));
        double contentHeight = Math.Max(S(1.0), maxY - minY + S(96.0));
        zoom = GameMath.Clamp(Math.Min(width / contentWidth, height / contentHeight), MinimumZoom, MaximumZoom);
        panX = -(minX + maxX) * 0.5 * zoom;
        panY = -(minY + maxY) * 0.5 * zoom;
        fitRequired = false;
        ClampPan(width, height);
    }

    private void ClampPan(double width, double height)
    {
        if (snapshot.Nodes.Length == 0 || width <= 0.0 || height <= 0.0)
        {
            panX = 0.0;
            panY = 0.0;
            return;
        }

        GetBounds(out double minX, out double minY, out double maxX, out double maxY);
        ClampAxis(width, minX * zoom, maxX * zoom, ref panX);
        ClampAxis(height, minY * zoom, maxY * zoom, ref panY);
    }

    private void GetBounds(out double minX, out double minY, out double maxX, out double maxY)
    {
        minX = double.MaxValue;
        minY = double.MaxValue;
        maxX = double.MinValue;
        maxY = double.MinValue;
        for (int i = 0; i < snapshot.Nodes.Length; i++)
        {
            TalentTreeNodePacket node = snapshot.Nodes[i];
            double radius = NodeRadius(node);
            double x = S(node.X);
            double y = S(node.Y);
            minX = Math.Min(minX, x - radius);
            minY = Math.Min(minY, y - radius);
            maxX = Math.Max(maxX, x + radius);
            maxY = Math.Max(maxY, y + radius);
        }
    }

    private static void ClampAxis(double viewportSize, double contentMin, double contentMax, ref double pan)
    {
        double padding = S(48.0);
        if (contentMax - contentMin <= Math.Max(1.0, viewportSize - padding * 2.0))
        {
            pan = -((contentMin + contentMax) * 0.5);
            return;
        }

        pan = GameMath.Clamp(pan, viewportSize * 0.5 - padding - contentMax, padding - viewportSize * 0.5 - contentMin);
    }

    private static double NodeRadius(TalentTreeNodePacket node)
    {
        string tier = NormalizeCode(node.VisualTier);
        return S(node.Starter || tier == "start" ? 36.0 : tier == "gamechanger" ? 24.0 : tier == "major" ? 20.0 : 16.0);
    }

    private static TalentGraphPoint EdgePoint(TalentGraphPoint from, TalentGraphPoint to, double radius)
    {
        double dx = to.X - from.X;
        double dy = to.Y - from.Y;
        double length = Math.Sqrt(dx * dx + dy * dy);
        return length <= 0.0001 ? from : new TalentGraphPoint(from.X + dx / length * radius, from.Y + dy / length * radius);
    }

    private static string EdgeKey(string left, string right)
    {
        return string.Compare(left, right, StringComparison.OrdinalIgnoreCase) <= 0 ? left + "|" + right : right + "|" + left;
    }

    private static string NormalizeCode(string value)
    {
        string safe = (value ?? "").Trim();
        return safe.StartsWith("vrpg:", StringComparison.OrdinalIgnoreCase) ? safe.Substring("vrpg:".Length).ToLowerInvariant() : safe.ToLowerInvariant();
    }

    private static bool IsPathAllocated(TalentGraphNodeState state)
    {
        return state == TalentGraphNodeState.Allocated || state == TalentGraphNodeState.PendingAllocation;
    }

    private static void AddNormalized(HashSet<string> target, string[]? values)
    {
        values ??= Array.Empty<string>();
        for (int i = 0; i < values.Length; i++) target.Add(NormalizeCode(values[i]));
    }

    private static double S(double value) => value * RuntimeEnv.GUIScale;

    private static void DrawGearRing(Context ctx, double x, double y, double outerRadius, double innerRadius, int teeth)
    {
        ctx.NewPath();
        for (int i = 0; i < teeth * 2; i++)
        {
            double angle = -Math.PI / 2.0 + i * Math.PI / teeth;
            double radius = i % 2 == 0 ? outerRadius : innerRadius;
            double px = x + Math.Cos(angle) * radius;
            double py = y + Math.Sin(angle) * radius;
            if (i == 0) ctx.MoveTo(px, py);
            else ctx.LineTo(px, py);
        }
        ctx.ClosePath();
    }

    private static void RoundedRectangle(Context ctx, double x, double y, double width, double height, double radius)
    {
        radius = Math.Min(radius, Math.Min(width, height) / 2.0);
        ctx.NewSubPath();
        ctx.Arc(x + width - radius, y + radius, radius, -Math.PI / 2.0, 0.0);
        ctx.Arc(x + width - radius, y + height - radius, radius, 0.0, Math.PI / 2.0);
        ctx.Arc(x + radius, y + height - radius, radius, Math.PI / 2.0, Math.PI);
        ctx.Arc(x + radius, y + radius, radius, Math.PI, 3.0 * Math.PI / 2.0);
        ctx.ClosePath();
    }

    private readonly struct TalentGraphPoint
    {
        public TalentGraphPoint(double x, double y)
        {
            X = x;
            Y = y;
        }

        public double X { get; }
        public double Y { get; }
    }
}

public enum TalentGraphNodeState
{
    Allocated,
    PendingAllocation,
    PendingRefund,
    Available,
    ConnectedNoPoints,
    Blocked
}

public readonly struct TalentGraphNodeVisual
{
    public TalentGraphNodeVisual(TalentTreeNodePacket node, TalentGraphNodeState state, double x, double y, double radius)
    {
        Node = node;
        State = state;
        X = x;
        Y = y;
        Radius = radius;
    }

    public TalentTreeNodePacket Node { get; }
    public TalentGraphNodeState State { get; }
    public double X { get; }
    public double Y { get; }
    public double Radius { get; }
}
