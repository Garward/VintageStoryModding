using System;
using System.Linq;
using System.Runtime.CompilerServices;
using VRPG.Parties.Persistence;

namespace VRPG.Parties;

public sealed partial class PartyService
{
    private static readonly ConditionalWeakTable<object, Authority> Authorities = new();
    private readonly IPartyRepository repository;
    private readonly Func<string, bool> isOnline;
    private readonly Authority authority;

    public PartyService(IPartyRepository repository, Func<string, bool> isOnline)
    {
        this.repository = repository ?? throw new ArgumentNullException(nameof(repository));
        this.isOnline = isOnline ?? throw new ArgumentNullException(nameof(isOnline));
        authority = Authorities.GetValue(repository.SyncRoot, _ => new Authority());
    }

    /// <summary>Explicit startup/reconciliation boundary. Pending invitations never survive this boundary.</summary>
    public PartyHealth Load()
    {
        lock (repository.SyncRoot)
        {
            if (authority.Committing) return PartyHealth.Unavailable;
            if (!Refresh()) return PartyHealth.Unavailable;
            if (authority.State.Invitations.Count > 0)
            {
                var next = PartyArchiveRules.Clone(authority.State);
                next.Invitations.Clear();
                if (!Publish(next)) return PartyHealth.Unavailable;
            }
            return PartyHealth.Healthy;
        }
    }

    public PartyView Read(string actorUid, DateTimeOffset now)
    {
        lock (repository.SyncRoot)
        {
            if (authority.Healthy && !authority.Committing && HasCommittedAuthority()) Refresh();
            return View(actorUid, now);
        }
    }

    /// <summary>The caller supplies the authenticated player's stable UID, never a name or packet actor field.</summary>
    public PartyCommandResult Execute(string actorUid, PartyCommand command, DateTimeOffset now)
    {
        lock (repository.SyncRoot)
        {
            PartyCommandResult Result(bool success, string code) => new(success, code, View(actorUid, now));
            if (authority.Committing) return Result(false, "commit-in-progress");
            if (!PartyArchiveRules.ValidUid(actorUid) || !PartyArchiveRules.ValidCommand(command))
                return Result(false, "invalid-command");
            if (!HasCommittedAuthority() || !Refresh()) return Result(false, "unavailable");
            var replay = authority.State.Replay.FirstOrDefault(r => r.ActorUid == actorUid && r.Command.RequestId == command.RequestId);
            if (replay != null)
            {
                bool same = PartyArchiveRules.SameCommand(replay.Command, command);
                return Result(same, same ? "ok" : "request-conflict");
            }
            if (command.ExpectedRevision != authority.State.Generation) return Result(false, "stale-revision");
            var next = PartyArchiveRules.Clone(authority.State);
            string code = Apply(next, actorUid, command, now);
            if (code != "ok") return Result(false, code);
            next.Invitations.RemoveAll(i => i.ExpiresAt <= now);
            next.Replay.Add(new PartyReplayRecord { ActorUid = actorUid, Command = command });
            while (next.Replay.Count(r => r.ActorUid == actorUid) > 16)
                next.Replay.RemoveAt(next.Replay.FindIndex(r => r.ActorUid == actorUid));
            while (next.Replay.Count > 1024) next.Replay.RemoveAt(0);
            return Publish(next) ? Result(true, "ok") : Result(false, "unavailable");
        }
    }

    /// <summary>
    /// Trusted synchronous item-transaction boundary. Lock order is party authority, then item transaction.
    /// Access must come from server-owned item metadata, never packet-supplied owner/audience/party fields.
    /// Callback success is returned directly; exceptions propagate after releasing the gate. Callbacks must
    /// not wait for another thread that needs this authority, and may not mutate membership or nest commits.
    /// </summary>
    public bool TryWithLootAccess(string actorUid, PartyLootAccess access, Func<bool> commit)
    {
        ArgumentNullException.ThrowIfNull(commit);
        lock (repository.SyncRoot)
        {
            if (authority.Committing || access == null || !PartyArchiveRules.ValidUid(actorUid)
                || !PartyArchiveRules.ValidUid(access.DropperUid) || !Enum.IsDefined(access.Audience)
                || access.PartyId == null || (access.PartyId.Length != 0 && !PartyArchiveRules.ValidId(access.PartyId))) return false;
            bool independent = access.Audience == PartyLootAudience.Everyone || access.DropperUid == actorUid;
            if (!independent)
            {
                if (!HasCommittedAuthority()) return false;
                if (repository.CommittedGeneration != authority.State.Generation && !Refresh()) return false;
                if (!authority.State.Parties.Any(p => p.PartyId == access.PartyId && p.Members.Contains(actorUid))) return false;
            }
            authority.Committing = true;
            try { return commit(); }
            finally { authority.Committing = false; }
        }
    }

    private bool HasCommittedAuthority()
    {
        if (!authority.Healthy) return false;
        if (repository.CommittedGeneration.HasValue) return true;
        return authority.Healthy = false;
    }

    private bool Refresh()
    {
        try
        {
            var loaded = repository.Load();
            var state = loaded.Status == PartyRepositoryLoadStatus.Absent ? new PartyArchive() : loaded.Archive;
            if (loaded.Status is not (PartyRepositoryLoadStatus.Absent or PartyRepositoryLoadStatus.Loaded) || state == null
                || !PartyArchiveRules.Validate(state) || state.Generation < authority.State.Generation)
                return authority.Healthy = false;
            authority.State = PartyArchiveRules.Clone(state);
            return authority.Healthy = true;
        }
        catch { return authority.Healthy = false; }
    }

    private bool Publish(PartyArchive next)
    {
        try
        {
            next.Generation = checked(authority.State.Generation + 1);
            if (!PartyArchiveRules.Validate(next)
                || repository.Save(next, authority.State.Generation) != PartyRepositorySaveStatus.Committed)
                return authority.Healthy = false;
            authority.State = next;
            return true;
        }
        catch { return authority.Healthy = false; }
    }

    private PartyView View(string actorUid, DateTimeOffset now)
    {
        if (!HasCommittedAuthority()
            || (repository.CommittedGeneration != authority.State.Generation && !Refresh())
            || !PartyArchiveRules.ValidUid(actorUid))
            return new(PartyHealth.Unavailable, authority.State.Generation, null, null,
                Array.Empty<PartyMemberView>(), Array.Empty<PartyInvitationView>());
        var party = authority.State.Parties.FirstOrDefault(p => p.Members.Contains(actorUid));
        return new(PartyHealth.Healthy, authority.State.Generation, party?.PartyId, party?.LeaderUid,
            Array.AsReadOnly(party?.Members.Select(uid => new PartyMemberView(uid, isOnline(uid))).ToArray()
                ?? Array.Empty<PartyMemberView>()),
            Array.AsReadOnly(authority.State.Invitations.Where(i => i.RecipientUid == actorUid && i.ExpiresAt > now)
                .Select(i => new PartyInvitationView(i.InvitationId, i.PartyId, i.LeaderUid, i.ExpiresAt)).ToArray()));
    }

    private sealed class Authority
    {
        public PartyArchive State = new();
        public bool Healthy;
        public bool Committing;
    }
}
