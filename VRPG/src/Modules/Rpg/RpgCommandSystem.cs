using System;
using System.Text;
using VRPG.Config;
using VRPG.Data;
using VRPG.Data.Definitions;
using VRPG.Modules.Rpg.Players;
using VRPG.Modules.Rpg.StatusEffects;
using VRPG.Modules.Rpg.Skills;
using VRPG.Modules.Rpg.Talents;
using VRPG.Network;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace VRPG.Modules.Rpg;

public sealed class RpgCommandSystem
{
    private readonly ICoreServerAPI api;
    private readonly RpgModuleConfig config;
    private readonly VRPGDataRegistry data;
    private readonly RpgPlayerStore playerStore;
    private readonly StatusEffectTracker statusEffects;
    private readonly RpgResourceService resources;
    private readonly SkillCastingService skills;
    private readonly TalentAllocationService talentAllocation;

    public RpgCommandSystem(ICoreServerAPI api, RpgModuleConfig config, VRPGDataRegistry data, RpgPlayerStore playerStore, StatusEffectTracker statusEffects, RpgResourceService resources, SkillCastingService skills, TalentAllocationService talentAllocation)
    {
        this.api = api;
        this.config = config;
        this.data = data;
        this.playerStore = playerStore;
        this.statusEffects = statusEffects;
        this.resources = resources;
        this.skills = skills;
        this.talentAllocation = talentAllocation;
    }

