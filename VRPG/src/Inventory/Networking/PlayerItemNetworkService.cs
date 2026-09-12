using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using ProtoBuf;
using VRPG.Inventory.Gear;
using VRPG.Inventory.Queries;
using VRPG.Modules.Rpg.Players;
using VRPG.Network;
using Vintagestory.API.Server;

namespace VRPG.Inventory.Networking;

/// <summary>Connection authority and bounded request lifetime. Call only on the server game thread.</summary>
public sealed class PlayerItemNetworkService
{
    public const int RequestsPerSecond = 20;
    public const int ReplayCapacity = 128;
    private readonly PlayerItemStoreDirectory items;
    private readonly RpgPlayerStore players;
    private readonly PlayerItemQueryService queries;
    private readonly PlayerItemDetailService details;
    private readonly GearEquipmentService equipment;
    private readonly Func<double> seconds;
    private readonly Dictionary<string, Session> sessions = new(StringComparer.OrdinalIgnoreCase);
    private long generation;

    public PlayerItemNetworkService(PlayerItemStoreDirectory items, RpgPlayerStore players,
        PlayerItemQueryService queries, PlayerItemDetailService details, GearEquipmentService equipment,
        Func<double>? seconds = null)
    {
        this.items = items;
        this.players = players;
        this.queries = queries;
        this.details = details;
        this.equipment = equipment;
        this.seconds = seconds ?? (() => Environment.TickCount64 / 1000d);
    }

    /// <summary>Lifecycle owner calls on connect and sends this packet before accepting queries.</summary>
    public PlayerItemSessionPacket Connect(IServerPlayer player)
    {
        if (player == null || !PlayerItemQueryService.Text(player.PlayerUID, 128, true))
            throw new ArgumentException("A connected server player is required.", nameof(player));
        var session = new Session(player, checked(++generation));
        sessions[player.PlayerUID] = session;
        return new() { ConnectionGeneration = session.Generation };
    }

    public void Disconnect(IServerPlayer player)
    {
        if (sessions.TryGetValue(player.PlayerUID, out var session) && ReferenceEquals(session.Player, player))
            sessions.Remove(player.PlayerUID);
    }

    /// <summary>Registers handlers only; caller owns capability gating and connect/disconnect event attachment.</summary>
    public void Attach(IServerNetworkChannel channel)
    {
        channel.SetMessageHandler<PlayerItemQueryRequest>((p, r) => channel.SendPacket(Query(p, r), p));
        channel.SetMessageHandler<PlayerItemDetailRequest>((p, r) => channel.SendPacket(Detail(p, r), p));
        channel.SetMessageHandler<PlayerItemActionRequest>((p, r) => channel.SendPacket(Action(p, r), p));
    }

    public PlayerItemQueryResult Query(IServerPlayer player, PlayerItemQueryRequest request)
    {
        string error = PlayerItemQueryService.Validate(request);
        Session? session = null;
        if (error.Length == 0) error = Authorize(player, request.ConnectionGeneration, out session);
        if (error.Length > 0) return new() { ConnectionGeneration = request.ConnectionGeneration,
            QueryId = request.QueryId, RejectionCode = error };
        IPlayerItemStore store = items.ForPlayer(player.PlayerUID);
        long build = players.GetBuildRevision(player.PlayerUID, store.Equipment());
        var result = queries.Query(store, request, build);
        if (result.RejectionCode.Length == 0) session!.Refreshed = true;
        return result;
    }

    public PlayerItemDetailResult Detail(IServerPlayer player, PlayerItemDetailRequest request)
    {
        string error = PlayerItemDetailService.Validate(request);
        if (error.Length == 0) error = Authorize(player, request.ConnectionGeneration, out _);
        if (error.Length > 0) return new() { ConnectionGeneration = request.ConnectionGeneration,
            QueryId = request.QueryId, RejectionCode = error,
            EntryId = PlayerItemQueryService.Text(request.EntryId, 128, true) ? request.EntryId : "",
            RoleCode = PlayerItemQueryService.Text(request.RoleCode, 128) ? request.RoleCode : "" };
        IPlayerItemStore store = items.ForPlayer(player.PlayerUID);
        long build = players.GetBuildRevision(player.PlayerUID, store.Equipment());
        var result = details.Detail(store, players.GetOrCreate(player.PlayerUID), build, request);
        if (players.GetBuildRevision(player.PlayerUID, store.Equipment()) != build)
        {
            result.Comparison = Array.Empty<PlayerItemComparisonPacket>();
            result.RejectionCode = "stale-build";
        }
        return result;
    }

