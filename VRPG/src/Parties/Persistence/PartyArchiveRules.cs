using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace VRPG.Parties.Persistence;

internal static class PartyArchiveRules
{
    internal const int MaximumBytes = 16 * 1024 * 1024;
    internal static readonly JsonSerializerSettings Settings = new() { TypeNameHandling = TypeNameHandling.None, MaxDepth = 32 };

    internal static bool ValidUid(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 128
        && !value.Any(c => char.IsControl(c) || char.IsWhiteSpace(c));
    internal static bool ValidId(string? value) => value?.Length == 32 && Guid.TryParseExact(value, "N", out var id) && id != Guid.Empty;

    internal static bool SameCommand(PartyCommand left, PartyCommand right) =>
        left.RequestId == right.RequestId && left.ExpectedRevision == right.ExpectedRevision && left.Action == right.Action
        && left.TargetUid == right.TargetUid && left.InvitationId == right.InvitationId;

    internal static bool ValidCommand(PartyCommand? command)
    {
        if (command == null || !ValidUid(command.RequestId) || command.ExpectedRevision < 0
            || !Enum.IsDefined(command.Action) || command.TargetUid == null || command.InvitationId == null) return false;
        return command.Action switch
        {
            PartyAction.Invite or PartyAction.Remove or PartyAction.TransferLeader => ValidUid(command.TargetUid) && command.InvitationId == "",
            PartyAction.Accept or PartyAction.Decline => command.TargetUid == "" && ValidId(command.InvitationId),
            _ => command.TargetUid == "" && command.InvitationId == ""
        };
    }

    internal static PartyArchive Clone(PartyArchive archive) =>
        JsonConvert.DeserializeObject<PartyArchive>(JsonConvert.SerializeObject(archive, Settings), Settings)!;

    internal static bool Validate(PartyArchive state)
    {
        if (state.Generation < 0 || state.Parties == null || state.Invitations == null || state.Replay == null
            || state.Parties.Count > 100000 || state.Invitations.Count > 800000 || state.Replay.Count > 1024) return false;
        var parties = new Dictionary<string, PartyRecord>(StringComparer.Ordinal);
        var members = new HashSet<string>(StringComparer.Ordinal);
        foreach (var party in state.Parties)
        {
            if (party == null || !ValidId(party.PartyId) || !ValidUid(party.LeaderUid) || party.Members == null
                || party.Members.Count is < 1 or > 8 || !party.Members.Contains(party.LeaderUid)
                || !parties.TryAdd(party.PartyId, party)) return false;
            foreach (var uid in party.Members) if (!ValidUid(uid) || !members.Add(uid)) return false;
        }
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var pairs = new HashSet<(string, string)>();
        var partyCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var recipientCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var invitation in state.Invitations)
        {
            if (invitation == null || !ValidId(invitation.InvitationId) || !ids.Add(invitation.InvitationId)
                || !ValidId(invitation.PartyId) || !parties.TryGetValue(invitation.PartyId, out var party)
                || invitation.LeaderUid != party.LeaderUid || !ValidUid(invitation.RecipientUid)
                || members.Contains(invitation.RecipientUid) || !pairs.Add((invitation.PartyId, invitation.RecipientUid))
                || invitation.ExpiresAt == default) return false;
            if ((partyCounts[invitation.PartyId] = partyCounts.GetValueOrDefault(invitation.PartyId) + 1) > 8
                || (recipientCounts[invitation.RecipientUid] = recipientCounts.GetValueOrDefault(invitation.RecipientUid) + 1) > 8) return false;
        }
        var requests = new HashSet<(string, string)>();
        var actorCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var replay in state.Replay)
        {
            if (replay == null || !ValidUid(replay.ActorUid) || !ValidCommand(replay.Command)
                || replay.Command.ExpectedRevision >= state.Generation
                || !requests.Add((replay.ActorUid, replay.Command.RequestId))
                || (actorCounts[replay.ActorUid] = actorCounts.GetValueOrDefault(replay.ActorUid) + 1) > 16) return false;
        }
        return state.Generation > 0 || (state.Parties.Count == 0 && state.Invitations.Count == 0 && state.Replay.Count == 0);
    }
}
