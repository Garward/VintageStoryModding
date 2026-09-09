using System;
using System.Linq;
using VRPG.Core;
using VRPG.Inventory.Migration;
using VRPG.Inventory.Bridges;
using VRPG.Patches;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace VRPG.Inventory.Persistence;

public sealed class PlayerItemPersistenceModule : IVrpgModule
{
    private ICoreServerAPI? api;
    private WorldScopedPlayerItemArchiveStorage? storage;
    private bool checkpointHealthy = true;

    public string Code => "player-items";
    public PlayerItemArchiveService? Service { get; private set; }
    public bool IsReady => Service?.IsLoaded == true && checkpointHealthy;
    public PlayerItemBridgeCoordinator? Bridges { get; private set; }
    public PlayerItemBridgeCoverage Coverage { get; } = new();
    public PlayerItemPersistenceModule()
    {
        Coverage.Register("pickup:persisted", () => IsReady && EngineItemFlowPatch.IsInstalled);
        Coverage.Register("pickup:fresh", () => IsReady && EngineItemFlowPatch.IsFreshIdentityInstalled);
    }
    public bool CanActivateNativeProfile => IsReady && EngineItemFlowPatch.IsInstalled && Coverage.CanActivate(new[]
    {
        "pickup:persisted", "pickup:fresh", "grant:rewards", "grant:admin", "slots:hotbar", "slots:offhand",
        "slots:cursor", "slots:crafting", "slots:equipment", "slots:backpack", "trade", "containers",
        "producer:sign", "producer:labeled-chest", "producer:ground-storage", "death", "held", "use", "drop", "home-placement"
    });

    public void StartServerSide(ICoreServerAPI api)
    {
        this.api = api;
        EngineItemFlowPatch.BeforePublicationObserver = EngineItemPickupSource.EstablishBeforePublication;
        storage = new WorldScopedPlayerItemArchiveStorage(api.GetOrCreateDataPath("VRPG/player-items"),
            api.WorldManager.SaveGame.SavegameIdentifier, new VintageStoryPlayerItemArchiveStorage(api.WorldManager.SaveGame));
        Service = new PlayerItemArchiveService(new PlayerItemArchiveRepository(storage));
        api.Event.SaveGameLoaded += OnSaveGameLoaded;
        api.Event.GameWorldSave += OnGameWorldSave;
        api.Event.OnEntitySpawn += OnEntitySpawn;
        api.Event.OnEntityLoaded += OnEntityLoaded;
        api.Event.PlayerDisconnect += OnPlayerDisconnect;
        api.Event.PlayerDeath += OnPlayerDeath;
        api.Event.BeforeActiveSlotChanged += OnBeforeActiveSlotChanged;
        RegisterCommands(api);
    }

    public void Dispose()
    {
        EngineItemFlowPatch.BeforePublicationObserver = null;
        if (api != null)
        {
            api.Event.SaveGameLoaded -= OnSaveGameLoaded;
            api.Event.GameWorldSave -= OnGameWorldSave;
            api.Event.OnEntitySpawn -= OnEntitySpawn;
            api.Event.OnEntityLoaded -= OnEntityLoaded;
            api.Event.PlayerDisconnect -= OnPlayerDisconnect;
            api.Event.PlayerDeath -= OnPlayerDeath;
            api.Event.BeforeActiveSlotChanged -= OnBeforeActiveSlotChanged;
        }
        api = null;
        Service = null;
        Bridges = null;
        storage = null;
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
        Bridges = new PlayerItemBridgeCoordinator(Service, new WorldCollectibleResolver(api.World));
        Bridges.Reconcile();

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
            return;
        }
        try { storage?.AdvanceWorldCheckpoint(); }
        catch (Exception error)
        {
            checkpointHealthy = false;
            api.Logger.Error("[VRPG/Inventory] World checkpoint is unavailable; custody activation is blocked: {0}", error.Message);
        }
    }

    private void OnEntitySpawn(Entity entity)
    {
        if (entity.GetType() == typeof(EntityItem)) EngineItemPickupSource.Observe((EntityItem)entity, loaded: false);
    }

    private void OnEntityLoaded(Entity entity)
    {
        if (entity.GetType() != typeof(EntityItem)) return;
        var item = (EntityItem)entity;
        EngineItemPickupSource.Observe(item, loaded: true);
        Bridges?.SuppressClaimedPickup(new EngineItemPickupSource(item));
    }

    private void OnPlayerDisconnect(IServerPlayer player) => Bridges?.Reconcile(player.PlayerUID);
    private void OnPlayerDeath(IServerPlayer player, DamageSource damage) => Bridges?.Reconcile(player.PlayerUID);
    private EnumHandling OnBeforeActiveSlotChanged(IServerPlayer player, ActiveSlotChangeEventArgs args) =>
        Bridges?.Reconcile(player.PlayerUID).Any(r => r.Stage == PlayerItemBridgeStage.ReconcileRequired) == true
            ? EnumHandling.PreventDefault : EnumHandling.PassThrough;

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
