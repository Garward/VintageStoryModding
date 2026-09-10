using System;
using System.Linq;
using Cairo;
using VRPG.Client.PlayerMenu.Components;
using VRPG.Parties;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace VRPG.Client.PlayerMenu.Shell;

public sealed partial class PlayerMenuShell
{
    private bool PartyOpen => party?.State.IsOpen == true;
    private MenuRect PartyHeaderBounds => new(Layout.Close.X - 84, 10, 78, 34);
    public MenuRect PartySearchBounds => new(PartyListBounds.X + 8, PartyListBounds.Y + 40, Math.Max(0, PartyListBounds.Width - 96), 30);
    private MenuRect PartyBody => new(Layout.Back.X, Layout.AlternatePanes ? 134 : 96,
        Layout.Width - 2 * Layout.Back.X, Math.Max(0, Layout.Height - (Layout.AlternatePanes ? 134 : 96) - 74));
    private MenuRect PartyListBounds => PartyBody with { Width = Layout.AlternatePanes ? PartyBody.Width : PartyBody.Width * .52 - 12 };
    private MenuRect PartyDetailBounds => Layout.AlternatePanes ? PartyBody : PartyBody with { X = PartyBody.X + PartyBody.Width * .52 + 12, Width = PartyBody.Width * .48 - 12 };
    private MenuRect partyClip;
    private int partyRowsVisible, partyRowsCount, partyDetailMaximum;
    private static string PersonName(string? name) => string.IsNullOrWhiteSpace(name) ? T("party-name-unavailable") : name;
    private (string Id, string Name, string Status)[] PartyRows() => party!.State.Section switch
    {
        "invitations" => party.View?.IncomingInvitations.Select(i => (i.InvitationId, PersonName(i.LeaderName), T("party-invitation"))).ToArray() ?? Array.Empty<(string, string, string)>(),
        "online" => party.View?.Targets.Select(t => (t.Uid, PersonName(t.DisplayName), T("party-online"))).ToArray() ?? Array.Empty<(string, string, string)>(),
        _ => party.View?.Members.Select(m => (m.Uid, PersonName(m.DisplayName),
            (m.Uid == party.View.LeaderUid ? T("party-leader") + "  " : "") + T(m.IsOnline ? "party-online" : "party-offline"))).ToArray() ?? Array.Empty<(string, string, string)>()
    };
    private void DrawParty(Context ctx)
    {
        var panel = party!; var state = panel.State; var view = panel.View;
        bool details = !Layout.AlternatePanes || state.ShowDetails || state.ConfirmDisband;
        bool list = !Layout.AlternatePanes || !details;
        if (list) DrawPartyList(ctx);
        if (details) DrawPartyDetail(ctx);
        var footer = new MenuRect(PartyBody.X, Layout.Height - 62, PartyBody.Width, 48);
        string[] actions;
        if (state.ConfirmDisband) actions = new[] { "confirm-disband", "cancel" };
        else if (view == null || view.Health != PartyHealth.Healthy) actions = new[] { "refresh" };
        else if (!details && Layout.AlternatePanes) actions = state.Section == "online" ? new[] { "previous", "next" } : view.PartyId == null ? new[] { "create", "refresh" } : new[] { "refresh" };
        else if (state.Section == "invitations" && state.SelectedId.Length > 0) actions = new[] { "accept", "decline" };
        else if (state.Section == "online" && state.SelectedId.Length > 0) actions = new[] { "invite" };
        else if (state.Section == "online" && !Layout.AlternatePanes) actions = Array.Empty<string>();
        else if (view.PartyId == null) actions = new[] { "create", "refresh" };
        else if (state.SelectedId == view.SelfUid) actions = panel.IsLeader ? new[] { "leave", "disband" } : new[] { "leave" };
        else if (state.SelectedId.Length > 0 && panel.IsLeader) actions = new[] { "remove", "transfer" };
        else actions = new[] { "refresh" };
        if (!Layout.AlternatePanes) footer = footer with { X = PartyDetailBounds.X, Width = PartyDetailBounds.Width };
        double actionWidth = actions.Length == 0 ? 0 : (footer.Width - 6 * (actions.Length - 1)) / actions.Length;
        for (int i = 0; i < actions.Length; i++)
        {
            string action = actions[i];
            string reason = action is "refresh" or "cancel" ? "" : action == "previous" ? (view?.Page > 0 ? "" : "disabled")
                : action == "next" ? (view?.HasMoreTargets == true ? "" : "disabled") : panel.DisabledReason(action);
            Button(ctx, "party:" + action, "party-" + action,
                new(footer.X + i * (actionWidth + 6), footer.Y, actionWidth, footer.Height), reason.Length == 0 ? MenuControlState.Default : MenuControlState.Disabled);
        }
        if (!Layout.AlternatePanes && state.Section == "online" && view?.Health == PartyHealth.Healthy)
        {
            var b = PartyListBounds; double w = (b.Width - 12) / 3;
            Button(ctx, "party:previous", "party-previous", new(b.X, footer.Y, w, 48), view?.Page > 0 ? MenuControlState.Default : MenuControlState.Disabled);
            Button(ctx, "party:next", "party-next", new(b.X + w + 6, footer.Y, w, 48), view?.HasMoreTargets == true ? MenuControlState.Default : MenuControlState.Disabled);
            Button(ctx, "party:refresh", "party-refresh", new(b.X + 2 * (w + 6), footer.Y, w, 48));
        }
    }
    private void DrawPartyList(Context ctx)
    {
        var panel = party!; var state = panel.State; var b = PartyListBounds;
        Fill(ctx, b, PlayerMenuTheme.Iron950);
        string[] sections = { "members", "invitations", "online" };
        double tabWidth = (b.Width - 16) / 3;
        for (int i = 0; i < sections.Length; i++) Button(ctx, "party:section:" + sections[i], "party-" + sections[i],
            new(b.X + 8 + i * tabWidth, b.Y + 2, tabWidth - 4, 34), state.Section == sections[i] ? MenuControlState.Selected : MenuControlState.Default);
        if (panel.View?.Health != PartyHealth.Healthy)
        {
            Text(ctx, T(panel.View == null ? "party-loading" : "party-unavailable"), new(b.X + 8, b.Y + 40, b.Width - 16, b.Height - 48), 14, PlayerMenuTheme.Muted);
            return;
        }
        double start = b.Y + 64;
        if (state.Section == "online")
        {
            controls.Add(("party:search", PartySearchBounds, new("party:search", "party-search", MenuControlState.Default)));
            Button(ctx, "party:find", "party-find", new(b.Right - 80, b.Y + 40, 72, 30));
            start = b.Y + 76;
        }
        else
        {
            string summary = panel.View == null ? T("party-loading") : panel.View.Health != PartyHealth.Healthy ? T("party-unavailable")
                : state.Section == "invitations" ? T("party-invite-count", panel.View.IncomingInvitations.Count)
                : panel.View.PartyId == null ? T("party-no-party") : T("party-count", panel.View.Members.Count);
            if (Layout.AlternatePanes && panel.View?.Health == PartyHealth.Healthy && panel.FeedbackKey.Length > 0) summary = T(panel.FeedbackKey);
            Text(ctx, summary, new(b.X + 8, b.Y + 40, b.Width - 16, 22), 14, PlayerMenuTheme.Muted);
        }
        partyClip = new(b.X + 8, start, b.Width - 16, Math.Max(0, b.Bottom - start - 4));
        var rows = PartyRows(); partyRowsCount = rows.Length; partyRowsVisible = Math.Max(1, (int)(partyClip.Height / 50));
        int scroll = Math.Clamp(state.Scroll, 0, Math.Max(0, rows.Length - partyRowsVisible));
        ctx.Save(); ctx.Rectangle(partyClip.X, partyClip.Y, partyClip.Width, partyClip.Height); ctx.Clip();
        if (rows.Length == 0 && panel.View?.Health == PartyHealth.Healthy)
            Text(ctx, T(state.Section == "online" ? "party-no-targets" : state.Section == "invitations" ? "party-no-invitations" : "party-create-help"), partyClip, 14, PlayerMenuTheme.Muted);
        foreach (var (row, index) in rows.Skip(scroll).Take(partyRowsVisible).Select((r, i) => (r, i)))
        {
            var rect = new MenuRect(partyClip.X, partyClip.Y + index * 50, partyClip.Width, 46);
            string id = "party:select:" + row.Id;
            Button(ctx, id, "", rect, state.SelectedId == row.Id ? MenuControlState.Selected : MenuControlState.Default);
            Text(ctx, Ellipsis(ctx, row.Name, rect.Width - 20), new(rect.X + 10, rect.Y + 3, rect.Width - 20, 21), 15);
            Text(ctx, row.Status, new(rect.X + 10, rect.Y + 25, rect.Width - 20, 19), 14, PlayerMenuTheme.Muted);
        }
        ctx.Restore();
        controls.RemoveAll(c => c.Id.StartsWith("party:select:") && c.Bounds.Bottom > partyClip.Bottom);
        if (rows.Length > partyRowsVisible) DrawPartyScroll(ctx, b, scroll, rows.Length - partyRowsVisible);
    }
    private void DrawPartyDetail(Context ctx)
    {
        var panel = party!; var state = panel.State; var b = PartyDetailBounds;
        Fill(ctx, b, PlayerMenuTheme.Iron950);
        ctx.Save(); ctx.Rectangle(b.X, b.Y, b.Width, b.Height); ctx.Clip();
        double y = b.Y + 8 - state.DetailScroll, x = b.X + 12, width = b.Width - 24;
        void Line(string text, double size = 15, string color = PlayerMenuTheme.Text)
        { double h = MeasureTextHeight(ctx, text, width, size); Text(ctx, text, new(x, y, width, h), size, color); y += h + 8; }
        var selected = PartyRows().FirstOrDefault(r => r.Id == state.SelectedId);
        if (state.ConfirmDisband) Line(T("party-disband-warning"), 18);
        else if (panel.View == null) Line(T("party-loading"), 18);
        else if (panel.View.Health != PartyHealth.Healthy) Line(T("party-unavailable"), 18);
        else if (selected.Id != null) { Line(selected.Name, 18); Line(selected.Status, 14, PlayerMenuTheme.Muted); }
        else Line(T(panel.View.PartyId == null ? "party-create-help" : "party-select"), 18);
        if (panel.View?.PartyId != null && !panel.IsLeader && state.Section != "invitations") Line(T("party-leader-required"), 14, PlayerMenuTheme.Muted);
        string[] possible = state.Section == "online" ? new[] { "invite" } : state.Section == "invitations" ? new[] { "accept" }
            : state.SelectedId == panel.View?.SelfUid ? new[] { "leave" } : new[] { "transfer" };
        if (selected.Id != null && !state.ConfirmDisband)
            foreach (string reason in possible.Select(panel.DisabledReason).Where(r => r.Length > 0 && r != "party-pending" && r != "party-leader-required").Distinct()) Line(T(reason), 14, PlayerMenuTheme.Muted);
        if (panel.FeedbackKey.Length > 0) Line(T(panel.FeedbackKey), 14, panel.Pending ? PlayerMenuTheme.Muted : PlayerMenuTheme.Text);
        partyDetailMaximum = Math.Max(0, (int)(y + state.DetailScroll - b.Bottom));
        ctx.Restore();
        if (partyDetailMaximum > 0) DrawPartyScroll(ctx, b, state.DetailScroll, partyDetailMaximum);
    }
    private static void DrawPartyScroll(Context ctx, MenuRect b, int offset, int maximum)
    {
        double track = Math.Max(0, b.Height - 12), thumb = Math.Min(28, track);
        Fill(ctx, new(b.Right - 5, b.Y + 6, 2, track), PlayerMenuTheme.Iron700);
        Fill(ctx, new(b.Right - 5, b.Y + 6 + (track - thumb) * Math.Clamp(offset / (double)maximum, 0, 1), 2, thumb), PlayerMenuTheme.CopperWorn);
    }
    private static string Ellipsis(Context ctx, string value, double width)
    {
        ctx.SelectFontFace(PlayerMenuTheme.Font, FontSlant.Normal, FontWeight.Normal); ctx.SetFontSize(15);
        if (ctx.TextExtents(value).Width <= width) return value;
        int count = value.Length;
        while (count > 0 && ctx.TextExtents(value[..count] + "…").Width > width) count--;
        return value[..count] + "…";
    }
    private bool HandlePartyKey(KeyEvent args)
    {
        if (args.KeyCode is not ((int)GlKeys.PageDown) and not ((int)GlKeys.PageUp) and not ((int)GlKeys.Down) and not ((int)GlKeys.Up)) return false;
        bool detail = Layout.AlternatePanes ? party!.State.ShowDetails : !focus.StartsWith("party:select:");
        int sign = args.KeyCode is (int)GlKeys.PageDown or (int)GlKeys.Down ? 1 : -1;
        party!.Scroll(sign * (detail ? 32 : 1), detail ? partyDetailMaximum : partyRowsCount - partyRowsVisible, detail);
        args.Handled = true; Refresh(); return true;
    }
    private void ScrollParty(int amount, int x, int y)
    {
        bool detail = Layout.AlternatePanes ? party!.State.ShowDetails : PartyDetailBounds.Contains((x - Bounds.absX) / RuntimeEnv.GUIScale, (y - Bounds.absY) / RuntimeEnv.GUIScale);
        party!.Scroll(amount * (detail ? 32 : 1), detail ? partyDetailMaximum : partyRowsCount - partyRowsVisible, detail);
    }
}
