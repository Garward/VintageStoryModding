using System;
using System.Collections.Generic;
using System.Linq;
using ProtoBuf;
using VRPG.Inventory.Networking;
using VRPG.Inventory.Queries;
using VRPG.Network;
using Vintagestory.API.Client;

namespace VRPG.Client.PlayerMenu.Inventory;

/// <summary>Connection-owned state; the GUI owns neither pending mutations nor network callbacks.</summary>
public sealed class PlayerItemClientService
{
    public const int PageCacheCapacity = 8;
    public const int DetailCacheCapacity = 32;
    public const int DebounceMilliseconds = 200;
    private readonly Dictionary<long, PlayerItemQueryResult> pages = new();
    private readonly Dictionary<long, PlayerItemDetailResult> details = new();
    private readonly Dictionary<string, PlayerItemActionRequest> mutations = new(StringComparer.Ordinal);
    private readonly Queue<long> pageOrder = new(), detailOrder = new();
    private Action<PlayerItemQueryRequest>? sendQuery;
    private Action<PlayerItemDetailRequest>? sendDetail;
    private Action<PlayerItemActionRequest>? sendAction;
    private PlayerItemQueryRequest? queued;
    private PlayerItemDetailRequest? selected;
    private long queryId, latestPage, sequence;
    private double due;
    private bool refreshed;
    private PlayerItemQueryResult? page;
    private PlayerItemDetailResult? detail;
    private bool readOnlyTransport;

    public long ConnectionGeneration { get; private set; }
    public long StoreRevision { get; private set; }
    public long BuildRevision { get; private set; }
    public bool CanWrite => !readOnlyTransport && ConnectionGeneration > 0 && refreshed;
    public int PendingMutations => mutations.Count;
    public int CachedPages => pages.Count;
    public int CachedDetails => details.Count;
    public PlayerItemQueryResult? CurrentPage => page == null ? null : Serializer.DeepClone(page);
    public PlayerItemDetailResult? CurrentDetail => detail == null ? null : Serializer.DeepClone(detail);
    public event Action<PlayerItemActionResult>? ActionCompleted;
    public event Action<PlayerItemQueryResult>? PageReceived;
    public event Action<PlayerItemDetailResult>? DetailReceived;
    public event Action<string>? QueryRejected;
    public event Action? Invalidated;
    public event Action? ConnectionChanged;

    public void AttachReadOnly(Action<PlayerItemQueryRequest> query, Action<PlayerItemDetailRequest> detail)
    {
        if (ConnectionGeneration != 0 || sendQuery != null) throw new InvalidOperationException("Transport already attached.");
        readOnlyTransport = true; sendQuery = query; sendDetail = detail; sendAction = null;
    }

    public void Attach(IClientNetworkChannel channel)
    {
        if (readOnlyTransport) throw new InvalidOperationException("Read-only transport cannot become writable.");
        sendQuery = request => channel.SendPacket(request);
        sendDetail = request => channel.SendPacket(request);
        sendAction = request => channel.SendPacket(request);
        channel.SetMessageHandler<PlayerItemSessionPacket>(BeginSession);
        channel.SetMessageHandler<PlayerItemQueryResult>(packet => Receive(packet));
        channel.SetMessageHandler<PlayerItemDetailResult>(packet => Receive(packet));
        channel.SetMessageHandler<PlayerItemActionResult>(packet => Receive(packet));
        channel.SetMessageHandler<PlayerItemInvalidationPacket>(packet => Receive(packet));
    }

    public void ResetConnection()
    {
        ConnectionGeneration = StoreRevision = BuildRevision = 0;
        sequence = 0;
        refreshed = false;
        mutations.Clear();
        ClearQueries();
        ConnectionChanged?.Invoke();
    }

    public void BeginSession(PlayerItemSessionPacket packet)
    {
        if (packet.ProtocolVersion != 1 || packet.ConnectionGeneration <= ConnectionGeneration) return;
        ResetConnection();
        ConnectionGeneration = packet.ConnectionGeneration;
        ConnectionChanged?.Invoke();
    }

    /// <summary>Caller pumps on the client game thread. Replacing a request cancels its previous query generation.</summary>
    public long QueueQuery(PlayerItemQueryRequest request, double nowMilliseconds)
    {
        queued = Serializer.DeepClone(request);
        queued.ConnectionGeneration = ConnectionGeneration;
        queued.QueryId = latestPage = checked(++queryId);
        selected = null;
        detail = null;
        due = nowMilliseconds + DebounceMilliseconds;
        return latestPage;
    }

    public PlayerItemQueryRequest? Pump(double nowMilliseconds)
    {
        if (queued == null || nowMilliseconds < due || ConnectionGeneration <= 0) return null;
        var request = queued;
        queued = null;
        string rejection = PlayerItemQueryService.Validate(request);
        if (rejection.Length > 0) { QueryRejected?.Invoke(rejection); return null; }
        sendQuery?.Invoke(Serializer.DeepClone(request));
        return request;
    }