    public void Register()
    {
        api.ChatCommands.GetOrCreate("vrpg")
            .WithDescription("VRPG gameplay, authoring, and data commands.")
            .RequiresPrivilege(Privilege.chat)
            .BeginSubCommand("sheet")
                .WithDescription("Show your current VRPG RPG state.")
                .RequiresPlayer()
                .HandleWith(args => WithPlayer(args, ShowSheet))
            .EndSubCommand()
            .BeginSubCommand("talent")
                .WithDescription("Allocate or inspect talents.")
                .BeginSubCommand("take")
                    .WithDescription("Allocate a talent by code.")
                    .RequiresPlayer()
                    .WithArgs(api.ChatCommands.Parsers.Word("code"))
                    .HandleWith(args => WithPlayer(args, player => TakeTalent(player, (string)args[0])))
                .EndSubCommand()
                .BeginSubCommand("reset")
                    .WithDescription("Admin: refund all allocated talents for playground testing.")
                    .RequiresPrivilege(Privilege.controlserver)
                    .RequiresPlayer()
                    .HandleWith(args => WithPlayer(args, ResetTalents))
                .EndSubCommand()
            .EndSubCommand()
            .BeginSubCommand("effects")
                .WithDescription("Inspect and test VRPG status effects.")
                .BeginSubCommand("list")
                    .WithDescription("List loaded status-effect definitions.")
                    .HandleWith(_ => TextCommandResult.Success(data.StatusEffects.FormatList("Status effects", 32)))
                .EndSubCommand()
                .BeginSubCommand("self")
                    .WithDescription("Show your current tracked status effects.")
                    .RequiresPlayer()
                    .HandleWith(args => WithPlayer(args, ShowStatusEffects))
                .EndSubCommand()
                .BeginSubCommand("applyself")
                    .WithDescription("Apply a status effect to yourself for testing.")
                    .RequiresPrivilege(Privilege.controlserver)
                    .RequiresPlayer()
                    .WithArgs(
                        api.ChatCommands.Parsers.Word("code"),
                        api.ChatCommands.Parsers.OptionalFloat("durationSeconds", 0),
                        api.ChatCommands.Parsers.OptionalInt("stacks", 1))
                    .HandleWith(args => WithPlayer(args, player => ApplyStatusEffect(player, (string)args[0], (float)args[1], (int)args[2])))
                .EndSubCommand()
            .EndSubCommand()
            .BeginSubCommand("resources")
                .WithDescription("Inspect and test VRPG player resources.")
                .BeginSubCommand("self")
                    .WithDescription("Show your current HP, MP, shield, blood, and XP values.")
                    .RequiresPlayer()
                    .HandleWith(args => WithPlayer(args, ShowResources))
                .EndSubCommand()
                .BeginSubCommand("setself")
                    .WithDescription("Set a test resource value: health, mana, shield, or blood.")
                    .RequiresPrivilege(Privilege.controlserver)
                    .RequiresPlayer()
                    .WithArgs(
                        api.ChatCommands.Parsers.Word("resource"),
                        api.ChatCommands.Parsers.Float("current"),
                        api.ChatCommands.Parsers.Float("max"))
                    .HandleWith(args => WithPlayer(args, player => SetResource(player, (string)args[0], (float)args[1], (float)args[2])))
                .EndSubCommand()
                .BeginSubCommand("setxp")
                    .WithDescription("Set test XP and XP-to-next-level values.")
                    .RequiresPrivilege(Privilege.controlserver)
                    .RequiresPlayer()
                    .WithArgs(
                        api.ChatCommands.Parsers.Long("experience"),
                        api.ChatCommands.Parsers.Long("toNextLevel"))
                    .HandleWith(args => WithPlayer(args, player => SetExperience(player, (long)args[0], (long)args[1])))
                .EndSubCommand()
            .EndSubCommand()
            .BeginSubCommand("grantpoints")
                .WithDescription("Admin: grant yourself talent, stat, and respec points for testing.")
                .WithAdditionalInformation("Arguments are ordered as talent points, stat points, then respec points. Omitted values default to 1, 0, and 0.")
                .WithExamples("/vrpg grantpoints 10", "/vrpg grantpoints 10 5 3")
                .RequiresPrivilege(Privilege.controlserver)
                .RequiresPlayer()
                .WithArgs(
                    api.ChatCommands.Parsers.OptionalInt("talents", 1),
                    api.ChatCommands.Parsers.OptionalInt("stats", 0),
                    api.ChatCommands.Parsers.OptionalInt("respec", 0))
                .HandleWith(args => WithPlayer(args, player => GrantPoints(player, (int)args[0], (int)args[1], (int)args[2])))
            .EndSubCommand()
            .BeginSubCommand("skill")
                .WithDescription("Grant, equip, inspect, and test data-defined skills.")
                .BeginSubCommand("list")
                    .WithDescription("List loaded skill definitions.")
                    .HandleWith(_ => TextCommandResult.Success(data.Skills.FormatList("Skills", 64)))
                .EndSubCommand()
                .BeginSubCommand("loadout")
                    .WithDescription("Show your learned and equipped skill slots.")
                    .RequiresPlayer()
                    .HandleWith(args => WithPlayer(args, player => TextCommandResult.Success(skills.FormatLoadout(player))))
                .EndSubCommand()
                .BeginSubCommand("grant")
                    .WithDescription("Admin: grant yourself a skill at a specified skill level.")
                    .RequiresPrivilege(Privilege.controlserver)
                    .RequiresPlayer()
                    .WithArgs(
                        api.ChatCommands.Parsers.Word("code"),
                        api.ChatCommands.Parsers.OptionalInt("level", 1))
                    .HandleWith(args => WithPlayer(args, player => GrantSkill(player, (string)args[0], (int)args[1])))
                .EndSubCommand()
                .BeginSubCommand("grantall")
                    .WithDescription("Admin: grant yourself every loaded skill.")
                    .RequiresPrivilege(Privilege.controlserver)
                    .RequiresPlayer()
                    .WithArgs(api.ChatCommands.Parsers.OptionalInt("level", 1))
                    .HandleWith(args => WithPlayer(args, player => GrantAllSkills(player, (int)args[0])))
                .EndSubCommand()
                .BeginSubCommand("grantto")
                    .WithDescription("Admin: grant an online player a skill at a specified skill level.")
                    .RequiresPrivilege(Privilege.controlserver)
                    .WithArgs(
                        api.ChatCommands.Parsers.OnlinePlayer("player"),
                        api.ChatCommands.Parsers.Word("code"),
                        api.ChatCommands.Parsers.OptionalInt("level", 1))
                    .HandleWith(args => GrantSkillTo((IPlayer)args[0], (string)args[1], (int)args[2]))
                .EndSubCommand()
                .BeginSubCommand("grantallto")
                    .WithDescription("Admin: grant every loaded skill to an online player.")
                    .RequiresPrivilege(Privilege.controlserver)
                    .WithArgs(
                        api.ChatCommands.Parsers.OnlinePlayer("player"),
                        api.ChatCommands.Parsers.OptionalInt("level", 1))
                    .HandleWith(args => GrantAllSkillsTo((IPlayer)args[0], (int)args[1]))
                .EndSubCommand()
                .BeginSubCommand("remove")
                    .WithDescription("Admin: remove a learned skill from yourself and clear it from the loadout.")
                    .RequiresPrivilege(Privilege.controlserver)
                    .RequiresPlayer()
                    .WithArgs(api.ChatCommands.Parsers.Word("code"))
                    .HandleWith(args => WithPlayer(args, player => RemoveSkill(player, (string)args[0])))
                .EndSubCommand()
                .BeginSubCommand("equip")
                    .WithDescription("Equip a learned skill in slot 1-8; use clear as the code to empty it.")
                    .RequiresPlayer()
                    .WithArgs(
                        api.ChatCommands.Parsers.IntRange("slot", 1, 8),
                        api.ChatCommands.Parsers.Word("code"))
                    .HandleWith(args => WithPlayer(args, player => EquipSkill(player, (int)args[0], (string)args[1])))
                .EndSubCommand()
                .BeginSubCommand("cast")
                    .WithDescription("Cast an equipped skill slot without using its hotkey.")
                    .RequiresPlayer()
                    .WithArgs(api.ChatCommands.Parsers.IntRange("slot", 1, 8))
                    .HandleWith(args => WithPlayer(args, player => CastSkill(player, (int)args[0])))
                .EndSubCommand()
            .EndSubCommand();
    }

