using System;
using System.Collections.Generic;
using System.Linq;
using Cairo;
using VRPG.Client.PlayerMenu.Components;
using VRPG.Client.UI;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace VRPG.Client.PlayerMenu.Shell;

public sealed partial class PlayerMenuShell
{
    private bool TalentsOpen => gallery == null && !PartyOpen && session.Page == PlayerMenuPage.Talents && hub?.HasSnapshot == true && hub.ErrorKey.Length == 0;
    private bool TalentGraphOpen => TalentsOpen && (!Layout.AlternatePanes || !session.Current.InspectorOnly);
    private MenuRect TalentWell => new(Layout.Browser.X + 6, Layout.Browser.Y + 58, Layout.Browser.Width - 12, Math.Max(0, Layout.Browser.Height - 64));
    private bool talentDragging, talentMoved;
    private double talentStartX, talentStartY, talentLastX, talentLastY;
    private EnumMouseButton talentButton;
    public bool TalentDragging => talentDragging;

    private void DrawTalents(Context ctx)
    {
        var page = hub!.Talents; var b = Layout.Browser;
        if (TalentGraphOpen)
        {
            PlayerMenuDrawing.DrawWell(ctx, b);
            HubBrowseLabel(ctx, string.IsNullOrWhiteSpace(page.Graph.Snapshot.TreeName) ? T("talents") : page.Graph.Snapshot.TreeName,
                new(b.X + 12, b.Y + 7, b.Width - 24 - (page.CanEditTree ? 120 : 0), 24), 18);
            if (page.CanEditTree) Button(ctx, "talent:editor", "talent-editor", new(b.Right - 120, b.Y + 4, 112, 28));
            Text(ctx, hub.TalentTreeReady ? T("talent-points", page.PlannedPoints, page.RemainingRespec) : T("talent-loading"), new(b.X + 12, b.Y + 33, b.Width - 24, 22), 14, PlayerMenuTheme.Muted);
            var graph = TalentWell; PlayerMenuDrawing.DrawWell(ctx, graph);
            if (page.Graph.Count == 0)
                Text(ctx, T(hub.TalentTree == null ? "talent-loading" : "talent-empty"), new(graph.X + 10, graph.Y + 8, graph.Width - 20, graph.Height - 16), 16);
            else
            {
                page.Graph.Draw(ctx, graph.X, graph.Y, graph.Width, graph.Height); page.SaveView();
                controls.Add(("talent:graph", graph, new("talent:graph", "", MenuControlState.Default)));
                if (focus == "talent:graph")
                { ctx.Rectangle(graph.X + 1, graph.Y + 1, graph.Width - 2, graph.Height - 2); PlayerMenuDrawing.Color(ctx, PlayerMenuTheme.Heat); ctx.LineWidth = 2; ctx.Stroke(); }
            }
        }
        if (!Layout.AlternatePanes || session.Current.InspectorOnly) DrawTalentInspector(ctx);
        var action = Layout.PrimaryAction;
        int count = Layout.AlternatePanes ? 3 : 2; double width = (action.Width - (count - 1) * 4) / count;
        Button(ctx, "talent:apply", "", action with { Width = width }, page.Pending ? MenuControlState.Pending : page.CanApply ? MenuControlState.Default : MenuControlState.Disabled,
            page.Pending ? T("talent-applying") : T("talent-apply", page.Changes));
        Button(ctx, "talent:reset", "talent-reset", action with { X = action.X + width + 4, Width = width }, page.Changes > 0 && !page.Pending ? MenuControlState.Default : MenuControlState.Disabled);
        if (Layout.AlternatePanes)
            Button(ctx, "talent:details", session.Current.InspectorOnly ? "talent-graph" : "talent-details", action with { X = action.X + 2 * (width + 4), Width = width });
        else
            Text(ctx, page.Pending ? T("talent-applying") : page.Feedback.Length > 0 ? page.Feedback : T("talent-input-short"),
                new(b.X, action.Y, b.Width, action.Height), 14, page.FeedbackError ? PlayerMenuTheme.Danger : PlayerMenuTheme.Muted);
    }

