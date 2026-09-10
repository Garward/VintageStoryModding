using System;
using System.Linq;
using VRPG.Parties;

namespace VRPG.Client.PlayerMenu.Party;

public sealed class PartyPanel : IDisposable
{
    private readonly PartyClientService client;
    private string localFeedback = "";
    private bool hadPending, mutationSucceeded;
    private PartyClientView? observedView;
    public event Action? Changed;
    public PartyPanelState State { get; } = new();
    public PartyClientView? View => client.View;
    public bool IsLeader => View?.PartyId != null && View.SelfUid == View.LeaderUid;
    public bool Pending => client.Pending != null;
    public bool ShowSearch(bool alternatePanes) => State.IsOpen && View?.Health == PartyHealth.Healthy
        && State.Section == "online" && (!alternatePanes || !State.ShowDetails);
    public PartyPanel(PartyClientService client) { this.client = client; observedView = client.View; hadPending = client.Pending != null; client.Changed += OnChanged; }
    public string FeedbackKey => State.ConfirmDisband ? "" : Pending ? "party-pending" : localFeedback.Length > 0 ? localFeedback
        : client.LastResultCode == "ok" ? (mutationSucceeded ? "party-ok" : "") : ResultKey(client.LastResultCode);
    public void Open(PlayerMenuViewState context)
    {
        if (!State.IsOpen) State.ReturnContext = context;
        State.IsOpen = true; client.RequestView();
    }
    public PlayerMenuViewState? Close()
    {
        var context = State.ReturnContext;
        State.IsOpen = false; State.ReturnContext = null; State.ShowDetails = State.ConfirmDisband = false;
        return context;
    }
    public bool Back()
    {
        if (State.ConfirmDisband) { State.ConfirmDisband = false; return true; }
        if (State.ShowDetails) { State.ShowDetails = false; return true; }
        return false;
    }
    private void OnChanged()
    {
        localFeedback = "";
        if (hadPending && !Pending) mutationSucceeded = client.LastResultCode == "ok";
        hadPending = Pending;
        if (View == null)
        {
            Close(); State.SelectedId = State.Search = ""; State.Scroll = State.DetailScroll = 0; State.Section = "members";
            mutationSucceeded = false;
        }
        else if (!ReferenceEquals(observedView, View) && State.SelectedId.Length > 0)
        {
            bool stillPresent = State.Section switch
            {
                "invitations" => View.IncomingInvitations.Any(i => i.InvitationId == State.SelectedId),
                "online" => View.Targets.Any(t => t.Uid == State.SelectedId),
                _ => View.Members.Any(m => m.Uid == State.SelectedId)
            };
            if (!stillPresent) State.SelectedId = "";
        }
        observedView = View;
        Changed?.Invoke();
    }
    public string DisabledReason(string intent)
    {
        if (View == null) return "party-loading";
        if (View.Health != PartyHealth.Healthy) return "party-unavailable";
        if (Pending) return "party-pending";
        bool hasParty = View.PartyId != null;
        var member = View.Members.FirstOrDefault(m => m.Uid == State.SelectedId);
        var invitation = View.IncomingInvitations.FirstOrDefault(i => i.InvitationId == State.SelectedId);
        if (intent == "create") return hasParty ? "party-already-member" : "";
        if (intent is "accept" or "decline")
        {
            if (hasParty && intent == "accept") return "party-already-member";
            if (invitation == null) return "party-invitation-missing";
            return invitation.ExpiresAt <= DateTimeOffset.UtcNow ? "party-invitation-expired" : "";
        }
        if (!hasParty) return "party-no-party";
        if (intent == "leave") return IsLeader && View.Members.Count > 1 ? "party-transfer-required" : "";
        if (!IsLeader) return "party-leader-required";
        if (intent is "disband" or "confirm-disband") return "";
        if (intent == "invite")
        {
            if (View.Members.Count >= 8) return "party-party-full";
            if (View.Members.Any(m => m.Uid == State.SelectedId)) return "party-already-member";
            return View.Targets.Any(t => t.Uid == State.SelectedId) ? "" : "party-invalid-target";
        }
        if (member == null || member.Uid == View.SelfUid) return "party-invalid-target";
        return "";
    }
    public bool Handle(string intent)
    {
        if (intent.StartsWith("select:", StringComparison.Ordinal)) { Select(intent[7..]); return true; }
        if (intent.StartsWith("section:", StringComparison.Ordinal))
        {
            string section = intent[8..];
            if (section is not ("members" or "invitations" or "online")) return false;
            State.Section = section; State.SelectedId = ""; State.Scroll = State.DetailScroll = 0;
            State.ShowDetails = State.ConfirmDisband = false;
            if (section == "online") client.RequestView(State.Search, 0, true);
            Changed?.Invoke(); return true;
        }
        if (intent == "refresh") { client.RequestView(); return true; }
        if (intent == "find") { Search(State.Search); return true; }
        if (intent is "next" or "previous")
        {
            if (View != null && (intent == "previous" ? View.Page > 0 : View.HasMoreTargets))
                client.RequestView(State.Search, intent == "previous" ? View.Page - 1 : View.Page + 1, true);
            return true;
        }
        if (intent == "cancel") { State.ConfirmDisband = false; Changed?.Invoke(); return true; }
        PartyAction? action = intent switch { "create" => PartyAction.Create, "invite" => PartyAction.Invite,
            "accept" => PartyAction.Accept, "decline" => PartyAction.Decline, "leave" => PartyAction.Leave,
            "remove" => PartyAction.Remove, "transfer" => PartyAction.TransferLeader,
            "disband" or "confirm-disband" => PartyAction.Disband, _ => null };
        if (action == null) return false;
        localFeedback = DisabledReason(intent);
        if (localFeedback.Length > 0) { Changed?.Invoke(); return true; }
        if (intent == "disband") { mutationSucceeded = false; State.ConfirmDisband = true; Changed?.Invoke(); return true; }
        if (intent == "confirm-disband" && !State.ConfirmDisband) return true;
        var command = new PartyCommand(Guid.NewGuid().ToString("N"), View!.Revision, action.Value,
            action is PartyAction.Invite or PartyAction.Remove or PartyAction.TransferLeader ? State.SelectedId : "",
            action is PartyAction.Accept or PartyAction.Decline ? State.SelectedId : "");
        if (!client.Send(command)) localFeedback = "party-refresh-required";
        State.ConfirmDisband = false; Changed?.Invoke(); return true;
    }
    public void Select(string id)
    {
        State.SelectedId = id; State.ShowDetails = true; State.DetailScroll = 0; State.ConfirmDisband = false;
        Changed?.Invoke();
    }
    public void Search(string text)
    {
        SetSearchText(text); State.Scroll = 0;
        client.RequestView(State.Search, 0, true);
    }
    public void SetSearchText(string text) => State.Search = new string((text ?? "").Where(c => !char.IsControl(c)).Take(64).ToArray());
    public void Scroll(int amount, int maximum, bool detail)
    {
        if (detail) State.DetailScroll = Math.Clamp(State.DetailScroll + amount, 0, Math.Max(0, maximum));
        else State.Scroll = Math.Clamp(State.Scroll + amount, 0, Math.Max(0, maximum));
    }
    public static string ResultKey(string code) => code switch
    {
        "" => "", "ok" => "party-ok",
        "invalid-command" or "unavailable" or "commit-in-progress" or "request-conflict" or "stale-revision" or
        "already-member" or "invitation-missing" or "invitation-expired" or "party-full" or "no-party" or
        "transfer-required" or "leader-required" or "target-offline" or "already-invited" or "invitation-limit" or
        "invalid-target" or "invalid-query" or "rate-limit" or "invite-rate-limit" or "refresh-required" or "stale-party" => "party-" + code,
        _ => "party-error"
    };
    public void Dispose() { client.Changed -= OnChanged; Changed = null; }
}