    private TextCommandResult WithPlayer(TextCommandCallingArgs args, System.Func<IServerPlayer, TextCommandResult> action)
    {
        if (args.Caller.Player is not IServerPlayer player)
        {
            return TextCommandResult.Error("Players only.");
        }

        return action(player);
    }

    private TextCommandResult ShowSheet(IServerPlayer player)
    {
        RpgPlayerState state = playerStore.GetOrCreate(player);
        var sb = new StringBuilder();
        sb.Append("VRPG sheet for ").Append(player.PlayerName)
            .Append(": level ").Append(state.Level)
            .Append(", exp ").Append(state.Experience).Append('/').Append(state.ExperienceToNextLevel)
            .Append(", talent points ").Append(state.UnspentTalentPoints)
            .Append(", respec points ").Append(state.RespecPoints)
            .Append(", stat points ").Append(state.UnspentStatPoints);

        sb.AppendLine().Append("Talents: ");
        sb.Append(state.Talents.Count == 0 ? "none" : string.Join(", ", state.Talents));

        return TextCommandResult.Success(sb.ToString());
    }

    private TextCommandResult TakeTalent(IServerPlayer player, string code)
    {
        return talentAllocation.TryAllocate(player, code, out string message)
            ? TextCommandResult.Success(message)
            : TextCommandResult.Error(message);
    }

    private TextCommandResult ResetTalents(IServerPlayer player)
    {
        return talentAllocation.TryReset(player, out string message)
            ? TextCommandResult.Success(message)
            : TextCommandResult.Error(message);
    }

    private TextCommandResult GrantPoints(IServerPlayer player, int talents, int stats, int respec)
    {
        RpgPlayerState state = playerStore.GetOrCreate(player);
        state.UnspentTalentPoints += Math.Max(0, talents);
        state.UnspentStatPoints += Math.Max(0, stats);
        state.RespecPoints += Math.Max(0, respec);
        playerStore.Save();
        resources.SendSnapshot(player);
        return TextCommandResult.Success(
            $"Granted {Math.Max(0, talents)} talent, {Math.Max(0, stats)} stat, and {Math.Max(0, respec)} respec point(s).");
    }

    private TextCommandResult GrantSkill(IServerPlayer player, string code, int level)
    {
        return skills.Learn(player, code, level, out string message)
            ? TextCommandResult.Success(message)
            : TextCommandResult.Error(message);
    }

    private TextCommandResult GrantAllSkills(IServerPlayer player, int level)
    {
        int count = skills.LearnAll(player, Math.Max(1, level));
        return TextCommandResult.Success($"Granted {count} skill(s) at requested level {Math.Max(1, level)}.");
    }

