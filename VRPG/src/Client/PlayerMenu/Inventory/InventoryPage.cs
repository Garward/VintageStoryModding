using System;
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
    public event Action? Changed;
    private bool open;
    private double now, pageDeadline, detailDeadline;
    public InventoryPage(PlayerItemClientService items)
    {
        Items = items; items.PageReceived += OnPage; items.DetailReceived += OnDetail;
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
        bool same = Query == query; Query = query; open = true; now = milliseconds;
        View.Begin(same); Items.QueueQuery(Query.ToRequest(), milliseconds); pageDeadline = milliseconds + 10200; Changed?.Invoke();
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
    private void OnPage(PlayerItemQueryResult page)
    {
        if (!open) return; View.Apply(page);
        if (View.Selected != null) Select(View.SelectedId);
        Changed?.Invoke();
    }
    private void OnDetail(PlayerItemDetailResult detail)
    {
        if (!open || detail.EntryId != View.SelectedId) return;
        if (detail.StoreRevision > View.StoreRevision || detail.BuildRevision > View.BuildRevision)
        { Refresh(now); return; }
        View.Detail = detail; View.DetailLoading = false; View.DetailError = detail.RejectionCode; Changed?.Invoke();
    }
    private void OnRejected(string reason) { if (!open) return; View.Loading = false; View.Error = reason; Changed?.Invoke(); }
    private void OnInvalidated() { if (open) Refresh(now); else View.Stale = View.Capacity != null; }
    private void OnConnection() { View.Reset(); if (open) Refresh(now); Changed?.Invoke(); }
    public void Dispose()
    {
        Close(); Items.PageReceived -= OnPage; Items.DetailReceived -= OnDetail;
        Items.QueryRejected -= OnRejected; Items.Invalidated -= OnInvalidated; Items.ConnectionChanged -= OnConnection;
    }
}