    /// <summary>An empty role asks the server to discover and compare the entry's actual equipment role.</summary>
    public PlayerItemDetailRequest Select(string entryId, string roleCode = "")
    {
        var request = new PlayerItemDetailRequest { ConnectionGeneration = ConnectionGeneration, QueryId = checked(++queryId),
            EntryId = entryId, RoleCode = roleCode };
        selected = request;
        detail = null;
        if (PlayerItemDetailService.Validate(request).Length == 0) sendDetail?.Invoke(Serializer.DeepClone(request));
        return Serializer.DeepClone(request);
    }

    public bool Receive(PlayerItemQueryResult result)
    {
        if (result.ConnectionGeneration == ConnectionGeneration && ConnectionGeneration > 0 && result.QueryId == latestPage
            && PlayerItemQueryService.Text(result.RejectionCode, 64, true))
        { QueryRejected?.Invoke(result.RejectionCode); return true; }
        if (result.ConnectionGeneration != ConnectionGeneration || ConnectionGeneration == 0
            || result.QueryId != latestPage || result.RejectionCode.Length > 0
            || result.Entries == null || result.Entries.Length > PlayerItemQueryService.MaximumEntries
            || result.Entries.Any(entry => !PlayerItemQueryService.ValidEntry(entry))
            || result.EquipmentEntries is not { Length: <= 128 } || result.EquipmentTotal < result.EquipmentEntries.Length
            || result.EquipmentEntries.Any(entry => !PlayerItemQueryService.ValidEntry(entry) || entry.Location != VRPG.Inventory.PlayerItemLocation.Equipped)
            || result.EquipmentEntries.Select(entry => entry.EntryId).Distinct(StringComparer.Ordinal).Count() != result.EquipmentEntries.Length
            || result.Total < result.Entries.Length || result.RecoveryCount < 0 || result.CategoryTotal < 0
            || result.Categories is not { Length: <= 128 } || result.Categories.Any(x => x == null || x.Count <= 0
                || !PlayerItemQueryService.Text(x.Code, 128, true) || !PlayerItemQueryService.Text(x.DisplayName, 256))
            || result.Categories.Select(x => x.Code).Distinct(StringComparer.Ordinal).Count() != result.Categories.Length
            || result.CoreCategories is not { Length: <= 5 } || result.CoreCategories.Any(x => x == null || x.Count < 0
                || !VRPG.Inventory.PlayerItemCategories.Required.Contains(x.Code) || !PlayerItemQueryService.Text(x.DisplayName, 256))
            || result.CoreCategories.Select(x => x.Code).Distinct(StringComparer.Ordinal).Count() != result.CoreCategories.Length
            || result.CapacitySummary == null || result.CapacitySummary.UniqueEntriesUsed < 0 || result.CapacitySummary.AggregateEntriesUsed < 0
            || result.CapacitySummary.UniqueEntryLimit < 0 || result.CapacitySummary.AggregateEntryLimit < 0 || result.CapacitySummary.AggregateQuantityPerEntry < 0
            || result.StoreRevision < StoreRevision || result.BuildRevision < BuildRevision) return false;
        Observe(result.StoreRevision, result.BuildRevision);
        page = Serializer.DeepClone(result);
        AddBounded(pages, pageOrder, result.QueryId, page, PageCacheCapacity);
        refreshed = true;
        PageReceived?.Invoke(Serializer.DeepClone(page));
        return true;
    }

    public bool Receive(PlayerItemDetailResult result)
    {
        // Admission errors have no store snapshot. Identity is still checked before publishing the failure.
        if (selected != null && result.ConnectionGeneration == ConnectionGeneration && ConnectionGeneration > 0
            && result.QueryId == selected.QueryId && result.EntryId == selected.EntryId
            && result.RoleCode == selected.RoleCode && result.StoreRevision == 0 && result.BuildRevision == 0
            && result.RejectionCode is "rate-limit" or "unauthorized-player" or "connection-generation" or "protocol-version" or "invalid-identity"
            && ValidDetail(result))
        { detail = Serializer.DeepClone(result); selected = null; DetailReceived?.Invoke(Serializer.DeepClone(detail)); return true; }
        if (selected == null || result.ConnectionGeneration != ConnectionGeneration || ConnectionGeneration == 0
            || result.QueryId != selected.QueryId || result.EntryId != selected.EntryId
            || !PlayerItemQueryService.Text(result.RoleCode, 128)
            || !string.IsNullOrEmpty(selected.RoleCode) && result.RoleCode != selected.RoleCode
            || result.StoreRevision < StoreRevision || result.BuildRevision < BuildRevision
            || result.Effects == null || result.Effects.Length > 128 || result.Comparison == null || result.Comparison.Length > 128
            || result.Actions == null || result.Actions.Length > 16 || !ValidDetail(result)) return false;
        Observe(result.StoreRevision, result.BuildRevision);
        detail = Serializer.DeepClone(result);
        selected = null;
        AddBounded(details, detailOrder, result.QueryId, detail, DetailCacheCapacity);
        DetailReceived?.Invoke(Serializer.DeepClone(detail));
        return true;
    }

