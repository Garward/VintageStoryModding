using System;
using System.Linq;
using VRPG.Parties;

namespace VRPG.Network;

internal static class PartyPacketRules
{
    internal static bool Identifier(string? value, int max = 128, bool empty = false) => value != null
        && value.Length <= max && (empty || value.Length > 0) && !value.Any(c => char.IsWhiteSpace(c) || char.IsControl(c));
    internal static bool Text(string? value, int max) => value != null && value.Length <= max && !value.Any(char.IsControl);
    internal static bool Id(string? value, bool empty = false) => value != null && ((empty && value.Length == 0)
        || (value.Length == 32 && Guid.TryParseExact(value, "N", out var guid) && guid != Guid.Empty));
    internal static bool Query(PartyQueryPacket p) => Identifier(p.SessionId, 64, true) && Identifier(p.RequestId, 64)
        && Text(p.Search, 64) && p.Page is >= 0 and <= int.MaxValue / 16 && p.QuerySequence > 0;
    internal static bool Mutation(PartyMutationPacket p)
    {
        if (!Identifier(p.SessionId, 64) || !Identifier(p.RequestId, 64) || !Id(p.PartyId, true)
            || p.ExpectedRevision < 0 || !Enum.IsDefined(p.Action)) return false;
        return p.Action switch
        {
            PartyAction.Invite or PartyAction.Remove or PartyAction.TransferLeader => Identifier(p.TargetUid) && p.InvitationId == "",
            PartyAction.Accept or PartyAction.Decline => p.TargetUid == "" && Id(p.InvitationId),
            _ => p.TargetUid == "" && p.InvitationId == ""
        };
    }

    internal static bool Snapshot(PartySnapshotPacket p)
    {
        // Check collection envelopes before touching entries or allocating client view records.
        if (p.Members == null || p.Members.Length > 8 || p.IncomingInvitations == null || p.IncomingInvitations.Length > 8
            || p.Targets == null || p.Targets.Length > 16) return false;
        if (!Identifier(p.SessionId, 64) || !Identifier(p.RequestId, 64, true) || p.Sequence <= 0
            || !Enum.IsDefined(p.Health) || p.Revision < 0 || !Identifier(p.SelfUid) || !Id(p.PartyId, true)
            || !Identifier(p.LeaderUid, 128, true) || !Text(p.Search, 64) || p.Page is < 0 or > int.MaxValue / 16
            || !Identifier(p.Code, 64)) return false;
        if (p.Members.Any(m => m == null || !Identifier(m.Uid) || !Text(m.DisplayName, 128))
            || p.IncomingInvitations.Any(i => i == null || !Id(i.InvitationId) || !Id(i.PartyId) || !Identifier(i.LeaderUid)
                || !Text(i.LeaderName, 128) || i.ExpiresAtUnixSeconds is < -62135596800 or > 253402300799)
            || p.Targets.Any(t => t == null || !Identifier(t.Uid) || !Text(t.DisplayName, 128))) return false;
        if (p.Members.Select(m => m.Uid).Distinct(StringComparer.Ordinal).Count() != p.Members.Length
            || p.IncomingInvitations.Select(i => i.InvitationId).Distinct(StringComparer.Ordinal).Count() != p.IncomingInvitations.Length
            || p.Targets.Select(t => t.Uid).Distinct(StringComparer.Ordinal).Count() != p.Targets.Length) return false;
        if (p.Health == PartyHealth.Unavailable)
            return p.PartyId == "" && p.LeaderUid == "" && p.Members.Length == 0 && p.IncomingInvitations.Length == 0 && p.Targets.Length == 0;
        return p.PartyId.Length == 0 ? p.LeaderUid == "" && p.Members.Length == 0
            : p.Members.Any(m => m.Uid == p.SelfUid) && p.Members.Any(m => m.Uid == p.LeaderUid);
    }
}
