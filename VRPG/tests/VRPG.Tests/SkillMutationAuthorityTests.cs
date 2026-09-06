using System;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json;
using VRPG.Config;
using VRPG.Data.Library;
using VRPG.Modules.Rpg;
using VRPG.Modules.Rpg.Skills;
using VRPG.Modules.Rpg.Stats;
using VRPG.Network;
using Vintagestory.API.Common;
using Xunit;
using Fixture = VRPG.Tests.SkillCastingAuthorityTests.Fixture;

namespace VRPG.Tests;

[Collection("EntityDamageScalingPatch")]
public sealed class SkillMutationAuthorityTests
{
    [Fact]
    public void UnavailableEmpowerCommandReportsAvailabilityInsteadOfAnEmptySlot()
    {
        var f = new Fixture(false);
        f.Resources.IsActive = _ => false;
        using var module = Module(f);
        var system = new VRPGModSystem();
        AccessTools.Field(typeof(VRPGModSystem), "rpgModule").SetValue(system, module);
        var args = new TextCommandCallingArgs
        {
            Caller = new Caller { Player = f.Player },
            Parsers = new System.Collections.Generic.List<ICommandArgumentParser>
            {
                PlayerDefenseServiceTests.Proxy.Create<ICommandArgumentParser>((name, _) => name == "GetValue" ? 1 : throw new NotSupportedException(name)),
                PlayerDefenseServiceTests.Proxy.Create<ICommandArgumentParser>((name, _) => name == "GetValue" ? "on" : throw new NotSupportedException(name))
            }
        };
        var reply = (TextCommandResult)AccessTools.Method(typeof(VRPGModSystem), "HandleVfxEmpower").Invoke(system, new object[] { args })!;
        Assert.Equal(EnumCommandStatus.Error, reply.Status);
        Assert.Contains("unavailable", reply.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(f.SentPackets);
    }

    [Fact]
    public void ActiveLoadoutStillInitializesAndRechargesCharges()
    {
        var f = new Fixture(false);
        f.Skill.Charges.Maximum = 2;
        Assert.Equal(2, f.Skills.BuildLoadout(f.Player).Slots[0].CurrentCharges);
        Assert.True(f.Skills.TryCastSlot(f.Player, 0, out _));
        Assert.Equal(1, f.Skills.BuildLoadout(f.Player).Slots[0].CurrentCharges);
        f.Now += 1000;
        using var module = Module(f);
        AccessTools.Method(typeof(RpgModule), "SendLoadout").Invoke(module, new object[] { f.Player });
        Assert.Equal(2, Assert.IsType<SkillLoadoutPacket>(f.SentPackets[^1]).Slots[0].CurrentCharges);
    }

    [Fact]
    public void UnavailableTextLoadoutCannotCreateAPlayerProfile()
    {
        var f = new Fixture(false);
        f.Resources.IsActive = _ => false;
        var states = (System.Collections.IDictionary)AccessTools.Field(f.Store.GetType(), "states").GetValue(f.Store)!;
        states.Clear();
        int writes = f.Storage.Writes;
        string text = f.Skills.FormatLoadout(f.Player);
        Assert.Empty(states);
        Assert.Equal(writes, f.Storage.Writes);
        Assert.Contains("unavailable", text, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("learn", false)]
    [InlineData("learnall", false)]
    [InlineData("equip", false)]
    [InlineData("clear", false)]
    [InlineData("empower", false)]
    [InlineData("learn", true)]
    [InlineData("learnall", true)]
    [InlineData("equip", true)]
    [InlineData("clear", true)]
    [InlineData("empower", true)]
    public void UnavailableSkillMutationPreservesState(string operation, bool invalidGear)
    {
        var f = new Fixture(false);
        f.Resources.IsActive = _ => invalidGear;
        f.Skills.EquippedStatsProvider = _ => invalidGear ? EquippedGearStats.Invalid : EquippedGearStats.Empty;
        string before = JsonConvert.SerializeObject(f.State);
        string empowered = FieldState(f.Skills, "empoweredSkills");
        int writes = f.Storage.Writes;
        switch (operation)
        {
            case "learn": f.Skills.Learn(f.Player, "test", 3, out _); break;
            case "learnall": f.Skills.LearnAll(f.Player, 3, out _, out _); break;
            case "equip": f.Skills.Equip(f.Player, 1, "test", out _); break;
            case "clear": f.Skills.Equip(f.Player, 0, "clear", out _); break;
            case "empower": f.Skills.SetEmpowered(f.Player, "test", true, out _); break;
        }
        Assert.Equal(before, JsonConvert.SerializeObject(f.State));
        Assert.Equal(empowered, FieldState(f.Skills, "empoweredSkills"));
        Assert.Equal(writes, f.Storage.Writes);
        Assert.Empty(f.SentPackets);
    }

    [Theory]
    [InlineData("GrantSkill", false)]
    [InlineData("GrantAllSkills", false)]
    [InlineData("GrantSkillTo", false)]
    [InlineData("GrantAllSkillsTo", false)]
    [InlineData("RemoveSkill", false)]
    [InlineData("EquipSkill", false)]
    [InlineData("SetResource", false)]
    [InlineData("SetExperience", false)]
    [InlineData("GrantSkill", true)]
    [InlineData("GrantAllSkills", true)]
    [InlineData("GrantSkillTo", true)]
    [InlineData("GrantAllSkillsTo", true)]
    [InlineData("RemoveSkill", true)]
    [InlineData("EquipSkill", true)]
    [InlineData("SetResource", true)]
    [InlineData("SetExperience", true)]
    public void MutationCommandsReportActualAvailability(string method, bool active)
    {
        var f = new Fixture(false);
        f.Resources.IsActive = _ => active;
        var commands = new RpgCommandSystem(f.Api, new(), f.Data, f.Store, null!, f.Resources, f.Skills, null!);
        object[] args = method switch
        {
            "GrantSkill" or "GrantSkillTo" => new object[] { f.Player, "test", 3 },
            "GrantAllSkills" or "GrantAllSkillsTo" => new object[] { f.Player, 3 },
            "RemoveSkill" => new object[] { f.Player, "test" },
            "EquipSkill" => new object[] { f.Player, 2, "test" },
            "SetResource" => new object[] { f.Player, "mana", 25f, 100f },
            _ => new object[] { f.Player, 25L, 1000L }
        };
        string before = JsonConvert.SerializeObject(f.State);
        int writes = f.Storage.Writes;
        var result = (TextCommandResult)AccessTools.Method(typeof(RpgCommandSystem), method).Invoke(commands, args)!;
        Assert.Equal(active ? EnumCommandStatus.Success : EnumCommandStatus.Error, result.Status);
        if (active)
        {
            Assert.NotEqual(before, JsonConvert.SerializeObject(f.State));
            Assert.True(f.Storage.Writes > writes);
            switch (method)
            {
                case "GrantSkill": case "GrantSkillTo": case "GrantAllSkills": case "GrantAllSkillsTo":
                    Assert.Equal(3, f.State.GetSkillLevel("vrpg:test")); break;
                case "RemoveSkill":
                    Assert.Equal(0, f.State.GetSkillLevel("vrpg:test"));
                    Assert.DoesNotContain("vrpg:test", f.State.EquippedSkills); break;
                case "EquipSkill": Assert.Equal("vrpg:test", f.State.EquippedSkills[1]); break;
                case "SetResource": Assert.Equal(25, f.State.Mana); break;
                case "SetExperience": Assert.Equal(25, f.State.Experience); break;
            }
        }
        else
        {
            Assert.Contains("unavailable", result.StatusMessage, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(before, JsonConvert.SerializeObject(f.State));
            Assert.Equal(writes, f.Storage.Writes);
            Assert.Empty(f.SentPackets);
        }
    }

    [Theory]
    [InlineData("build", false)]
    [InlineData("build", true)]
    [InlineData("send", false)]
    [InlineData("send", true)]
    [InlineData("periodic", false)]
    [InlineData("periodic", true)]
    public void UnavailableLoadoutDoesNotInitializeOrRechargeCharges(string path, bool existingCharges)
    {
        var f = new Fixture(false);
        f.Skill.Charges.Maximum = 2;
        if (existingCharges) Assert.True(f.Skills.TryCastSlot(f.Player, 0, out _));
        var tracker = AccessTools.Field(typeof(SkillCastingService), "chargeTracker").GetValue(f.Skills)!;
        string before = FieldState(tracker, "states");
        string state = JsonConvert.SerializeObject(f.State);
        int writes = f.Storage.Writes, packets = f.Packets;
        f.Resources.IsActive = _ => false;
        f.Now += 5000;
        using var module = Module(f);
        if (path == "build") Assert.Empty(f.Skills.BuildLoadout(f.Player).Slots);
        else if (path == "send") AccessTools.Method(typeof(RpgModule), "SendLoadout").Invoke(module, new object[] { f.Player });
        else
        {
            var world = PlayerDefenseServiceTests.Proxy.Create<Vintagestory.API.Server.IServerWorldAccessor>((_, _) => new IPlayer[] { f.Player });
            var api = PlayerDefenseServiceTests.Proxy.Create<Vintagestory.API.Server.ICoreServerAPI>((_, _) => world);
            AccessTools.Method(typeof(RpgModule), "BroadcastLoadouts").Invoke(module, new object[] { api });
        }
        Assert.Equal(before, FieldState(tracker, "states"));
        Assert.Equal(state, JsonConvert.SerializeObject(f.State));
        Assert.Equal(writes, f.Storage.Writes);
        Assert.Equal(packets, f.Packets);
    }

    [Fact]
    public void UnavailableEquipPacketReportsFailureWithoutSkillMutationOrLoadoutSend()
    {
        var f = new Fixture(false);
        f.Resources.IsActive = _ => false;
        using var module = Module(f);
        var system = new VRPGModSystem();
        AccessTools.Field(typeof(VRPGModSystem), "rpgModule").SetValue(system, module);
        AccessTools.Field(typeof(VRPGModSystem), "serverChannel").SetValue(system, f.Channel);
        AccessTools.Field(typeof(VRPGModSystem), "libraryIndex").SetValue(system, LibraryIndex.Build(f.Data));
        string before = JsonConvert.SerializeObject(f.State);
        int writes = f.Storage.Writes;
        AccessTools.Method(typeof(VRPGModSystem), "HandleSkillEquipRequest").Invoke(system,
            new object[] { f.Api, f.Player, new SkillEquipRequestPacket { Slot = 2, SkillCode = "test" } });
        Assert.Equal(before, JsonConvert.SerializeObject(f.State));
        Assert.Equal(writes, f.Storage.Writes);
        var feedback = Assert.IsType<OpenHubPacket>(Assert.Single(f.SentPackets));
        Assert.True(feedback.FeedbackError);
        Assert.Contains("unavailable", feedback.FeedbackMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmpowermentHonorsAvailabilityBeforeMutationOrLoadoutSend(bool active)
    {
        var f = new Fixture(false);
        f.Resources.IsActive = _ => active;
        using var module = Module(f);
        string before = FieldState(f.Skills, "empoweredSkills");
        Assert.Equal(active, module.SetSkillEmpowered(f.Player, "test", true, out _));
        if (active)
        {
            Assert.NotEqual(before, FieldState(f.Skills, "empoweredSkills"));
            Assert.True(Assert.Single(f.SentPackets.OfType<SkillLoadoutPacket>()).Slots[0].Empowered);
        }
        else
        {
            Assert.Equal(before, FieldState(f.Skills, "empoweredSkills"));
            Assert.Empty(f.SentPackets);
        }
    }

    private static RpgModule Module(Fixture f)
    {
        var module = new RpgModule(new RpgModuleConfig(), f.Data, f.Channel);
        AccessTools.Field(typeof(RpgModule), "skills").SetValue(module, f.Skills);
        return module;
    }

    private static string FieldState(object owner, string field) => JsonConvert.SerializeObject(
        AccessTools.Field(owner.GetType(), field).GetValue(owner));
}
