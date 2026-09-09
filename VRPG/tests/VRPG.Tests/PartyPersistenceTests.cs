using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VRPG.Parties;
using VRPG.Parties.Persistence;
using Xunit;

namespace VRPG.Tests;

public sealed class PartyPersistenceTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "vrpg-parties-tests-" + Guid.NewGuid().ToString("N"));
    private readonly DateTimeOffset now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void MembershipAndLeaderSurviveRepositoryReopenInIsolatedWorlds()
    {
        string id;
        using (var repository = new WorldScopedPartyRepository(root, "world-one"))
        {
            Assert.Equal(PartyRepositoryLoadStatus.Absent, repository.Load().Status);
            var service = new PartyService(repository, _ => true); service.Load();
            var created = service.Execute("owner", new("create", 0, PartyAction.Create), now);
            Assert.True(created.Success); id = created.View.PartyId!;
            Assert.True(service.Execute("owner", new("invite", created.View.Revision, PartyAction.Invite, "member"), now).Success);
            var invited = service.Read("member", now);
            Assert.True(service.Execute("member", new("accept", invited.Revision, PartyAction.Accept,
                InvitationId: Assert.Single(invited.IncomingInvitations).InvitationId), now).Success);
        }
        using var reopened = new WorldScopedPartyRepository(root, "world-one");
        var restored = new PartyService(reopened, _ => false); Assert.Equal(PartyHealth.Healthy, restored.Load());
        Assert.Equal(id, restored.Read("member", now).PartyId);
        Assert.Equal("owner", restored.Read("member", now).LeaderUid);
        using var other = new WorldScopedPartyRepository(root, "world-two");
        Assert.Equal(PartyRepositoryLoadStatus.Absent, other.Load().Status);
    }

    [Theory]
    [InlineData("corrupt")]
    [InlineData("truncate")]
    [InlineData("missing")]
    [InlineData("unsupported")]
    [InlineData("world")]
    [InlineData("generation")]
    [InlineData("structure")]
    [InlineData("missing-command-action")]
    public void UnreadableHistoryIsPreservedAndNeverTreatedAsAnEmptyWorld(string damage)
    {
        using var repository = CreateSaved();
        string path = repository.ArchivePath;
        var envelope = JObject.Parse(File.ReadAllText(path));
        switch (damage)
        {
            case "corrupt": envelope["Checksum"] = "bad"; break;
            case "truncate": File.WriteAllText(path, "{\"Format\":"); break;
            case "missing": File.Delete(path); break;
            case "unsupported": envelope["Format"] = 99; break;
            case "world": envelope["WorldIdentity"] = "other-world"; break;
            case "generation": envelope["Generation"] = -1; break;
            case "structure":
                RewritePayload(envelope, payload => payload["Parties"]![0]!["LeaderUid"] = "not-a-member"); break;
            case "missing-command-action":
                RewritePayload(envelope, payload => ((JObject)payload["Replay"]![0]!["Command"]!).Remove("Action")); break;
        }
        if (damage != "truncate" && damage != "missing") File.WriteAllText(path, envelope.ToString(Formatting.None));
        byte[]? original = File.Exists(path) ? File.ReadAllBytes(path) : null;
        Assert.Equal(PartyRepositoryLoadStatus.Unavailable, repository.Load().Status);
        Assert.NotEqual(PartyRepositorySaveStatus.Committed, repository.Save(new PartyArchive { Generation = 2 }, 1));
        if (original == null) Assert.False(File.Exists(path)); else Assert.Equal(original, File.ReadAllBytes(path));
    }

    [Fact]
    public void UnknownFieldsSurviveSuccessfulMembershipMutation()
    {
        using var repository = CreateSaved();
        var envelope = JObject.Parse(File.ReadAllText(repository.ArchivePath));
        envelope["FutureEnvelope"] = new JObject { ["flag"] = true };
        RewritePayload(envelope, payload =>
        {
            payload["FutureArchive"] = 123; payload["Parties"]![0]!["FutureParty"] = "kept";
            payload["Replay"]![0]!["Command"]!["FutureCommand"] = "also-kept";
        });
        File.WriteAllText(repository.ArchivePath, envelope.ToString(Formatting.None));
        var service = new PartyService(repository, _ => true); service.Load();
        var view = service.Read("owner", now);
        Assert.True(service.Execute("owner", new("invite", view.Revision, PartyAction.Invite, "member"), now).Success);
        var saved = JObject.Parse(File.ReadAllText(repository.ArchivePath));
        var payload = JObject.Parse(Encoding.UTF8.GetString(Convert.FromBase64String((string)saved["Payload"]!)));
        Assert.True((bool)saved["FutureEnvelope"]!["flag"]!);
        Assert.Equal(123, (int)payload["FutureArchive"]!);
        Assert.Equal("kept", (string)payload["Parties"]![0]!["FutureParty"]!);
        Assert.Equal("also-kept", (string?)payload["Replay"]![0]!["Command"]!["FutureCommand"]);
    }

    [Fact]
    public void StaleRepositoryWriteCannotUndoRemovalAndSharesGate()
    {
        using var first = CreateSaved(); using var second = new WorldScopedPartyRepository(root, "world");
        Assert.Same(first.SyncRoot, second.SyncRoot);
        var stale = second.Load().Archive!;
        var service = new PartyService(first, _ => true); service.Load();
        Assert.True(service.Execute("owner", new("disband", 1, PartyAction.Disband), now).Success);
        byte[] latest = File.ReadAllBytes(first.ArchivePath);
        stale.Generation = 2;
        Assert.Equal(PartyRepositorySaveStatus.Failed, second.Save(stale, 1));
        Assert.Equal(latest, File.ReadAllBytes(first.ArchivePath));
    }

    [Fact]
    public void RealRestartClearsExpiredInvitationsWithoutRemovingMembership()
    {
        using (var repository = CreateSaved())
        {
            var service = new PartyService(repository, _ => true); service.Load();
            Assert.True(service.Execute("owner", new("invite", 1, PartyAction.Invite, "pending"), now).Success);
        }
        using var reopened = new WorldScopedPartyRepository(root, "world");
        var restarted = new PartyService(reopened, _ => false);
        Assert.Equal(PartyHealth.Healthy, restarted.Load());
        Assert.Empty(restarted.Read("pending", now + TimeSpan.FromHours(1)).IncomingInvitations);
        Assert.Equal("owner", restarted.Read("owner", now).LeaderUid);
        Assert.Empty(reopened.Load().Archive!.Invitations);
    }

    [Fact]
    public void FilesystemCommitFailurePreservesCurrentBytesAndRequiresReload()
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("This test verifies Linux durable storage.");
        using var repository = CreateSaved();
        var service = new PartyService(repository, _ => true); service.Load();
        byte[] before = File.ReadAllBytes(repository.ArchivePath);
        string directory = Path.GetDirectoryName(repository.ArchivePath)!;
        var mode = File.GetUnixFileMode(directory);
        try
        {
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserExecute);
            Assert.False(service.Execute("owner", new("disband", 1, PartyAction.Disband), now).Success);
            Assert.Equal(PartyHealth.Unavailable, service.Read("owner", now).Health);
            Assert.Equal(before, File.ReadAllBytes(repository.ArchivePath));
        }
        finally { File.SetUnixFileMode(directory, mode); }
        Assert.Equal(PartyHealth.Healthy, service.Load());
        Assert.Equal("owner", service.Read("owner", now).LeaderUid);
    }

    [Fact]
    public void AnotherProcessHoldingWorldLeaseRefusesLoadAndPreservesBytes()
    {
        string archive;
        using (var repository = CreateSaved()) archive = repository.ArchivePath;
        byte[] before = File.ReadAllBytes(archive);
        string lockPath = Directory.GetFiles(Path.Combine(root, "party-locks")).Single();
        var start = new ProcessStartInfo("flock")
        { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true };
        start.ArgumentList.Add("-x"); start.ArgumentList.Add(lockPath);
        start.ArgumentList.Add("sh"); start.ArgumentList.Add("-c");
        start.ArgumentList.Add("echo locked; read release");
        using var blocker = Process.Start(start)!;
        try
        {
            Assert.Equal("locked", blocker.StandardOutput.ReadLine());
            using var blocked = new WorldScopedPartyRepository(root, "world");
            Assert.Equal(PartyRepositoryLoadStatus.Unavailable, blocked.Load().Status);
            Assert.Equal(PartyRepositorySaveStatus.Failed, blocked.Save(new PartyArchive { Generation = 2 }, 1));
            Assert.Equal(before, File.ReadAllBytes(archive));
        }
        finally { blocker.StandardInput.WriteLine("release"); Assert.True(blocker.WaitForExit(5000)); }
        using var restored = new WorldScopedPartyRepository(root, "world");
        Assert.Equal(PartyRepositoryLoadStatus.Loaded, restored.Load().Status);
    }

    [Fact]
    public void OversizedFileRefusesWithoutOverwritingIt()
    {
        using var repository = CreateSaved();
        using (var oversized = new FileStream(repository.ArchivePath, FileMode.Open, FileAccess.Write))
            oversized.SetLength(17 * 1024 * 1024);
        Assert.Equal(PartyRepositoryLoadStatus.Unavailable, repository.Load().Status);
        Assert.Equal(PartyRepositorySaveStatus.Failed, repository.Save(new PartyArchive { Generation = 2 }, 1));
        Assert.Equal(17 * 1024 * 1024, new FileInfo(repository.ArchivePath).Length);
    }

    [Fact]
    public void DisposedRepositoryCannotAuthorizeCachedSharedPickup()
    {
        var repository = CreateSaved();
        try
        {
            var service = new PartyService(repository, _ => true); service.Load();
            Assert.True(service.Execute("owner", new("invite", 1, PartyAction.Invite, "member"), now).Success);
            var view = service.Read("member", now);
            var accepted = service.Execute("member", new("accept", view.Revision, PartyAction.Accept,
                InvitationId: Assert.Single(view.IncomingInvitations).InvitationId), now);
            Assert.True(accepted.Success);
            repository.Dispose();
            bool called = false;
            Assert.False(service.TryWithLootAccess("member", new("owner", PartyLootAudience.PartyOnly, accepted.View.PartyId!),
                () => called = true));
            Assert.False(called);
        }
        finally { repository.Dispose(); }
    }

    [Fact]
    public void WorldLeaseExcludesAnotherProcessUntilLastRepositoryIsDisposed()
    {
        string lockPath = Path.Combine(root, "party-locks",
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("world")))) + ".lock";
        var first = CreateSaved(); var second = new WorldScopedPartyRepository(root, "world");
        try
        {
            Assert.Equal(PartyRepositoryLoadStatus.Loaded, second.Load().Status);
            Assert.Equal(1, ProbeLock(lockPath));
            first.Dispose();
            Assert.Equal(1, ProbeLock(lockPath));
        }
        finally { first.Dispose(); second.Dispose(); }
        Assert.Equal(0, ProbeLock(lockPath));
        Assert.Equal(PartyRepositoryLoadStatus.Unavailable, second.Load().Status);
        using var reopened = new WorldScopedPartyRepository(root, "world");
        Assert.Equal(PartyRepositoryLoadStatus.Loaded, reopened.Load().Status);
    }

    [Fact]
    public void RemovedHistoryDirectoryCannotBecomeFreshThroughReopening()
    {
        string archive;
        using (var repository = CreateSaved()) archive = repository.ArchivePath;
        Directory.Delete(Path.GetDirectoryName(archive)!, recursive: true);
        using var reopened = new WorldScopedPartyRepository(root, "world");
        Assert.Equal(PartyRepositoryLoadStatus.Unavailable, reopened.Load().Status);
        Assert.False(File.Exists(archive));
    }

    private static int ProbeLock(string path)
    {
        var start = new ProcessStartInfo("flock") { UseShellExecute = false };
        start.ArgumentList.Add("-n"); start.ArgumentList.Add(path); start.ArgumentList.Add("true");
        using var process = Process.Start(start)!;
        Assert.True(process.WaitForExit(5000));
        return process.ExitCode;
    }

    [Fact]
    public void InterruptedTemporaryWriteCannotReplaceCurrentRoster()
    {
        using var repository = CreateSaved();
        byte[] latest = File.ReadAllBytes(repository.ArchivePath);
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(repository.ArchivePath)!, ".parties-interrupted"), "partial");
        Assert.Equal(PartyRepositoryLoadStatus.Loaded, repository.Load().Status);
        Assert.Equal(latest, File.ReadAllBytes(repository.ArchivePath));
        File.Delete(repository.ArchivePath);
        Assert.Equal(PartyRepositoryLoadStatus.Unavailable, repository.Load().Status);
    }

    private WorldScopedPartyRepository CreateSaved()
    {
        var repository = new WorldScopedPartyRepository(root, "world");
        var service = new PartyService(repository, _ => true); service.Load();
        Assert.True(service.Execute("owner", new("create", 0, PartyAction.Create), now).Success);
        return repository;
    }

    private static void RewritePayload(JObject envelope, Action<JObject> mutate)
    {
        var payload = JObject.Parse(Encoding.UTF8.GetString(Convert.FromBase64String((string)envelope["Payload"]!)));
        mutate(payload);
        byte[] bytes = Encoding.UTF8.GetBytes(payload.ToString(Formatting.None));
        envelope["Payload"] = Convert.ToBase64String(bytes);
        envelope["Checksum"] = Convert.ToHexString(SHA256.HashData(bytes));
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
}
