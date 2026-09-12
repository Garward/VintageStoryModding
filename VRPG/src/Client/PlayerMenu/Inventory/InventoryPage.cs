using System;
using System.Linq;
using VRPG.Config;
using VRPG.Data;
using VRPG.Inventory;
using VRPG.Inventory.Gear;
using VRPG.Inventory.Queries;
using VRPG.Modules.Rpg.Players;
using VRPG.Modules.Rpg.Talents;
using VRPG.Network;

namespace VRPG.Client.PlayerMenu.Inventory;

/// <summary>Browse lifecycle in the existing connection owner. The isolated store route has no mutation transport.</summary>
public sealed class InventoryPage : IDisposable
{
    public PlayerItemClientService Items { get; }
    public InventoryViewModel View { get; } = new();
    public InventoryQueryState Query { get; private set; } = new();
    public bool ActionPending => pendingActionRequestId.Length > 0;
    public string? PendingActionRequestId => pendingActionRequestId.Length == 0 ? null : pendingActionRequestId;
    public string ActionFeedback { get; private set; } = "";
    public bool ActionFeedbackIsError { get; private set; }
    public event Action? Changed;
    private bool open;
    private double now, pageDeadline, detailDeadline;
    private string pendingActionRequestId = "";
    public InventoryPage(PlayerItemClientService items)
    {
        Items = items; items.PageReceived += OnPage; items.DetailReceived += OnDetail; items.ActionCompleted += OnActionCompleted;
        items.QueryRejected += OnRejected; items.Invalidated += OnInvalidated; items.ConnectionChanged += OnConnection;
    }
    public static InventoryPage CreateReadOnlyTestStore(IPlayerItemStore store, VRPGDataRegistry? data = null)
    {
        var items = new PlayerItemClientService();
        var query = new PlayerItemQueryService();
        var talents = new TalentTreeCatalog();
        var details = new PlayerItemDetailService(new GearComparisonService(data ?? new(), new(talents), new(new RpgModuleConfig(), talents)));
        items.AttachReadOnly(request => items.Receive(query.Query(store, request)),
            request => items.Receive(details.Detail(store, new RpgPlayerState { Level = 20 }, 0, request)));
        items.BeginSession(new() { ConnectionGeneration = 1 });
        return new(items);
    }
    public void Open(double milliseconds) { open = true; Refresh(milliseconds); }
    public void Close() { open = false; Items.CloseMenu(); View.Loading = View.DetailLoading = false; }
    public void ChangeQuery(InventoryQueryState query, double milliseconds)
    {
        bool sameItems = Query.HasSameItems(query), auxiliaryOnly = sameItems && Query != query;
        Query = query; open = true; now = milliseconds;
        View.Begin(sameItems, auxiliaryOnly); Items.QueueQuery(Query.ToRequest(), milliseconds); pageDeadline = milliseconds + 10200; Changed?.Invoke();
    }
    public void Refresh(double milliseconds) => ChangeQuery(Query, milliseconds);
    public void NextPage(double milliseconds)
    { if (!View.Loading && Query.Offset + 64 < View.Total) ChangeQuery(Query with { Offset = checked(Query.Offset + 64) }, milliseconds); }
    public void PreviousPage(double milliseconds) => ChangeQuery(Query with { Offset = Math.Max(0, Query.Offset - 64) }, milliseconds);
    public void Pump(double milliseconds)
    {
        now = milliseconds; if (!open) return; Items.Pump(milliseconds);
        if (View.Loading && milliseconds >= pageDeadline) { View.Loading = false; View.Error = "timeout"; Changed?.Invoke(); }
        if (View.DetailLoading && milliseconds >= detailDeadline) { View.DetailLoading = false; View.DetailError = "timeout"; Changed?.Invoke(); }
    }
    public void Select(string id)
    {
        if (!View.Select(id)) return;
        detailDeadline = now + 10000; Items.Select(id); Changed?.Invoke();
    }
    /// <summary>Submits only the selected server-advertised equipment action and keeps its feedback at that control.</summary>
    public bool TrySubmitAction(PlayerItemAction action, double milliseconds)
    {
        now = milliseconds;
        if (ActionPending || View.Selected is not { } entry || View.Detail is not { } detail || detail.EntryId != entry.EntryId)
            return false;
        PlayerItemActionAvailability? availability = detail.Actions.FirstOrDefault(candidate => candidate.Action == action);
        if (availability == null || !availability.Enabled || action is not (PlayerItemAction.Equip or PlayerItemAction.Unequip))
        {
            ActionFeedback = availability?.ReasonCode ?? "action-unavailable";
            ActionFeedbackIsError = true;
            Changed?.Invoke();
            return false;
        }
        PlayerItemActionRequest? request = Items.Submit(new PlayerItemActionRequest
        {
            Action = action,
            EntryId = entry.EntryId,
            RoleCode = detail.RoleCode,
            Quantity = 1
        });
        if (request == null)
        {
            ActionFeedback = "action-unavailable";
            ActionFeedbackIsError = true;
            Changed?.Invoke();
            return false;
        }
        pendingActionRequestId = request.RequestId;
        ActionFeedback = "";
        ActionFeedbackIsError = false;
        Changed?.Invoke();
        return true;
    }
    private void OnPage(PlayerItemQueryResult page)
    {
        if (!open) return; View.Apply(page);
        if (View.Selected != null && (View.Detail == null || View.Detail.StoreRevision != page.StoreRevision
            || View.Detail.BuildRevision != page.BuildRevision)) Select(View.SelectedId);
        Changed?.Invoke();
    }
    private void OnDetail(PlayerItemDetailResult detail)
    {
        if (!open || detail.EntryId != View.SelectedId) return;
        if (detail.StoreRevision > View.StoreRevision || detail.BuildRevision > View.BuildRevision)
        { Refresh(now); return; }
        View.Detail = detail; View.DetailLoading = false; View.DetailError = detail.RejectionCode; Changed?.Invoke();
    }
    private void OnActionCompleted(PlayerItemActionResult result)
    {
        if (!string.Equals(result.RequestId, pendingActionRequestId, StringComparison.Ordinal)) return;
        pendingActionRequestId = "";
        if (result.Success)
        {
            ActionFeedback = "equipment-updated";
            ActionFeedbackIsError = false;
            RefreshIfOpen();
        }
        else if (result.RejectionCode is "stale-store" or "stale-build")
        {
            ActionFeedback = "stale";
            ActionFeedbackIsError = false;
            RefreshIfOpen();
        }
        else
        {
            ActionFeedback = result.RejectionCode.Length == 0 ? "action-unavailable" : result.RejectionCode;
            ActionFeedbackIsError = true;
        }
        Changed?.Invoke();
    }
    private void RefreshIfOpen()
    {
        if (open) Refresh(now);
        else View.Stale = View.Capacity != null;
    }
    private void OnRejected(string reason) { if (!open) return; View.Loading = false; View.Error = reason; Changed?.Invoke(); }
    private void OnInvalidated() { if (open) Refresh(now); else View.Stale = View.Capacity != null; }
    private void OnConnection() { pendingActionRequestId = ""; ActionFeedback = ""; ActionFeedbackIsError = false; View.Reset(); if (open) Refresh(now); Changed?.Invoke(); }
    public void Dispose()
    {
        Close(); Items.PageReceived -= OnPage; Items.DetailReceived -= OnDetail; Items.ActionCompleted -= OnActionCompleted;
        Items.QueryRejected -= OnRejected; Items.Invalidated -= OnInvalidated; Items.ConnectionChanged -= OnConnection;
    }
}