    private static string TalentState(TalentGraphNodeState state) => T("talent-state-" + state.ToString().ToLowerInvariant());
    private void DrawTalentInspector(Context ctx)
    {
        var page = hub!.Talents; var b = Layout.Inspector;
        PlayerMenuDrawing.DrawWell(ctx, b);
        var lines = new List<(string Text, double Size)>();
        if (!hub.TalentTreeReady) lines.Add((T("talent-loading"), 16));
        if (page.Pending) lines.Add((T("talent-applying"), 16));
        else if (page.Feedback.Length > 0) lines.Add((page.Feedback, 16));
        if (page.Graph.SelectedNode is { } node)
        {
            lines.Add((string.IsNullOrWhiteSpace(node.Name) ? T("talent-node") : node.Name, 20));
            lines.Add((TalentState(page.Graph.StateFor(node)) + " · " + (node.Starter ? T("talent-free") : T(page.Cost(node.Code) == 1 ? "talent-cost-one" : "talent-cost", page.Cost(node.Code))), 15));
            lines.Add((T("talent-points", page.PlannedPoints, page.RemainingRespec), 15));
            if (page.Changes > 0 && page.CanEdit && !page.CanApply && page.Feedback.Length == 0) lines.Add((T("talent-review-plan"), 15));
            lines.Add((T("talent-effects"), 17));
            if (node.Modifiers.Length == 0) lines.Add((T("talent-no-effects"), 15));
            foreach (string modifier in node.Modifiers) lines.Add((modifier.Replace("vrpg:", "", StringComparison.OrdinalIgnoreCase).Replace('_', ' '), 16));
            if (!string.IsNullOrWhiteSpace(node.Description) && !string.Equals(node.Description.Trim(), "Unassigned talent node.", StringComparison.OrdinalIgnoreCase)) lines.Add((node.Description, 15));
        }
        else lines.Add((T(hub.TalentTree == null ? "talent-loading" : "talent-empty"), 17));
        lines.Add((T("talent-input"), 14));
        lines.Add((T("talent-legend"), 17));
        foreach (var state in Enum.GetValues<TalentGraphNodeState>()) lines.Add((TalentState(state), 14));
        lines.Add((T("talent-free-start"), 14));
        var clip = b with { Height = Math.Max(0, b.Height - 44) };
        var measured = lines.Select(line => (line.Text, line.Size, Height: MeasureTextHeight(ctx, line.Text, b.Width - 28, line.Size))).ToArray();
        inspectorMaximumScroll = Math.Max(0, (int)Math.Ceiling(measured.Sum(l => l.Height + 8) + 20 - clip.Height));
        ctx.Save(); ctx.Rectangle(clip.X + 2, clip.Y + 2, clip.Width - 4, Math.Max(0, clip.Height - 4)); ctx.Clip();
        double y = clip.Y + 10 - Math.Clamp(session.Current.InspectorScroll, 0, inspectorMaximumScroll);
        foreach (var line in measured) { Text(ctx, line.Text, new(b.X + 14, y, b.Width - 28, line.Height), line.Size); y += line.Height + 8; }
        ctx.Restore();
        double buttonWidth = (b.Width - 24) / 3;
        bool undo = page.Graph.SelectedNode is { } selected && page.Graph.StateFor(selected) is TalentGraphNodeState.PendingAllocation or TalentGraphNodeState.PendingRefund;
        Button(ctx, "talent:allocate", undo ? "talent-undo" : "talent-allocate", new(b.X + 8, b.Bottom - 38, buttonWidth, 30), page.CanQueueSelected(false) ? MenuControlState.Default : MenuControlState.Disabled);
        Button(ctx, "talent:refund", "talent-refund", new(b.X + 12 + buttonWidth, b.Bottom - 38, buttonWidth, 30), !undo && page.CanQueueSelected(true) ? MenuControlState.Default : MenuControlState.Disabled);
        Button(ctx, "talent:read", session.Current.InspectorScroll >= inspectorMaximumScroll ? "talent-top" : "talent-more", new(b.X + 16 + 2 * buttonWidth, b.Bottom - 38, buttonWidth, 30), inspectorMaximumScroll > 0 ? MenuControlState.Default : MenuControlState.Disabled);
    }

    private bool HandleTalentIntent(string target)
    {
        if (!TalentsOpen) return false;
        var page = hub!.Talents;
        switch (target)
        {
            case "talent:apply": page.Apply(); break;
            case "talent:reset": page.ResetDraft(); break;
            case "talent:details": session.Update(session.Current with { InspectorOnly = !session.Current.InspectorOnly, InspectorScroll = 0 }); break;
            case "talent:allocate": if (page.Graph.SelectedNode is { } allocate) page.QueueAllocation(allocate.Code); break;
            case "talent:refund": if (page.Graph.SelectedNode is { } refund) page.QueueRefund(refund.Code); break;
            case "talent:graph": break;
            case "talent:read": session.Update(session.Current with { InspectorScroll = session.Current.InspectorScroll >= inspectorMaximumScroll ? 0 : Math.Min(inspectorMaximumScroll, session.Current.InspectorScroll + 96) }); break;
            case "talent:editor": if (page.CanEditTree) hub.Actions.OpenTalentEditor(); break;
            default: return false;
        }
        return true;
    }

