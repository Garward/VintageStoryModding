using System;
using System.Linq;
using VRPG.Parties.Persistence;

namespace VRPG.Parties;

public sealed partial class PartyService
{
    private string Apply(PartyArchive state, string actor, PartyCommand command, DateTimeOffset now)
    {
        var party = state.Parties.FirstOrDefault(p => p.Members.Contains(actor));
        if (command.Action == PartyAction.Create)
        {
            if (party != null) return "already-member";
            state.Parties.Add(new PartyRecord { PartyId = Guid.NewGuid().ToString("N"), LeaderUid = actor, Members = new() { actor } });
            state.Invitations.RemoveAll(i => i.RecipientUid == actor);
            return "ok";
        }
        if (command.Action is PartyAction.Accept or PartyAction.Decline)
        {
            var invitation = state.Invitations.FirstOrDefault(i => i.InvitationId == command.InvitationId && i.RecipientUid == actor);
            if (invitation == null) return "invitation-missing";
            if (invitation.ExpiresAt <= now) return "invitation-expired";
            if (command.Action == PartyAction.Decline) { state.Invitations.Remove(invitation); return "ok"; }
            if (party != null) return "already-member";
            var destination = state.Parties.FirstOrDefault(p => p.PartyId == invitation.PartyId && p.LeaderUid == invitation.LeaderUid);
            if (destination == null) return "invitation-missing";
            if (destination.Members.Count >= 8) return "party-full";
            destination.Members.Add(actor);
            state.Invitations.RemoveAll(i => i.RecipientUid == actor);
            return "ok";
        }
        if (party == null) return "no-party";
        if (command.Action == PartyAction.Leave)
        {
            if (party.LeaderUid == actor && party.Members.Count > 1) return "transfer-required";
            party.Members.Remove(actor);
            if (party.Members.Count == 0) Retire(state, party);
            return "ok";
        }
        if (party.LeaderUid != actor) return "leader-required";
        switch (command.Action)
        {
            case PartyAction.Invite:
                if (state.Parties.Any(p => p.Members.Contains(command.TargetUid))) return "already-member";
                if (!isOnline(command.TargetUid)) return "target-offline";
                var pending = state.Invitations.Where(i => i.ExpiresAt > now).ToArray();
                if (pending.Any(i => i.PartyId == party.PartyId && i.RecipientUid == command.TargetUid)) return "already-invited";
                if (pending.Count(i => i.PartyId == party.PartyId) >= 8
                    || pending.Count(i => i.RecipientUid == command.TargetUid) >= 8) return "invitation-limit";
                if (party.Members.Count >= 8) return "party-full";
                if (now > DateTimeOffset.MaxValue - TimeSpan.FromMinutes(5)) return "invalid-command";
                state.Invitations.Add(new PartyInvitationRecord
                {
                    InvitationId = Guid.NewGuid().ToString("N"), PartyId = party.PartyId, LeaderUid = actor,
                    RecipientUid = command.TargetUid, ExpiresAt = now + TimeSpan.FromMinutes(5)
                });
                return "ok";
            case PartyAction.Remove:
            case PartyAction.TransferLeader:
                if (command.TargetUid == actor || !party.Members.Contains(command.TargetUid)) return "invalid-target";
                if (command.Action == PartyAction.Remove) party.Members.Remove(command.TargetUid);
                else
                {
                    party.LeaderUid = command.TargetUid;
                    state.Invitations.RemoveAll(i => i.PartyId == party.PartyId);
                }
                return "ok";
            case PartyAction.Disband:
                Retire(state, party);
                return "ok";
            default: return "invalid-command";
        }
    }

    private static void Retire(PartyArchive state, PartyRecord party)
    {
        state.Parties.Remove(party);
        state.Invitations.RemoveAll(i => i.PartyId == party.PartyId);
    }
}
