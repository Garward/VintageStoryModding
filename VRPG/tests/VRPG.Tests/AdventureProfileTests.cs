using System;
using System.Collections.Generic;
using System.Linq;
using VRPG.Inventory;
using VRPG.Inventory.Persistence;
using Xunit;

namespace VRPG.Tests;

[Collection("EngineItemBirth")]
public sealed class AdventureProfileTests
{
    [Theory]
    [InlineData(true, true, true)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    public void ProfileRequiresBothServerOptions(bool rpg, bool replacement, bool expected) =>
        Assert.Equal(expected, AdventureProfile.IsEnabled(rpg, replacement));

    [Theory]
    [InlineData("none", true)]
    [InlineData("store", false)]
    [InlineData("durability", false)]
    [InlineData("guards", false)]
    [InlineData("menu", false)]
    public void AdmissionRequiresDurabilityProtectionAndMenuBeforeTouchingLegacySlots(string failure, bool expected)
    {
        var storage = new Storage { IsCrashDurable = failure != "durability" };
        var archive = new PlayerItemArchiveService(new(storage));
        if (failure != "store") Assert.True(archive.Load().Success);
        var profile = Profile(archive, () => failure != "guards");
        var native = new NativeLegacyInventoryFixture();
        var result = profile.Prepare(native.Source, failure != "menu");
        Assert.Equal(expected, result.Success);
        Assert.Equal(expected ? 0 : 10, native.Quantity);
        Assert.Equal(expected, profile.CanParticipate("player", failure != "menu"));
    }

    [Fact]
    public void FirstJoinMigratesBeforeReadinessAndReconnectDoesNotReimport()
    {
        var archive = new PlayerItemArchiveService(new(new Storage()));
        Assert.True(archive.Load().Success);
        var profile = Profile(archive, () => true);
        var native = new NativeLegacyInventoryFixture();
        Assert.False(profile.CanParticipate("player", true));
        Assert.True(profile.Prepare(native.Source, true).Success);
        Assert.True(profile.CanParticipate("player", true));
        Assert.Equal(10, archive.Directory.ForPlayer("player").Query(new("", "", 0, 100)).Entries.Sum(e => e.Quantity));
        long generation = archive.Generation;
        Assert.True(profile.Prepare(native.Source, true).Success);
        Assert.Equal(generation, archive.Generation);
    }

    [Fact]
    public void MigrationFailureKeepsLegacyItemsAndNeverAdvertisesCanonicalReadiness()
    {
        var storage = new Storage();
        var archive = new PlayerItemArchiveService(new(storage));
        Assert.True(archive.Load().Success);
        storage.FailWrites = true;
        var profile = Profile(archive, () => true);
        var native = new NativeLegacyInventoryFixture();
        Assert.False(profile.Prepare(native.Source, true).Success);
        Assert.False(profile.CanParticipate("player", true));
        Assert.Equal(10, native.Quantity);
    }

    internal static AdventureProfile Profile(PlayerItemArchiveService archive, Func<bool> protection) =>
        new(true, () => archive, protection, uid =>
        {
            var confirmation = archive.MigrationConfirmation(uid);
            return new PlayerItemCustodyPolicy().Observe(uid, archive.IsLoaded && confirmation.IsConfirmed,
                confirmation.Phase, true, true);
        });

    internal sealed class Storage : IDurablePlayerItemArchiveStorage
    {
        private readonly Dictionary<string, byte[]> values = new();
        public bool IsCrashDurable { get; set; } = true;
        public bool FailWrites;
        public byte[]? Read(string key) => values.TryGetValue(key, out var bytes) ? (byte[])bytes.Clone() : null;
        public void Write(string key, byte[] bytes)
        {
            if (FailWrites) throw new InvalidOperationException("Injected archive failure");
            values[key] = (byte[])bytes.Clone();
        }
    }
}
