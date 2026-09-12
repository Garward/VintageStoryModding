using System;
using System.Collections.Generic;
using VRPG.Client.PlayerMenu.Hub;
using VRPG.Network;
using Xunit;

namespace VRPG.Tests;

public sealed class StatsSkillsPageTests
{
    [Fact]
    public void SimpleControlQueriesDoNotCloneUnrelatedHubContent()
    {
        using var f = new Fixture(); var packet = Fixture.Packet();
        packet.Classes[0].Description = new string('x', 100000); packet.Options.SkillHotbarSlots = 99; f.Hub.Receive(packet);
        _ = f.Hub.Skills.VisibleSlots; _ = f.Hub.Stats.CanAdjust("strength", 1);
        long before = GC.GetAllocatedBytesForCurrentThread(); int slots = 0; bool allowed = true;
        for (int i = 0; i < 10; i++) { slots = f.Hub.Skills.VisibleSlots; allowed &= f.Hub.Stats.CanAdjust("strength", 1); }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(8, slots); Assert.True(allowed);
        Assert.True(allocated < 65536, $"Simple control queries allocated {allocated:N0} bytes by copying unrelated content.");
        var external = f.Hub.Packet!; external.Options.SkillHotbarSlots = 4; external.Resources!.UnspentStatPoints = 0;
        Assert.Equal(8, f.Hub.Skills.VisibleSlots); Assert.True(f.Hub.Stats.CanAdjust("strength", 1));
        f.Hub.Receive(new RpgResourcePacket { UnspentStatPoints = 0, CombatLockRemainingSeconds = 2 });
        Assert.False(f.Hub.Stats.CanAdjust("strength", 1)); Assert.False(f.Hub.Stats.CanAdjust("strength", -1));
        f.Hub.Receive(new RpgResourcePacket { UnspentStatPoints = 1 }); Assert.True(f.Hub.Stats.CanAdjust("strength", 1));
        packet.Options.SkillHotbarSlots = 2; f.Hub.Receive(packet); Assert.Equal(4, f.Hub.Skills.VisibleSlots);
    }

    [Theory]
    [InlineData("strength")][InlineData("dexterity")][InlineData("intelligence")]
    public void AllocationSendsExactCodeAndDeltaAndSuppressesDuplicates(string code)
    {
        using var fixture = new Fixture();
        fixture.Hub.Stats.Adjust(code, 1);
        fixture.Hub.Stats.Adjust(code, 1);
        Assert.Equal(new[] { (code, 1) }, fixture.Stats);
        Assert.True(fixture.Hub.Stats.Pending);
        Assert.Equal(3, fixture.Hub.Packet!.BaseStats[0].Value);
    }

    [Theory]
    [InlineData("vrpg:strength", 1)][InlineData("health", 1)][InlineData("strength", 0)][InlineData("strength", 2)]
    public void UnsupportedAdjustmentNeverSends(string code, int delta)
    {
        using var fixture = new Fixture(); fixture.Hub.Stats.Adjust(code, delta);
        Assert.Empty(fixture.Stats);
    }

    [Fact]
    public void StatControlsRespectCombatPointsAndAllowFreeRefunds()
    {
        using var f = new Fixture(); var packet = Fixture.Packet();
        packet.Resources!.CombatLockRemainingSeconds = 2; f.Hub.Receive(packet); f.Hub.Stats.Adjust("strength", 1);
        packet.Resources.CombatLockRemainingSeconds = 0; packet.Resources.UnspentStatPoints = 0;
        packet.Resources.RespecPoints = 0; f.Hub.Receive(packet);
        f.Hub.Stats.Adjust("strength", 1);
        Assert.Empty(f.Stats);
        f.Hub.Stats.Adjust("strength", -1);
        Assert.Equal(new[] { ("strength", -1) }, f.Stats);
    }

    [Fact]
    public void ResourceUpdateDoesNotReleasePendingButHubRejectionDoes()
    {
        using var f = new Fixture(); f.Hub.Stats.Adjust("strength", 1);
        long revision = f.Hub.SnapshotRevision;
        f.Hub.Receive(new RpgResourcePacket { Level = 8 });
        Assert.True(f.Hub.Stats.Pending); Assert.Equal(revision, f.Hub.SnapshotRevision);
        var response = Fixture.Packet(); response.FeedbackMessage = "Stats are locked during combat."; response.FeedbackError = true;
        f.Hub.Receive(response);
        Assert.False(f.Hub.Stats.Pending); Assert.True(f.Hub.Stats.FeedbackError);
        Assert.Equal("Stats are locked during combat.", f.Hub.Stats.Feedback);
        Assert.Equal("strength", f.Hub.Stats.FeedbackCode);
        f.Hub.Stats.Adjust("dexterity", 1); Assert.Equal(2, f.Stats.Count);
    }

    [Fact]
    public void LearnedSkillUsesOneBasedVisibleSlotAndSameSlotUnequips()
    {
        using var f = new Fixture(); f.Hub.Skills.EquipSelected(2);
        Assert.Equal(new[] { (3, "vrpg:cleave") }, f.Equips);
        f.Hub.Skills.EquipSelected(1); Assert.Single(f.Equips);
        var response = Fixture.Packet(); response.Classes[0].Skills[0].EquippedSlot = 3; f.Hub.Receive(response);
        f.Hub.Skills.EquipSelected(2);
        Assert.Equal((3, ""), f.Equips[1]);
    }

