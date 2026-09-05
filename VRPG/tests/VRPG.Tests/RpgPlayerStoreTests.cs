using System.Collections.Generic;
using VRPG.Modules.Rpg.Players;
using Xunit;

namespace VRPG.Tests;

public sealed class RpgPlayerStoreTests
{
    [Fact]
    public void ProgressionRoundTripsInsideOneWorldStorage()
    {
        var storage = new MemoryRpgPlayerStateStorage();
        var first = new RpgPlayerStore();
        first.Load(storage);
        RpgPlayerState state = first.GetOrCreate("player-one");
        state.Level = 17;
        state.SkillLevels["vrpg:junk_toss"] = 4;
        state.EquippedSkills[0] = "vrpg:junk_toss";
        first.Save();

        var restored = new RpgPlayerStore();
        restored.Load(storage);
        RpgPlayerState loaded = restored.GetOrCreate("PLAYER-ONE");

        Assert.Equal(17, loaded.Level);
        Assert.Equal(4, loaded.GetSkillLevel("VRPG:JUNK_TOSS"));
        Assert.Equal("vrpg:junk_toss", loaded.EquippedSkills[0]);
    }

    [Fact]
    public void SamePlayerInAnotherWorldStartsWithoutLearnedOrEquippedSkills()
    {
        var firstWorld = new MemoryRpgPlayerStateStorage();
        var original = new RpgPlayerStore();
        original.Load(firstWorld);
        RpgPlayerState progressed = original.GetOrCreate("player-one");
        progressed.SkillLevels["vrpg:junk_toss"] = 5;
        progressed.EquippedSkills[0] = "vrpg:junk_toss";
        original.Save();

        var secondWorld = new RpgPlayerStore();
        secondWorld.Load(new MemoryRpgPlayerStateStorage());
        RpgPlayerState fresh = secondWorld.GetOrCreate("player-one");

        Assert.Empty(fresh.SkillLevels);
        Assert.All(fresh.EquippedSkills, Assert.Empty);
        Assert.Equal(1, fresh.Level);
    }

    [Fact]
    public void EmptyWorldDoesNotImportLegacyGlobalProgression()
    {
        var world = new MemoryRpgPlayerStateStorage();
        var store = new RpgPlayerStore();

        store.Load(world);

        Assert.Null(world.Data);
    }

    private sealed class MemoryRpgPlayerStateStorage : IRpgPlayerStateStorage
    {
        public byte[]? Data { get; private set; }

        public byte[]? Read() => Data == null ? null : (byte[])Data.Clone();

        public void Write(byte[] data)
        {
            Data = (byte[])data.Clone();
        }
    }
}
