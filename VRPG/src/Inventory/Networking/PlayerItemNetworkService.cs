using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using ProtoBuf;
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
    private readonly Func<double> seconds;
    private readonly Dictionary<string, Session> sessions = new(StringComparer.OrdinalIgnoreCase);
    private long generation;

    public PlayerItemNetworkService(PlayerItemStoreDirectory items, RpgPlayerStore players,
        PlayerItemQueryService queries, PlayerItemDetailService details, Func<double>? seconds = null)
    {
        this.items = items;
        this.players = players;
        this.queries = queries;
        this.details = details;
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
            QueryId = request.QueryId, RejectionCode = error };
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
        else if (!store.TryGet(request.EntryId, out var entry) || entry == null) result.RejectionCode = "entry-missing";
        else if (request.Quantity > entry.Quantity) result.RejectionCode = "insufficient-quantity";
        else result.RejectionCode = "unsupported-action";
        // This slice deliberately has no mutation dispatcher. Later action bridges must preserve all checks above.
        session.LastSequence = Math.Max(session.LastSequence, request.RequestSequence);
        if (session.Replay.Count == ReplayCapacity) session.Replay.Remove(session.Order.Dequeue());
        session.Replay[request.RequestId!] = (fingerprint, Serializer.DeepClone(result));
        session.Order.Enqueue(request.RequestId!);
        return result;
    }

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