    public PlayerItemActionResult Action(IServerPlayer player, PlayerItemActionRequest request)
    {
        string error = ValidateAction(request);
        Session? session = null;
        if (error.Length == 0) error = Authorize(player, request.ConnectionGeneration, out session);
        var result = new PlayerItemActionResult { RequestId = PlayerItemQueryService.Bounded(request.RequestId ?? "", 128),
            ConnectionGeneration = request.ConnectionGeneration, RejectionCode = error };
        if (error.Length > 0) return result;
        IPlayerItemStore store = items.ForPlayer(player.PlayerUID);
        result.StoreRevision = store.Revision;
        result.BuildRevision = players.GetBuildRevision(player.PlayerUID, store.Equipment());
        using var bytes = new MemoryStream();
        Serializer.Serialize(bytes, request);
        string fingerprint = Convert.ToHexString(SHA256.HashData(bytes.GetBuffer().AsSpan(0, (int)bytes.Length)));
        if (session!.Replay.TryGetValue(request.RequestId!, out var prior))
        {
            if (prior.Fingerprint == fingerprint) return Serializer.DeepClone(prior.Result);
            result.RejectionCode = "request-id-conflict";
            return result;
        }
        if (request.RequestSequence <= session.LastSequence) result.RejectionCode = "request-expired";
        else if (!session.Refreshed) result.RejectionCode = "refresh-required";
        else if (request.ExpectedStoreRevision != result.StoreRevision) result.RejectionCode = "stale-store";
        else if (request.ExpectedBuildRevision != result.BuildRevision) result.RejectionCode = "stale-build";
        else DispatchEquipmentAction(player, request, store, result);
        session.LastSequence = Math.Max(session.LastSequence, request.RequestSequence);
        if (session.Replay.Count == ReplayCapacity) session.Replay.Remove(session.Order.Dequeue());
        session.Replay[request.RequestId!] = (fingerprint, Serializer.DeepClone(result));
        session.Order.Enqueue(request.RequestId!);
        return result;
    }

    private void DispatchEquipmentAction(
        IServerPlayer player,
        PlayerItemActionRequest request,
        IPlayerItemStore store,
        PlayerItemActionResult result)
    {
        if (request.Action is not (PlayerItemAction.Equip or PlayerItemAction.Unequip))
        {
            result.RejectionCode = "unsupported-action";
            return;
        }

        string priorEntryId = request.Action == PlayerItemAction.Equip
            ? store.Equipment().Entries.FirstOrDefault(entry => string.Equals(
                entry.RoleCode, request.RoleCode, StringComparison.Ordinal))?.EntryId ?? ""
            : "";
        PlayerItemOperationResult operation;
        if (request.Action == PlayerItemAction.Equip)
        {
            if (!store.TryGet(request.EntryId, out PlayerItemDetail? entry) || entry == null)
            {
                result.RejectionCode = "entry-missing";
                return;
            }
            operation = equipment.TryEquip(store, players.GetOrCreate(player.PlayerUID).Level,
                request.RequestId, entry.EntryId, request.RoleCode, request.ExpectedStoreRevision);
        }
        else
        {
            operation = store.Unequip(request.RequestId, request.RoleCode, request.ExpectedStoreRevision);
        }

        result.StoreRevision = operation.Revision;
        result.BuildRevision = players.GetBuildRevision(player.PlayerUID, store.Equipment());
        result.Success = operation.Success;
        result.RejectionCode = operation.Success ? "" : RejectionCode(operation.Rejection);
        if (!operation.Success) return;

        result.ChangedEntries = ChangedEntries(store, operation.EntryId, priorEntryId);
        result.InvalidatedScopes = new[] { "inventory", "equipment", "character" };
    }

    private static PlayerItemEntryPacket[] ChangedEntries(IPlayerItemStore store, params string[] entryIds)
    {
        var entries = new List<PlayerItemEntryPacket>(entryIds.Length);
        foreach (string entryId in entryIds)
        {
            if (entryId.Length == 0 || entries.Any(entry => string.Equals(entry.EntryId, entryId, StringComparison.OrdinalIgnoreCase)))
                continue;
            if (!store.TryGet(entryId, out PlayerItemDetail? detail) || detail == null) continue;
            entries.Add(PlayerItemQueryService.ToPacket(new PlayerItemSummary(
                detail.EntryId,
                detail.Prototype.Kind,
                detail.Prototype.AssetCode,
                detail.Prototype.Category,
                detail.Prototype.DisplayName,
                detail.Quantity,
                detail.Prototype.AssetKind,
                detail.Location,
                detail.Flags,
                0,
                detail.ProtectionSource,
                detail.Prototype.Category)));
        }
        return entries.ToArray();
    }