    public PlayerItemActionRequest? Submit(PlayerItemActionRequest request)
    {
        if (!CanWrite || mutations.Count >= 128) return null;
        var copy = Serializer.DeepClone(request);
        copy.ConnectionGeneration = ConnectionGeneration;
        copy.ExpectedStoreRevision = StoreRevision;
        copy.ExpectedBuildRevision = BuildRevision;
        copy.RequestSequence = checked(++sequence);
        copy.RequestId = Guid.NewGuid().ToString("N");
        if (PlayerItemNetworkService.ValidateAction(copy).Length > 0) return null;
        mutations.Add(copy.RequestId, Serializer.DeepClone(copy));
        sendAction?.Invoke(Serializer.DeepClone(copy));
        return copy;
    }

    public bool Receive(PlayerItemActionResult result)
    {
        if (result.ConnectionGeneration != ConnectionGeneration || ConnectionGeneration == 0
            || !mutations.ContainsKey(result.RequestId) || result.ChangedEntries == null || result.ChangedEntries.Length > 128
            || result.ChangedEntries.Any(entry => !PlayerItemQueryService.ValidEntry(entry))
            || result.InvalidatedScopes == null || result.InvalidatedScopes.Length > 16
            || result.InvalidatedScopes.Any(scope => !PlayerItemQueryService.Text(scope, 64))) return false;
        mutations.Remove(result.RequestId);
        Observe(Math.Max(StoreRevision, result.StoreRevision), Math.Max(BuildRevision, result.BuildRevision));
        if (result.InvalidatedScopes.Length > 0) { refreshed = false; ClearCaches(); Invalidated?.Invoke(); }
        ActionCompleted?.Invoke(Serializer.DeepClone(result));
        return true;
    }

    public bool Receive(PlayerItemInvalidationPacket result)
    {
        if (result.ConnectionGeneration != ConnectionGeneration || ConnectionGeneration == 0
            || result.StoreRevision < StoreRevision || result.BuildRevision < BuildRevision
            || result.Scopes == null || result.Scopes.Length > 16
            || result.Scopes.Any(scope => !PlayerItemQueryService.Text(scope, 64))) return false;
        Observe(result.StoreRevision, result.BuildRevision);
        refreshed = false;
        ClearQueries();
        Invalidated?.Invoke();
        return true;
    }

    public void CloseMenu()
    {
        queued = null;
        selected = null;
        latestPage = checked(++queryId);
        detail = null;
    }

    private void Observe(long storeRevision, long buildRevision)
    {
        if (storeRevision != StoreRevision || buildRevision != BuildRevision)
        {
            refreshed = false;
            ClearCaches();
        }
        StoreRevision = storeRevision;
        BuildRevision = buildRevision;
    }

    private void ClearQueries() { CloseMenu(); ClearCaches(); }
    private void ClearCaches()
    {
        pages.Clear(); details.Clear(); pageOrder.Clear(); detailOrder.Clear(); page = null; detail = null;
    }

    private static void AddBounded<T>(Dictionary<long, T> cache, Queue<long> order, long key, T value, int capacity)
    {
        if (cache.ContainsKey(key)) { cache[key] = value; return; }
        if (cache.Count == capacity) cache.Remove(order.Dequeue());
        cache.Add(key, value);
        order.Enqueue(key);
    }

    private static bool ValidDetail(PlayerItemDetailResult result) => PlayerItemQueryService.Text(result.RejectionCode, 64)
        && result.Effects is { Length: <= 128 } && result.Comparison is { Length: <= 128 } && result.Actions is { Length: <= 16 }
        && result.Effects.All(x => x != null && double.IsFinite(x.Value) && PlayerItemQueryService.Text(x.StatCode, 128)
            && PlayerItemQueryService.Text(x.Operation, 32) && PlayerItemQueryService.Text(x.UnitCode, 32)
            && PlayerItemQueryService.Text(x.ConditionCode, 64))
        && result.Comparison.All(x => x != null && double.IsFinite(x.Before) && double.IsFinite(x.After)
            && double.IsFinite(x.Difference) && PlayerItemQueryService.Text(x.StatCode, 128)
            && PlayerItemQueryService.Text(x.UnitCode, 32) && PlayerItemQueryService.Text(x.ConditionCode, 64))
        && result.Actions.All(x => x != null && Enum.IsDefined(x.Action) && PlayerItemQueryService.Text(x.ReasonCode, 64))
        && result.GainedTransformations is { Length: <= 128 } && result.LostTransformations is { Length: <= 128 }
        && result.GainedTransformations.Concat(result.LostTransformations).All(x => PlayerItemQueryService.Text(x, 128));
}