    private bool BeginTalentDrag(MouseEvent args)
    {
        if (!TalentGraphOpen || !hub!.TalentTreeReady || args.Button is not (EnumMouseButton.Left or EnumMouseButton.Right)) return false;
        double x = (args.X - Bounds.absX) / RuntimeEnv.GUIScale, y = (args.Y - Bounds.absY) / RuntimeEnv.GUIScale;
        if (!TalentWell.Contains(x, y)) return false;
        talentDragging = true; talentMoved = false; talentButton = args.Button;
        talentStartX = talentLastX = x; talentStartY = talentLastY = y; focus = "talent:graph";
        args.Handled = true; return true;
    }
    private bool MoveTalentDrag(MouseEvent args)
    {
        if (!talentDragging) return false;
        double x = (args.X - Bounds.absX) / RuntimeEnv.GUIScale, y = (args.Y - Bounds.absY) / RuntimeEnv.GUIScale;
        talentMoved |= Math.Abs(x - talentStartX) > 1 || Math.Abs(y - talentStartY) > 1;
        if (TalentGraphOpen && talentMoved) hub!.Talents.Pan(x - talentLastX, y - talentLastY, TalentWell.Width, TalentWell.Height);
        talentLastX = x; talentLastY = y; args.Handled = true; Refresh(); return true;
    }
    private bool EndTalentDrag(MouseEvent args)
    {
        if (!talentDragging) return false;
        talentDragging = false;
        double x = (args.X - Bounds.absX) / RuntimeEnv.GUIScale, y = (args.Y - Bounds.absY) / RuntimeEnv.GUIScale;
        bool moved = talentMoved || Math.Abs(x - talentStartX) > 1 || Math.Abs(y - talentStartY) > 1;
        if (TalentGraphOpen && !moved && args.Button == talentButton && TalentWell.Contains(x, y))
        { hub!.Talents.SelectAt(x, y, args.Button); session.Update(session.Current with { InspectorScroll = 0, Focus = "talent:graph" }); }
        args.Handled = true; Refresh(); return true;
    }
    /// <summary>Consumes logical pointer coordinates; only the graph owns wheel zoom.</summary>
    public bool TalentWheel(int direction, double x, double y)
    {
        if (!TalentGraphOpen || !hub!.TalentTreeReady || !TalentWell.Contains(x, y)) return false;
        hub!.Talents.ZoomAt(direction, x - TalentWell.X - TalentWell.Width / 2, y - TalentWell.Y - TalentWell.Height / 2, TalentWell.Width, TalentWell.Height);
        Refresh(); return true;
    }
    private bool HandleTalentKey(KeyEvent args)
    {
        if (!TalentsOpen) return false;
        if (focus == "talent:graph" && TalentGraphOpen && hub!.TalentTreeReady && args.KeyCode is (int)GlKeys.Left or (int)GlKeys.Right)
        {
            var graph = hub!.Talents.Graph; var nodes = graph.Snapshot.Nodes;
            if (nodes.Length > 0)
            { int index = Array.IndexOf(nodes, graph.SelectedNode); graph.Select(nodes[(index + (args.KeyCode == (int)GlKeys.Right ? 1 : nodes.Length - 1)) % nodes.Length].Code); hub.Talents.SaveView(); session.Update(session.Current with { InspectorScroll = 0 }); }
        }
        else if (focus == "talent:graph" && args.KeyCode == (int)GlKeys.Enter && hub!.Talents.Graph.SelectedNode is { } node) hub.Talents.QueueAllocation(node.Code);
        else if ((!Layout.AlternatePanes || session.Current.InspectorOnly) && args.KeyCode is (int)GlKeys.PageDown or (int)GlKeys.PageUp)
            session.Update(session.Current with { InspectorScroll = Math.Clamp(session.Current.InspectorScroll + (args.KeyCode == (int)GlKeys.PageDown ? 96 : -96), 0, inspectorMaximumScroll) });
        else return false;
        args.Handled = true; Refresh(); return true;
    }
}