    [Fact]
    public void LockedSkillsAndHiddenSlotsNeverSend()
    {
        using var f = new Fixture(); f.Hub.Skills.EquipSelected(-1); f.Hub.Skills.EquipSelected(4);
        f.Hub.Skills.SelectSkill(1); f.Hub.Skills.EquipSelected(0);
        Assert.Empty(f.Equips);
    }

    [Fact]
    public void RefreshPreservesIdentityAndClampsRemovedClassAndSkill()
    {
        using var f = new Fixture(); f.Hub.Skills.SelectSkill(99);
        Assert.Equal(1, f.Hub.Skills.SelectedSkillIndex);
        var response = Fixture.Packet(); Array.Reverse(response.Classes[0].Skills); f.Hub.Receive(response);
        Assert.Equal("vrpg:locked", f.Hub.Skills.SelectedSkill!.Code);
        f.Hub.Skills.SelectClass(99); Assert.Equal(0, f.Hub.Skills.SelectedClassIndex);
        response.Classes[0].Skills = new[] { response.Classes[0].Skills[1] }; f.Hub.Receive(response);
        Assert.Equal(0, f.Hub.Skills.SelectedSkillIndex);
        response.Classes = Array.Empty<HubClassPacket>(); f.Hub.Receive(response);
        Assert.Null(f.Hub.Skills.SelectedSkill); f.Hub.Skills.EquipSelected(0); Assert.Empty(f.Equips);
    }

    [Fact]
    public void SkillPendingSurvivesResourceUpdatesAndRejectionKeepsSelection()
    {
        using var f = new Fixture(); f.Hub.Skills.EquipSelected(0);
        f.Hub.Receive(new RpgResourcePacket { Level = 8 });
        Assert.True(f.Hub.Skills.Pending);
        var response = Fixture.Packet(); response.FeedbackError = true; response.FeedbackMessage = "That skill is not learned.";
        f.Hub.Receive(response);
        Assert.False(f.Hub.Skills.Pending); Assert.Equal("vrpg:cleave", f.Hub.Skills.SelectedSkill!.Code);
        Assert.True(f.Hub.Skills.FeedbackError); Assert.Equal("That skill is not learned.", f.Hub.Skills.Feedback);
    }

    [Fact]
    public void ClassIdentitySurvivesReorderingAndRemovedSelectionClampsToLastClass()
    {
        using var f = new Fixture(); var packet = Fixture.Packet();
        var warrior = packet.Classes[0]; var mage = new HubClassPacket { Code = "vrpg:mage", Name = "Mage" };
        packet.Classes = new[] { warrior, mage }; f.Hub.Receive(packet); f.Hub.Skills.SelectClass(1);
        packet.Classes = new[] { mage, warrior }; f.Hub.Receive(packet);
        Assert.Equal(0, f.Hub.Skills.SelectedClassIndex); Assert.Null(f.Hub.Skills.SelectedSkill);
        packet.Classes = new[] { warrior }; f.Hub.Receive(packet);
        Assert.Equal(0, f.Hub.Skills.SelectedClassIndex); Assert.Equal("vrpg:cleave", f.Hub.Skills.SelectedSkill!.Code);
    }

    [Fact]
    public void PagesShareMutationGateAndResetWithConnection()
    {
        using var f = new Fixture(); f.Hub.Skills.EquipSelected(0); f.Hub.Stats.Adjust("strength", 1);
        Assert.Empty(f.Stats); f.Hub.ResetConnection();
        Assert.False(f.Hub.Skills.Pending); Assert.False(f.Hub.Stats.Pending);
        Assert.Equal("", f.Hub.State.SelectedSkill);
    }

    internal sealed class Fixture : IDisposable
    {
        public List<(string, int)> Stats { get; } = new();
        public List<(int, string)> Equips { get; } = new();
        public HubPageClientService Hub { get; }
        public Fixture()
        {
            Hub = new(_ => { }, new() { AdjustStat = (code, delta) => Stats.Add((code, delta)), EquipSkill = (slot, code) => Equips.Add((slot, code)) });
            Hub.Receive(Packet());
        }
        public static OpenHubPacket Packet() => new()
        {
            Resources = new() { Level = 8, Health = 85, MaxHealth = 120, Mana = 40, MaxMana = 60, MagicShield = 12, MaxMagicShield = 30,
                UnspentStatPoints = 2, RespecPoints = 1, Experience = 340, ExperienceToNextLevel = 800, HealthRegenPerSecond = 1.5f, ManaRegenPerSecond = 2 },
            BaseStats = new[] { new HubStatPacket { Code = "vrpg:strength", Value = 3 }, new HubStatPacket { Code = "dexterity", Value = 2 }, new HubStatPacket { Code = "intelligence", Value = 1 } },
            Classes = new[] { new HubClassPacket { Code = "vrpg:warrior", Name = "Warrior", Description = "A close combat specialist.", Skills = new[] {
                new HubSkillPacket { Code = "vrpg:cleave", Name = "Cleave", Description = "Sweep your weapon through nearby enemies.", LearnedLevel = 1, MaxLevel = 5, RequiredLevel = 1, Damage = 12, Range = 3, CooldownSeconds = 2, ResourceType = "mana", ResourceCost = 4 },
                new HubSkillPacket { Code = "vrpg:locked", Name = "Whirlwind", Description = "Strike enemies around you.", RequiredLevel = 12, MaxLevel = 5 } } } }
        };
        public void Dispose() => Hub.Dispose();
    }
}
