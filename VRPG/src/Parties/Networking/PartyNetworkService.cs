using System;
using System.Collections.Generic;
using System.Linq;
using VRPG.Network;
using Vintagestory.API.Server;

namespace VRPG.Parties.Networking;

/// <summary>Authenticated connection transport. All calls belong on the server game thread.</summary>
public sealed class PartyNetworkService : IDisposable
{
    private readonly Func<PartyService?> service;
    private readonly Func<IEnumerable<IServerPlayer>> online;
    private readonly Func<string, string?> name;
    private readonly Func<DateTimeOffset> now;
    private readonly Func<double> seconds;
    private readonly Dictionary<string, Session> sessions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> invitationAttempts = new(StringComparer.Ordinal);
    private IServerNetworkChannel? channel;
    private bool disposed;

    public PartyNetworkService(Func<PartyService?> service, Func<IEnumerable<IServerPlayer>> online,
        Func<string, string?> name, Func<DateTimeOffset>? now = null, Func<double>? seconds = null)
    {
        this.service = service;
        this.online = online;
        this.name = name;
        this.now = now ?? (() => DateTimeOffset.UtcNow);
        this.seconds = seconds ?? (() => Environment.TickCount64 / 1000d);
    }

    public void Attach(IServerNetworkChannel channel)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (this.channel != null) throw new InvalidOperationException("Party handlers already attached.");
        this.channel = channel;
        channel.SetMessageHandler<PartyQueryPacket>(Query);
        channel.SetMessageHandler<PartyMutationPacket>(Mutate);
    }

    public void Connect(IServerPlayer player)
    {
        if (disposed || player == null || !PartyPacketRules.Identifier(player.PlayerUID)) return;
        if (sessions.TryGetValue(player.PlayerUID, out var prior) && ReferenceEquals(prior.Player, player))
        {
            InvalidatePresence(player.PlayerUID, prior);
            return;
        }
        var session = new Session(player, seconds());
        sessions[player.PlayerUID] = session;
        session.View = Read(player.PlayerUID);
        InvalidatePresence(player.PlayerUID, session);
    }

    public void Disconnect(IServerPlayer player)
    {
        if (!TrySession(player, out var session)) return;
        sessions.Remove(player.PlayerUID);
        InvalidatePresence(player.PlayerUID, session);
    }

    public bool IsOnline(string uid) => sessions.TryGetValue(uid, out var session) && Eligible(session.Player);

    private static bool Eligible(IServerPlayer player) => player.ConnectionState is EnumClientState.Connected or EnumClientState.Playing;

    private bool TrySession(IServerPlayer player, out Session session)
    {
        session = null!;
        return !disposed && player != null && PartyPacketRules.Identifier(player.PlayerUID)
            && sessions.TryGetValue(player.PlayerUID, out session!) && ReferenceEquals(session.Player, player);
    }

    private void Query(IServerPlayer player, PartyQueryPacket request)
    {
        if (!TrySession(player, out var session)) return;
        bool allowed = session.Take(seconds());
        if (request == null || !PartyPacketRules.Identifier(request.RequestId, 64)) return;
        bool valid = PartyPacketRules.Query(request);
        if (!valid) { SendSnapshot(session, request.RequestId, "invalid-query", read: false); return; }
        if (request.SessionId.Length != 0 && request.SessionId != session.Id) return;
        if (request.QuerySequence <= session.LastQuerySequence) return;
        if (request.SessionId.Length == 0)
            channel!.SendPacket(new PartySessionPacket { SessionId = session.Id, RequestId = request.RequestId }, player);
        session.Established = true;
        session.LastQuerySequence = request.QuerySequence;
        if (!allowed) { SendSnapshot(session, request.RequestId, "rate-limit", read: false); return; }
        session.Search = request.Search;
        session.Page = request.Page;
        session.IncludeTargets = request.IncludeTargets;
        SendSnapshot(session, request.RequestId);
        InvalidateHealth(session);
    }

    private void Mutate(IServerPlayer player, PartyMutationPacket request)
    {
        if (!TrySession(player, out var session)) return;
        bool allowed = session.Take(seconds());
        if (request == null || !PartyPacketRules.Identifier(request.RequestId, 64) || request.SessionId != session.Id) return;
        void Reject(string code) => channel!.SendPacket(new PartyResultPacket
            { SessionId = session.Id, RequestId = request.RequestId, Code = code }, player);
        if (!PartyPacketRules.Mutation(request)) { Reject("invalid-command"); return; }
        if (!allowed) { Reject("rate-limit"); return; }
        if (!session.Established) { Reject("refresh-required"); return; }
        if (request.Action == PartyAction.Invite)
        {
            double time = seconds();
            if (invitationAttempts.TryGetValue(player.PlayerUID, out double last) && time - last < 2)
                { Reject("invite-rate-limit"); return; }
            invitationAttempts[player.PlayerUID] = time;
        }
        var authority = service();
        var before = Read(player.PlayerUID);
        PartyCommandResult result;
        if (authority == null || before.Health == PartyHealth.Unavailable) result = new(false, "unavailable", before);
        else if (request.PartyId != (before.PartyId ?? "")) result = new(false, "stale-party", before);
        else if (request.Action == PartyAction.Invite && !IsOnline(request.TargetUid)) result = new(false, "target-offline", before);
        else result = authority.Execute(player.PlayerUID,
            new(request.RequestId, request.ExpectedRevision, request.Action, request.TargetUid, request.InvitationId), now());
        session.View = result.View;
        channel!.SendPacket(new PartyResultPacket { SessionId = session.Id, RequestId = request.RequestId,
            Success = result.Success, Code = result.Code, Snapshot = Snapshot(session, result.View, request.RequestId) }, player);
        if (result.Success)
        {
            var partyIds = new HashSet<string>(StringComparer.Ordinal);
            if (before.PartyId != null) partyIds.Add(before.PartyId);
            if (result.View.PartyId != null) partyIds.Add(result.View.PartyId);
            var members = new HashSet<string>(before.Members.Select(m => m.Uid), StringComparer.Ordinal);
            members.UnionWith(result.View.Members.Select(m => m.Uid));
            if (request.Action == PartyAction.Invite) members.Add(request.TargetUid);
            foreach (var other in sessions.Values)
                if (other != session && other.Established && (members.Contains(other.Player.PlayerUID)
                    || other.View?.IncomingInvitations.Any(i => partyIds.Contains(i.PartyId)) == true))
                    SendSnapshot(other, "");
        }
        InvalidateHealth(session);
    }

    private PartyView Read(string uid) => service()?.Read(uid, now())
        ?? new(PartyHealth.Unavailable, 0, null, null, Array.Empty<PartyMemberView>(), Array.Empty<PartyInvitationView>());

    private void InvalidatePresence(string uid, Session changed)
    {
        foreach (var session in sessions.Values)
            if (session != changed && session.Established && session.View?.Members.Any(m => m.Uid == uid) == true)
                SendSnapshot(session, "");
    }

    private void InvalidateHealth(Session observed)
    {
        if (observed.View?.Health != PartyHealth.Unavailable) return;
        foreach (var session in sessions.Values)
            if (session != observed && session.Established && session.View?.Health == PartyHealth.Healthy)
                SendSnapshot(session, "");
    }

    /// <summary>Expire only subscribed invitation views; ordinary refresh never reloads authority.</summary>
    public void Tick()
    {
        if (disposed) return;
        double clock = seconds();
        foreach (var entry in invitationAttempts.Where(p => clock - p.Value >= 2).ToArray()) invitationAttempts.Remove(entry.Key);
        var time = now();
        foreach (var session in sessions.Values)
            if (session.Established && session.View?.IncomingInvitations.Any(i => i.ExpiresAt <= time) == true)
                SendSnapshot(session, "");
    }

    private void SendSnapshot(Session session, string requestId, string code = "ok", bool read = true)
    {
        var view = read ? Read(session.Player.PlayerUID) : new PartyView(PartyHealth.Unavailable,
            session.View?.Revision ?? 0, null, null, Array.Empty<PartyMemberView>(), Array.Empty<PartyInvitationView>());
        if (read) session.View = view;
        var packet = Snapshot(session, view, requestId, includeTargets: read);
        packet.Code = view.Health == PartyHealth.Unavailable && code == "ok" ? "unavailable" : code;
        channel!.SendPacket(packet, session.Player);
    }

    private PartySnapshotPacket Snapshot(Session session, PartyView view, string requestId, bool includeTargets = true)
    {
        var packet = new PartySnapshotPacket { SessionId = session.Id, RequestId = requestId, Sequence = ++session.Sequence,
            Health = view.Health, Revision = view.Revision, SelfUid = session.Player.PlayerUID,
            PartyId = view.PartyId ?? "", LeaderUid = view.LeaderUid ?? "", Search = session.Search, Page = session.Page,
            Members = view.Members.Select(m => new PartyMemberPacket
                { Uid = m.Uid, DisplayName = DisplayName(m.Uid), IsOnline = IsOnline(m.Uid) }).ToArray(),
            IncomingInvitations = view.IncomingInvitations.Select(i => new PartyInvitationPacket
                { InvitationId = i.InvitationId, PartyId = i.PartyId, LeaderUid = i.LeaderUid,
                    LeaderName = DisplayName(i.LeaderUid), ExpiresAtUnixSeconds = i.ExpiresAt.ToUnixTimeSeconds() }).ToArray() };
        if (includeTargets && view.Health == PartyHealth.Healthy && session.IncludeTargets)
        {
            var targets = online().Where(p => PartyPacketRules.Identifier(p.PlayerUID) && p.PlayerUID != session.Player.PlayerUID
                    && Eligible(p) && IsOnline(p.PlayerUID) && !view.Members.Any(m => m.Uid == p.PlayerUID))
                .Select(p => new PartyTargetPacket { Uid = p.PlayerUID, DisplayName = CleanName(p.PlayerName) })
                .Where(p => session.Search.Length == 0 || p.DisplayName.Contains(session.Search, StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase).ThenBy(p => p.Uid, StringComparer.Ordinal)
                .Skip(session.Page * 16).Take(17).ToArray();
            packet.Targets = targets.Take(16).ToArray();
            packet.HasMoreTargets = targets.Length > 16;
        }
        return packet;
    }

    private string DisplayName(string uid) => CleanName(name(uid));
    private static string CleanName(string? value) => new((value ?? "").Where(c => !char.IsControl(c)).Take(128).ToArray());

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        // Native channels have no unregister operation. Release captures and leave inert handlers.
        channel?.SetMessageHandler<PartyQueryPacket>((_, _) => { });
        channel?.SetMessageHandler<PartyMutationPacket>((_, _) => { });
        channel = null;
        sessions.Clear();
        invitationAttempts.Clear();
    }

    private sealed class Session(IServerPlayer player, double start)
    {
        public readonly IServerPlayer Player = player;
        public readonly string Id = Guid.NewGuid().ToString("N");
        public PartyView? View;
        public long Sequence;
        public long LastQuerySequence;
        public bool Established;
        public string Search = "";
        public int Page;
        public bool IncludeTargets;
        private double tokens = 5, last = start;
        public bool Take(double time)
        {
            tokens = Math.Min(5, tokens + Math.Max(0, time - last) * 5);
            last = Math.Max(last, time);
            if (tokens < 1) return false;
            tokens -= 1;
            return true;
        }
    }
}