    private TextCommandResult GrantSkillTo(IPlayer target, string code, int level)
    {
        if (target is not IServerPlayer serverPlayer)
        {
            return TextCommandResult.Error("Target player is not available on the server.");
        }

        return skills.Learn(serverPlayer, code, level, out string message)
            ? TextCommandResult.Success(serverPlayer.PlayerName + ": " + message)
            : TextCommandResult.Error(message);
    }

    private TextCommandResult GrantAllSkillsTo(IPlayer target, int level)
    {
        if (target is not IServerPlayer serverPlayer)
        {
            return TextCommandResult.Error("Target player is not available on the server.");
        }

        int count = skills.LearnAll(serverPlayer, Math.Max(1, level));
        return TextCommandResult.Success($"Granted {count} skill(s) to {serverPlayer.PlayerName} at requested level {Math.Max(1, level)}.");
    }

    private TextCommandResult RemoveSkill(IServerPlayer player, string code)
    {
        string normalized = NormalizeCode(code);
        RpgPlayerState state = playerStore.GetOrCreate(player);
        if (!state.SkillLevels.Remove(normalized))
        {
            return TextCommandResult.Error("Skill is not learned: " + normalized);
        }

        for (int i = 0; i < state.EquippedSkills.Length; i++)
        {
            if (SameCode(state.EquippedSkills[i] ?? "", normalized))
            {
                state.EquippedSkills[i] = "";
            }
        }

        playerStore.Save();
        return TextCommandResult.Success("Removed " + normalized + " and cleared it from the loadout.");
    }

    private TextCommandResult EquipSkill(IServerPlayer player, int slot, string code)
    {
        return skills.Equip(player, slot - 1, code, out string message)
            ? TextCommandResult.Success(message)
            : TextCommandResult.Error(message);
    }

    private TextCommandResult CastSkill(IServerPlayer player, int slot)
    {
        return skills.TryCastSlot(player, slot - 1, out string error)
            ? TextCommandResult.Success("Cast skill slot " + slot + ".")
            : TextCommandResult.Error(error);
    }

    private TextCommandResult ShowStatusEffects(IServerPlayer player)
    {
        return TextCommandResult.Success(statusEffects.Format(player.Entity.EntityId));
    }

    private TextCommandResult ApplyStatusEffect(IServerPlayer player, string code, float durationSeconds, int stacks)
    {
        if (!statusEffects.Apply(player.Entity.EntityId, code, player.Entity.EntityId, durationSeconds, stacks))
        {
            return TextCommandResult.Error("Unknown status effect: " + code);
        }

        return TextCommandResult.Success("Applied " + NormalizeCode(code) + ". " + statusEffects.Format(player.Entity.EntityId));
    }

    private TextCommandResult ShowResources(IServerPlayer player)
    {
        RpgResourcePacket packet = resources.BuildSnapshot(player);
        return TextCommandResult.Success(
            $"HP {Format(packet.Health)}/{Format(packet.MaxHealth)}, "
            + $"MP {Format(packet.Mana)}/{Format(packet.MaxMana)}, "
            + $"Shield {Format(packet.MagicShield)}/{Format(packet.MaxMagicShield)}, "
            + (packet.BloodUnlocked ? $"Blood {Format(packet.Blood)}/{Format(packet.MaxBlood)}, " : "Blood locked, ")
            + $"Level {packet.Level}, XP {packet.Experience}/{packet.ExperienceToNextLevel}");
    }

    private TextCommandResult SetResource(IServerPlayer player, string resource, float current, float max)
    {
        try
        {
            resources.SetResource(player, resource, current, max);
            return TextCommandResult.Success("Set " + resource + " to " + Format(current) + "/" + Format(max) + ".");
        }
        catch (ArgumentException ex)
        {
            return TextCommandResult.Error(ex.Message);
        }
    }

    private TextCommandResult SetExperience(IServerPlayer player, long experience, long toNextLevel)
    {
        resources.SetExperience(player, experience, toNextLevel);
        return TextCommandResult.Success("Set XP to " + experience + "/" + toNextLevel + ".");
    }

    private static bool SameCode(string left, string right)
    {
        return string.Equals(NormalizeCode(left), NormalizeCode(right), StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeCode(string code)
    {
        return code.Contains(':') ? code : "vrpg:" + code;
    }

    private static string Format(float value)
    {
        return value.ToString("0.#");
    }
}