    private static string RejectionCode(PlayerItemOperationRejection rejection) => rejection switch
    {
        PlayerItemOperationRejection.StaleRevision => "stale-store",
        PlayerItemOperationRejection.RequestIdConflict => "request-id-conflict",
        PlayerItemOperationRejection.EntryMissing => "entry-missing",
        PlayerItemOperationRejection.EntryUnavailable => "entry-unavailable",
        PlayerItemOperationRejection.EntryReserved => "entry-reserved",
        PlayerItemOperationRejection.EntryProtected => "entry-protected",
        PlayerItemOperationRejection.InsufficientQuantity => "insufficient-quantity",
        PlayerItemOperationRejection.EquipmentRoleMissing => "equipment-role-mismatch",
        PlayerItemOperationRejection.NotEquippable => "not-equippable",
        PlayerItemOperationRejection.InvalidGear => "invalid-gear",
        PlayerItemOperationRejection.EquipmentRoleMismatch => "equipment-role-mismatch",
        PlayerItemOperationRejection.RequirementNotMet => "requirement-not-met",
        _ => "invalid-request"
    };

    public PlayerItemInvalidationPacket Invalidate(IServerPlayer player, params string[] scopes)
    {
        if (!sessions.TryGetValue(player.PlayerUID, out var session) || !ReferenceEquals(session.Player, player))
            throw new InvalidOperationException("Player is not connected to this service.");
        if (scopes.Length > 16 || Array.Exists(scopes, x => !PlayerItemQueryService.Text(x, 64, true)))
            throw new ArgumentException("Invalidation scopes must be bounded.", nameof(scopes));
        IPlayerItemStore store = items.ForPlayer(player.PlayerUID);
        session.Refreshed = false;
        return new() { ConnectionGeneration = session.Generation, StoreRevision = store.Revision,
            BuildRevision = players.GetBuildRevision(player.PlayerUID, store.Equipment()), Scopes = (string[])scopes.Clone() };
    }

    public static string ValidateAction(PlayerItemActionRequest request)
    {
        if (request.ProtocolVersion != 1) return "protocol-version";
        if (request.ConnectionGeneration <= 0 || request.RequestSequence <= 0
            || !PlayerItemQueryService.Text(request.RequestId, 128, true)
            || !PlayerItemQueryService.Text(request.EntryId, 128, true)
            || !PlayerItemQueryService.Text(request.RoleCode, 128)
            || !PlayerItemQueryService.Text(request.DestinationId, 128)
            || !PlayerItemQueryService.Text(request.PreviewToken, 256)) return "invalid-identity";
        if (!Enum.IsDefined(request.Action) || request.Action == PlayerItemAction.None) return "invalid-action";
        if (request.Quantity <= 0 || request.ExpectedStoreRevision < 0 || request.ExpectedBuildRevision < 0) return "invalid-quantity";
        if (request.Action is PlayerItemAction.Equip or PlayerItemAction.Unequip
            && (!PlayerEquipmentRoles.BuiltIn.Contains(request.RoleCode) || request.Quantity != 1)) return "equipment-role-mismatch";
        return "";
    }

    private string Authorize(IServerPlayer player, long requestGeneration, out Session? session)
    {
        session = null;
        if (player == null || string.IsNullOrWhiteSpace(player.PlayerUID)
            || !sessions.TryGetValue(player.PlayerUID, out session) || !ReferenceEquals(session.Player, player)) return "unauthorized-player";
        if (session.Generation != requestGeneration) return "connection-generation";
        double now = seconds();
        if (now - session.WindowStart >= 1) { session.WindowStart = now; session.Count = 0; }
        if (++session.Count > RequestsPerSecond) return "rate-limit";
        return "";
    }

    private sealed class Session(IServerPlayer player, long generation)
    {
        public IServerPlayer Player { get; } = player;
        public long Generation { get; } = generation;
        public bool Refreshed;
        public double WindowStart;
        public int Count;
        public long LastSequence;
        public readonly Dictionary<string, (string Fingerprint, PlayerItemActionResult Result)> Replay = new(StringComparer.Ordinal);
        public readonly Queue<string> Order = new();
    }
}
