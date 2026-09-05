using System;
using VRPG.Core;
using VRPG.Inventory.Migration;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace VRPG.Inventory.Persistence;

public sealed class PlayerItemPersistenceModule : IVrpgModule
{
    private ICoreServerAPI? api;

    public string Code => "player-items";
    public PlayerItemArchiveService? Service { get; private set; }
    public bool IsReady => Service?.IsLoaded == true;

    public void StartServerSide(ICoreServerAPI api)
    {
        this.api = api;
        Service = new PlayerItemArchiveService(new PlayerItemArchiveRepository(
            new VintageStoryPlayerItemArchiveStorage(api.WorldManager.SaveGame)));
        api.Event.SaveGameLoaded += OnSaveGameLoaded;
        api.Event.GameWorldSave += OnGameWorldSave;
        RegisterCommands(api);
    }

    public void Dispose()
    {
        if (api != null)
        {
            api.Event.SaveGameLoaded -= OnSaveGameLoaded;
            api.Event.GameWorldSave -= OnGameWorldSave;
        }
        api = null;
        Service = null;
    }

    private void OnSaveGameLoaded()
    {
        if (api == null || Service == null) return;
        PlayerItemArchiveLoadResult result = Service.Load();
        if (!result.Success)
        {
            api.Logger.Error(
                "[VRPG/Inventory] Player-item persistence is unavailable: {0}",
                result.Error);
            return;
        }

        if (result.RecoveredFromOlderGeneration)
        {
            api.Logger.Warning(
                "[VRPG/Inventory] Recovered player items from generation {0}; a newer snapshot was invalid.",
                result.Generation);
        }
        api.Logger.Notification(
            "[VRPG/Inventory] Loaded {0} player item stores at generation {1}.",
            result.Archive.Players.Count,
            result.Generation);
    }

    private void OnGameWorldSave()
    {
        if (api == null || Service?.IsLoaded != true) return;
        PlayerItemArchiveSaveResult result = Service.SaveDirty();
        if (!result.Success)
        {
            api.Logger.Error(
                "[VRPG/Inventory] Player-item snapshot failed; the prior generation remains recoverable: {0}",
                result.Error);
        }
    }

    private void RegisterCommands(ICoreServerAPI api)
    {
        api.ChatCommands.Create("vrpgitems")
            .WithDescription("Inspect VRPG inventory migration and recovery.")
            .RequiresPrivilege(Privilege.controlserver)
            .BeginSubCommand("scan")
                .WithDescription("Check an online player's inventory without moving items.")
                .WithArgs(api.ChatCommands.Parsers.OnlinePlayer("player"))
                .HandleWith(args => WithOnlinePlayer(args, player =>
                {
                    if (Service?.IsLoaded != true)
                        return TextCommandResult.Error("Inventory migration is unavailable. Check the server log.");
                    var source = new VintageStoryLegacyInventorySource(player);
                    PlayerItemMigrationReport report = new PlayerItemMigrationCoordinator(Service)
                        .DryRun(source).Report;
                    return report.Ready
                        ? TextCommandResult.Success(report.Summary())
                        : TextCommandResult.Error(report.Summary());
                }))
            .EndSubCommand()
            .BeginSubCommand("status")
                .WithDescription("Show an online player's migration or recovery state.")
                .WithArgs(api.ChatCommands.Parsers.OnlinePlayer("player"))
                .HandleWith(args => WithOnlinePlayer(args, player =>
                {
                    PlayerItemMigrationJournalState? journal = Service?.Migration(player.PlayerUID);
                    if (journal == null)
                        return TextCommandResult.Success("No inventory migration has been recorded for this player.");
                    string message = journal.Status switch
                    {
                        PlayerItemMigrationStatus.Prepared => "Migration recovery is waiting. Run the resume command.",
                        PlayerItemMigrationStatus.Committed => "Migration complete. The original inventory can still be restored.",
                        PlayerItemMigrationStatus.RestorePending => "Inventory restore is incomplete. Run the resume command.",
                        PlayerItemMigrationStatus.Restored => "The original inventory has been restored.",
                        _ => "The migration state is unavailable. Check the server log."
                    };
                    return TextCommandResult.Success(message);
                }))
            .EndSubCommand()
            .BeginSubCommand("resume")
                .WithDescription("Finish a prepared migration or interrupted recovery.")
                .WithArgs(api.ChatCommands.Parsers.OnlinePlayer("player"))
                .HandleWith(args => WithOnlinePlayer(args, player =>
                {
                    if (Service?.IsLoaded != true)
                        return TextCommandResult.Error("Inventory recovery is unavailable. Check the server log.");
                    PlayerItemMigrationResult result = new PlayerItemMigrationCoordinator(Service)
                        .Resume(new VintageStoryLegacyInventorySource(player));
                    return result.Success
                        ? TextCommandResult.Success(result.Message)
                        : TextCommandResult.Error(result.Message);
                }))
            .EndSubCommand()
            .BeginSubCommand("restore")
                .WithDescription("Restore an online player's original inventory from the migration snapshot.")
                .WithArgs(
                    api.ChatCommands.Parsers.OnlinePlayer("player"),
                    api.ChatCommands.Parsers.Word("confirmation"))
                .HandleWith(args => WithOnlinePlayer(args, player =>
                {
                    if (!string.Equals((string)args[1], "confirm", StringComparison.OrdinalIgnoreCase))
                    {
                        return TextCommandResult.Error(
                            "This replaces the player's migrated inventory with the original snapshot. Run the command again with confirm.");
                    }
                    if (Service?.IsLoaded != true)
                        return TextCommandResult.Error("Inventory recovery is unavailable. Check the server log.");
                    PlayerItemMigrationResult result = new PlayerItemMigrationCoordinator(Service)
                        .RestorePrior(new VintageStoryLegacyInventorySource(player));
                    return result.Success
                        ? TextCommandResult.Success(result.Message)
                        : TextCommandResult.Error(result.Message);
                }))
            .EndSubCommand();
    }

    private static TextCommandResult WithOnlinePlayer(
        TextCommandCallingArgs args,
        System.Func<IServerPlayer, TextCommandResult> action)
    {
        return args[0] is IServerPlayer player
            ? action(player)
            : TextCommandResult.Error("That player must be online.");
    }
}
