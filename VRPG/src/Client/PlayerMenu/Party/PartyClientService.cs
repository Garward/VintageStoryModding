using System;
using System.Collections.Generic;
using System.Linq;
using VRPG.Network;
using VRPG.Parties;
using Vintagestory.API.Client;

namespace VRPG.Client.PlayerMenu.Party;

public sealed class PartyClientService : IDisposable
{
    private readonly IClientNetworkChannel channel;
    private readonly IClientEventAPI? events;
    private readonly HashSet<string> usedRequests = new(StringComparer.Ordinal);
    private string sessionId = "", queryId = "", handshakeId = "", search = "";
    private int page;
    private bool includeTargets, disposed;
    private long sequence;
    private long querySequence;
    public PartyClientView? View { get; private set; }
    public PartyCommand? Pending { get; private set; }
    public string LastResultCode { get; private set; } = "";
    public event Action? Changed;

    /// <summary>Owned by the connection, independently of any menu opening or closing.</summary>
    public PartyClientService(IClientNetworkChannel channel, IClientEventAPI? events = null)
    {
        this.channel = channel;
        this.events = events;
        channel.SetMessageHandler<PartySessionPacket>(ReceiveSession);
        channel.SetMessageHandler<PartySnapshotPacket>(ReceiveSnapshot);
        channel.SetMessageHandler<PartyResultPacket>(ReceiveResult);
        if (events != null) events.LeaveWorld += ResetConnection;
    }

    public void RequestView() => RequestView(search, page, includeTargets);

    public void RequestView(string search, int page = 0, bool includeTargets = true)
    {
        if (disposed || !PartyPacketRules.Text(search, 64) || page is < 0 or > int.MaxValue / 16) return;
        this.search = search;
        this.page = page;
        this.includeTargets = includeTargets;
        queryId = Guid.NewGuid().ToString("N");
        handshakeId = sessionId.Length == 0 ? queryId : "";
        channel.SendPacket(new PartyQueryPacket { SessionId = sessionId, RequestId = queryId,
            Search = search, Page = page, IncludeTargets = includeTargets, QuerySequence = ++querySequence });
    }

    /// <summary>Command IDs must be unique for the connection. No optimistic membership changes.</summary>
    public bool Send(PartyCommand command)
    {
        if (disposed || command == null || Pending != null || sessionId.Length == 0 || View?.Health != PartyHealth.Healthy
            || command.ExpectedRevision != View.Revision || usedRequests.Contains(command.RequestId)) return false;
        var packet = new PartyMutationPacket { SessionId = sessionId, RequestId = command.RequestId,
            PartyId = View.PartyId ?? "", ExpectedRevision = command.ExpectedRevision,
            Action = command.Action, TargetUid = command.TargetUid, InvitationId = command.InvitationId };
        if (!PartyPacketRules.Mutation(packet)) return false;
        usedRequests.Add(command.RequestId);
        Pending = command;
        LastResultCode = "";
        channel.SendPacket(packet);
        Changed?.Invoke();
        return true;
    }

    private void ReceiveSession(PartySessionPacket packet)
    {
        if (disposed || packet == null || handshakeId.Length == 0 || packet.RequestId != handshakeId
            || !PartyPacketRules.Identifier(packet.SessionId, 64)) return;
        sessionId = packet.SessionId;
        handshakeId = "";
    }

    private void ReceiveSnapshot(PartySnapshotPacket packet)
    {
        if (disposed || packet == null || sessionId.Length == 0 || packet.SessionId != sessionId
            || !PartyPacketRules.Snapshot(packet) || (packet.RequestId.Length != 0 && packet.RequestId != queryId)) return;
        if (Apply(packet))
        {
            if (packet.RequestId == queryId) queryId = "";
            LastResultCode = packet.Code;
            Changed?.Invoke();
        }
    }

    private void ReceiveResult(PartyResultPacket packet)
    {
        if (disposed || packet == null || Pending == null || packet.SessionId != sessionId || packet.RequestId != Pending.RequestId
            || !PartyPacketRules.Identifier(packet.Code, 64)) return;
        if (packet.Snapshot != null)
        {
            if (packet.Snapshot.SessionId != sessionId || packet.Snapshot.RequestId != packet.RequestId
                || !PartyPacketRules.Snapshot(packet.Snapshot)) return;
            // A newer invalidation can arrive before this result. Finish only this request; keep newer state.
            Apply(packet.Snapshot);
        }
        Pending = null;
        LastResultCode = packet.Code;
        Changed?.Invoke();
    }

    private bool Apply(PartySnapshotPacket packet)
    {
        if (packet.Sequence <= sequence || (View != null && packet.Revision < View.Revision)) return false;
        sequence = packet.Sequence;
        // Rate-limit/validation responses convey feedback without claiming that authority was lost.
        if (packet.Code is "rate-limit" or "invalid-query") return true;
        View = new(packet.Health, packet.Revision, packet.SelfUid,
            packet.PartyId.Length == 0 ? null : packet.PartyId, packet.LeaderUid.Length == 0 ? null : packet.LeaderUid,
            Array.AsReadOnly(packet.Members.Select(m => new PartyClientMember(m.Uid, m.DisplayName, m.IsOnline)).ToArray()),
            Array.AsReadOnly(packet.IncomingInvitations.Select(i => new PartyClientInvitation(i.InvitationId, i.PartyId,
                i.LeaderUid, i.LeaderName, DateTimeOffset.FromUnixTimeSeconds(i.ExpiresAtUnixSeconds))).ToArray()),
            Array.AsReadOnly(packet.Targets.Select(t => new PartyClientTarget(t.Uid, t.DisplayName)).ToArray()),
            packet.Search, packet.Page, packet.HasMoreTargets);
        return true;
    }

    public void ResetConnection()
    {
        sessionId = queryId = handshakeId = search = "";
        sequence = page = 0;
        includeTargets = false;
        usedRequests.Clear();
        View = null;
        Pending = null;
        LastResultCode = "";
        Changed?.Invoke();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (events != null) events.LeaveWorld -= ResetConnection;
        channel.SetMessageHandler<PartySessionPacket>(_ => { });
        channel.SetMessageHandler<PartySnapshotPacket>(_ => { });
        channel.SetMessageHandler<PartyResultPacket>(_ => { });
        ResetConnection();
        Changed = null;
    }
}

public sealed record PartyClientMember(string Uid, string DisplayName, bool IsOnline);
public sealed record PartyClientInvitation(string InvitationId, string PartyId, string LeaderUid, string LeaderName, DateTimeOffset ExpiresAt);
public sealed record PartyClientTarget(string Uid, string DisplayName);
public sealed record PartyClientView(PartyHealth Health, long Revision, string SelfUid, string? PartyId, string? LeaderUid,
    IReadOnlyList<PartyClientMember> Members, IReadOnlyList<PartyClientInvitation> IncomingInvitations,
    IReadOnlyList<PartyClientTarget> Targets, string Search, int Page, bool HasMoreTargets);
