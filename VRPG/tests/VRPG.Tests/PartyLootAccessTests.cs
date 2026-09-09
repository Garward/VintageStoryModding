using System;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using VRPG.Parties;
using Xunit;

namespace VRPG.Tests;

public sealed class PartyLootAccessTests
{
    [Fact]
    public void CurrentMembersGainAndLoseAccessWhileOwnerCanAlwaysReclaim()
    {
        var f = new PartyTestFixture(); string id = f.Create("owner");
        var access = new PartyLootAccess("owner", PartyLootAudience.PartyOnly, id);
        Assert.False(Allowed(f, "member", access));
        f.Join("owner", "member"); Assert.True(Allowed(f, "member", access));
        Assert.True(f.Act("owner", PartyAction.Remove, "member").Success);
        Assert.False(Allowed(f, "member", access));
        f.Join("owner", "member"); Assert.True(f.Act("owner", PartyAction.Disband).Success);
        Assert.False(Allowed(f, "member", access)); Assert.True(Allowed(f, "owner", access));
        f.Create("owner"); f.Join("owner", "member"); Assert.False(Allowed(f, "member", access));
    }

    [Fact]
    public void SoloDropsStayOwnerOnlyAndPublicDropsAllowOthers()
    {
        var f = new PartyTestFixture();
        var solo = new PartyLootAccess("owner", PartyLootAudience.PartyOnly, "");
        f.Create("owner"); f.Join("owner", "member");
        Assert.False(Allowed(f, "member", solo)); Assert.True(Allowed(f, "owner", solo));
        Assert.True(Allowed(f, "stranger", solo with { Audience = PartyLootAudience.Everyone }));
        Assert.False(Allowed(f, "owner", solo with { Audience = (PartyLootAudience)99 }));
        Assert.False(Allowed(f, "", solo with { Audience = PartyLootAudience.Everyone }));
        Assert.False(Allowed(f, "stranger", solo with { DropperUid = "", Audience = PartyLootAudience.Everyone }));
    }

    [Fact]
    public void UnavailableAuthorityRefusesSharedAccessButKeepsIndependentRights()
    {
        var f = new PartyTestFixture(); string id = f.Create("owner"); f.Join("owner", "member");
        var access = new PartyLootAccess("owner", PartyLootAudience.PartyOnly, id);
        f.Repository.FailBeforeWrite = true;
        Assert.False(f.Act("owner", PartyAction.Remove, "member").Success);
        Assert.False(Allowed(f, "member", access)); Assert.True(Allowed(f, "owner", access));
        Assert.True(Allowed(f, "member", access with { Audience = PartyLootAudience.Everyone }));
    }

    [Fact]
    public void CallbackFailureExceptionAndReentrantMutationDoNotLeakAGrant()
    {
        var f = new PartyTestFixture(); string id = f.Create("owner"); f.Join("owner", "member");
        var access = new PartyLootAccess("owner", PartyLootAudience.PartyOnly, id);
        Assert.False(f.Service.TryWithLootAccess("member", access, () => false));
        Assert.Throws<InvalidOperationException>(() => f.Service.TryWithLootAccess("member", access,
            () => throw new InvalidOperationException("item transaction")));
        Assert.True(f.Service.TryWithLootAccess("member", access, () =>
        {
            Assert.Equal("commit-in-progress", f.Act("owner", PartyAction.Remove, "member").Code);
            Assert.Equal(PartyHealth.Unavailable, f.Service.Load());
            Assert.False(f.Service.TryWithLootAccess("member", access, () => true));
            return true;
        }));
        Assert.True(f.Act("owner", PartyAction.Remove, "member").Success);
        Assert.False(Allowed(f, "member", access));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CommitAndLeaveOrKickSerialize(bool kick)
    {
        var f = new PartyTestFixture(); string id = f.Create("owner"); f.Join("owner", "member");
        var access = new PartyLootAccess("owner", PartyLootAudience.PartyOnly, id);
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        using var removalStarted = new ManualResetEventSlim();
        Task<bool> commit = Task.Run(() => f.Service.TryWithLootAccess("member", access, () =>
        { entered.Set(); Assert.True(release.Wait(TimeSpan.FromSeconds(5))); return true; }));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        Task<PartyCommandResult> remove = Task.Run(() =>
        { removalStarted.Set(); return f.Act(kick ? "owner" : "member", kick ? PartyAction.Remove : PartyAction.Leave, kick ? "member" : ""); });
        Assert.True(removalStarted.Wait(TimeSpan.FromSeconds(5)));
        Assert.False(remove.IsCompleted); release.Set();
        Assert.True(await commit); Assert.True((await remove).Success);
        Assert.False(Allowed(f, "member", access));
    }

    [Fact]
    public void HealthySharedPickupUsesCommittedSnapshotWithoutDiskWork()
    {
        var f = new PartyTestFixture(); string id = f.Create("owner"); f.Join("owner", "member");
        int reads = f.Repository.Reads;
        for (int i = 0; i < 10; i++)
            Assert.True(Allowed(f, "member", new("owner", PartyLootAudience.PartyOnly, id)));
        Assert.Equal(reads, f.Repository.Reads);
        var archive = f.Repository.Load().Archive!;
        archive.Parties[0].Members.Remove("member");
        long expected = archive.Generation++;
        Assert.Equal(VRPG.Parties.Persistence.PartyRepositorySaveStatus.Committed, f.Repository.Save(archive, expected));
        Assert.False(Allowed(f, "member", new("owner", PartyLootAudience.PartyOnly, id)));
    }

    [Fact]
    public void RawRepositoryFailureInvalidatesCachedSharedGrantsUntilLoad()
    {
        var f = new PartyTestFixture(); string id = f.Create("owner"); f.Join("owner", "member");
        var archive = f.Repository.Load().Archive!;
        f.Repository.FailBeforeWrite = true;
        Assert.Throws<IOException>(() => f.Repository.Save(archive, archive.Generation));
        Assert.Equal(PartyHealth.Unavailable,
            f.Service.Execute("member", new("invalid", -1, PartyAction.Leave), f.Now).View.Health);
        Assert.False(Allowed(f, "member", new("owner", PartyLootAudience.PartyOnly, id)));
        f.Repository.FailBeforeWrite = false;
        Assert.Equal(PartyHealth.Healthy, f.Service.Load());
        Assert.True(Allowed(f, "member", new("owner", PartyLootAudience.PartyOnly, id)));
    }

    [Fact]
    public void AnotherServiceCannotUseItsCachedRosterAfterRemoval()
    {
        var f = new PartyTestFixture(); string id = f.Create("owner"); f.Join("owner", "member");
        var second = new PartyService(f.Repository, f.Online.Contains); second.Load();
        Assert.Equal(id, second.Read("member", f.Now).PartyId);
        Assert.True(f.Act("owner", PartyAction.Remove, "member").Success);
        bool called = false;
        Assert.False(second.TryWithLootAccess("member", new("owner", PartyLootAudience.PartyOnly, id), () => called = true));
        Assert.False(called);
    }

    private static bool Allowed(PartyTestFixture fixture, string actor, PartyLootAccess access)
    {
        bool called = false;
        bool result = fixture.Service.TryWithLootAccess(actor, access, () => called = true);
        Assert.Equal(called, result);
        return result;
    }
}
