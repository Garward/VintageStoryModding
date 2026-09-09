using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using VRPG.Parties;
using VRPG.Parties.Persistence;

namespace VRPG.Tests;

internal sealed class PartyTestFixture
{
    public DateTimeOffset Now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);
    public HashSet<string> Online { get; } = new(StringComparer.Ordinal);
    public SnapshotPartyRepository Repository { get; } = new();
    public PartyService Service { get; }
    private int request;

    public PartyTestFixture()
    {
        Service = new PartyService(Repository, Online.Contains);
        Service.Load();
    }

    public PartyCommandResult Act(string actor, PartyAction action, string target = "", string invitation = "") =>
        Service.Execute(actor, new("request-" + ++request, Service.Read(actor, Now).Revision,
            action, target, invitation), Now);

    public string Create(string actor)
    {
        var result = Act(actor, PartyAction.Create);
        Xunit.Assert.True(result.Success, result.Code);
        return result.View.PartyId!;
    }

    public string Invite(string leader, string target)
    {
        Online.Add(target);
        var result = Act(leader, PartyAction.Invite, target);
        Xunit.Assert.True(result.Success, result.Code);
        return Xunit.Assert.Single(Service.Read(target, Now).IncomingInvitations,
            invitation => invitation.LeaderUid == leader).InvitationId;
    }

    public void Join(string leader, string member)
    {
        string id = Invite(leader, member);
        var result = Act(member, PartyAction.Accept, invitation: id);
        Xunit.Assert.True(result.Success, result.Code);
    }
}

internal sealed class SnapshotPartyRepository : IPartyRepository
{
    public object SyncRoot { get; } = new();
    public string? Bytes;
    public int Writes;
    public int Reads;
    public long? CommittedGeneration { get; private set; }
    public bool FailBeforeWrite;
    public bool FailAfterWrite;
    public bool FailLoad;

    public PartyRepositoryLoadResult Load()
    {
        Reads++;
        if (FailLoad) { CommittedGeneration = null; return new(PartyRepositoryLoadStatus.Unavailable, null); }
        var archive = Bytes == null ? null : JsonConvert.DeserializeObject<PartyArchive>(Bytes);
        CommittedGeneration = archive?.Generation ?? 0;
        return archive == null ? new(PartyRepositoryLoadStatus.Absent, null) : new(PartyRepositoryLoadStatus.Loaded, archive);
    }

    public PartyRepositorySaveStatus Save(PartyArchive archive, long expectedGeneration)
    {
        if (FailBeforeWrite) { CommittedGeneration = null; throw new IOException("Injected failure before write"); }
        var previous = Load().Archive;
        if ((previous?.Generation ?? 0) != expectedGeneration) { CommittedGeneration = null; return PartyRepositorySaveStatus.Failed; }
        Bytes = JsonConvert.SerializeObject(archive);
        Writes++;
        if (FailAfterWrite) { CommittedGeneration = null; throw new IOException("Injected failure after write"); }
        CommittedGeneration = archive.Generation;
        return PartyRepositorySaveStatus.Committed;
    }
}
