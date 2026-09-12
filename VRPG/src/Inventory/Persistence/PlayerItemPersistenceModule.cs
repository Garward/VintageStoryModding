using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
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
    private bool bootRefused;
    private bool protectionInstallFailed;
    private readonly string? bootstrapError;
    private readonly PlayerItemCustodyPolicy custody = new();
    private readonly HashSet<string> unresolvedCursors = new(StringComparer.OrdinalIgnoreCase);

    public string Code => "player-items";
    public PlayerItemArchiveService? Service { get; private set; }
    public bool IsReady => !bootRefused && Service?.IsLoaded == true && checkpointHealthy;
    public PlayerItemBridgeCoordinator? Bridges { get; private set; }
    public bool OperationsRequested { get; }
    public AdventureProfile Adventure { get; }
    public PlayerItemBridgeCoverage Coverage { get; } = new();
    public PlayerItemPersistenceModule(bool operationsRequested = true, WorldScopedPlayerItemArchiveStorage? storage = null)
    {
        OperationsRequested = operationsRequested;
        this.storage = storage;
        Adventure = new(operationsRequested, () => Service,
            () => IsReady && NativeInventoryPacketGuardPatch.IsReady && NativeInventoryTickGuardPatch.IsReady
                && NativeInventoryMutationGuardPatch.IsReady && EngineItemFlowPatch.IsInstalled
                && EngineItemFlowPatch.IsFreshIdentityInstalled && EngineItemFlowPatch.AdventureSuppression?.Enabled == true
                && unresolvedCursors.Count == 0,
            Custody);
        Coverage.Register("pickup:persisted", () => IsReady && EngineItemFlowPatch.IsInstalled);
        Coverage.Register("pickup:fresh", () => IsReady && EngineItemFlowPatch.IsFreshIdentityInstalled);
        Coverage.Register("native:packets", () => IsReady && NativeInventoryPacketGuardPatch.IsReady);
        Coverage.Register("native:packet3-block-place-break", () => IsReady && NativeInventoryPacketGuardPatch.IsReady);
        Coverage.Register("native:packet22-block-entity", () => IsReady && NativeInventoryPacketGuardPatch.IsReady);
        Coverage.Register("native:packet31-entity", () => IsReady && NativeInventoryPacketGuardPatch.IsReady);
        Coverage.Register("native:OnUsingTick", () => IsReady && NativeInventoryTickGuardPatch.IsReady);
        Coverage.Register("native:UpdateTransitionStates", () => IsReady && NativeInventoryTickGuardPatch.IsReady);
        foreach (string boundary in new[] { "TryGiveItemstack", "TryTransferAway", "DropItem", "OnDeath",
            "player-DropAll", "deferred-player-death", "manager-DiscardAll", "player-Clear", "player-DiscardAll",
            "WipePlayerInventory", "clearinv-command", "queued-player-clear" })
            Coverage.Register("native:" + boundary, () => IsReady && NativeInventoryMutationGuardPatch.IsReady);
        Coverage.Register("native:cursor-securing", () => IsReady && NativeInventoryPacketGuardPatch.IsReady && unresolvedCursors.Count == 0);
    }
    internal PlayerItemPersistenceModule(bool operationsRequested, string bootstrapError) : this(operationsRequested)
    {
        this.bootstrapError = bootstrapError;
    }
    public bool CanActivateNativeProfile => Adventure.IsReady;

    public void StartServerSide(ICoreServerAPI api)
    {
        custody.Reset();
        unresolvedCursors.Clear();
        checkpointHealthy = true;
        bootRefused = false;
        protectionInstallFailed = false;
        this.api = api;
        api.Event.SaveGameLoaded += OnSaveGameLoaded;
        if (bootstrapError != null) return;
        storage ??= new WorldScopedPlayerItemArchiveStorage(api.GetOrCreateDataPath("VRPG/player-items"),
            api.WorldManager.SaveGame.SavegameIdentifier, new VintageStoryPlayerItemArchiveStorage(api.WorldManager.SaveGame));
        Service = new PlayerItemArchiveService(new PlayerItemArchiveRepository(storage));
        api.Event.GameWorldSave += OnGameWorldSave;
        api.Event.OnEntitySpawn += OnEntitySpawn;
        api.Event.OnEntityLoaded += OnEntityLoaded;
        api.Event.PlayerDisconnect += OnPlayerDisconnect;
        api.Event.PlayerDeath += OnPlayerDeath;
        api.Event.BeforeActiveSlotChanged += OnBeforeActiveSlotChanged;
        RegisterCommands(api);
    }

    public void InstallProtection(Harmony harmony)
    {
        if (api == null) throw new InvalidOperationException("Start custody persistence before installing protection.");
        if (bootstrapError != null) return;
        try
        {
            EngineItemFlowPatch.BeforePublicationObserver = EngineItemPickupSource.EstablishBeforePublication;
            EngineItemFlowPatch.AdventureSuppression = new(Adventure.Enabled,
                message => api.Logger.Warning("[VRPG/Adventure] {0}", message));
            NativeInventoryPacketGuardPatch.CustodyResolver = Custody;
            NativeInventoryPacketGuardPatch.CursorRecoveryObserver = RequireCursorRecovery;
            NativeInventoryPacketGuardPatch.Patch(harmony, api.Logger);
            NativeInventoryTickGuardPatch.CustodyResolver = playerUid => ResolveCustody(playerUid, checkCanonicalReadiness: false);
            NativeInventoryTickGuardPatch.Patch(harmony, api.Logger);
            NativeInventoryMutationGuardPatch.CustodyResolver = playerUid => ResolveCustody(playerUid, checkCanonicalReadiness: false);
            NativeInventoryMutationGuardPatch.Patch(harmony, api.Logger);
            EngineItemFlowPatch.CustodyResolver = Custody;
            EngineItemFlowPatch.CoordinatorResolver = () => Bridges;
            EngineItemFlowPatch.Patch(harmony, api.Logger);
        }
        catch (Exception error)
        {
            // Modloader exceptions alone do not stop the server. Keep the loaded-world
            // refusal handler registered even when only part of the patch set installed.
            protectionInstallFailed = true;
            api.Logger.Error("[VRPG/Inventory] Custody protection installation failed: {0}", error.Message);
        }
    }

    public void Dispose()
    {
        NativeInventoryPacketGuardPatch.Reset();
        NativeInventoryTickGuardPatch.Reset();
        NativeInventoryMutationGuardPatch.Reset();
        EngineItemFlowPatch.BeforePublicationObserver = null;
        EngineItemFlowPatch.AdventureSuppression = null;
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
        custody.Reset();
        unresolvedCursors.Clear();
        checkpointHealthy = true;
        bootRefused = false;
        protectionInstallFailed = false;
    }

    public PlayerItemCustodySnapshot Custody(string playerUid) => ResolveCustody(playerUid, checkCanonicalReadiness: true);

    private PlayerItemCustodySnapshot ResolveCustody(string playerUid, bool checkCanonicalReadiness)
    {
        if (string.IsNullOrWhiteSpace(playerUid)) return PlayerItemCustodySnapshot.Unresolved;
        if (unresolvedCursors.Contains(playerUid.Trim())) return PlayerItemCustodySnapshot.Unresolved;
        bool loaded = Service?.IsLoaded == true;
        var confirmation = Service?.MigrationConfirmation(playerUid);
        var phase = confirmation?.Phase;
        bool reconciled = false;
        // Native effect refusal needs only the owner. Use the same confirmed phase/uncertainty
        // authority without copying receipts or evaluating canonical operation readiness.
        if (checkCanonicalReadiness && loaded && phase == PlayerItemMigrationStatus.Committed && IsReady && Service!.IsCrashDurable && Bridges != null)
        {
            try
            {
                // A custody query is read-only. Reconciliation remains at explicit lifecycle
                // boundaries; refusing an arbitrary packet must not replay an external adapter.
                reconciled = Service.BridgeSnapshot()
                    .Where(r => string.Equals(r.Request.PlayerUid, playerUid.Trim(), StringComparison.OrdinalIgnoreCase))
                    .All(r => r.Stage is
                    PlayerItemBridgeStage.Completed or PlayerItemBridgeStage.RolledBack or PlayerItemBridgeStage.Refused);
            }
            catch { /* Health loss cannot reopen competing Vanilla custody. */ }
        }
        return custody.Observe(playerUid, loaded && confirmation?.IsConfirmed == true, phase, IsReady,
            OperationsRequested && reconciled && NativeInventoryPacketGuardPatch.IsReady
                && NativeInventoryTickGuardPatch.IsReady && NativeInventoryMutationGuardPatch.IsReady && EngineItemFlowPatch.IsInstalled
                && Adventure.IsReady);
    }

    public void RequireCursorRecovery(string playerUid)
    {
        if (!string.IsNullOrWhiteSpace(playerUid)) unresolvedCursors.Add(playerUid.Trim());
        api?.Logger.Error("[VRPG/Inventory] Native cursor retained for {0}; activation requires explicit recovery. Retained memory does not establish disk durability.", playerUid);
    }

    private void OnSaveGameLoaded()
    {
        if (api == null || bootRefused) return;
        if (bootstrapError != null)
        {
            RefuseBoot(bootstrapError);
            return;
        }
        if (Service == null)
        {
            RefuseBoot("Custody persistence did not finish initialization.");
            return;
        }
        if (protectionInstallFailed || !NativeInventoryPacketGuardPatch.IsReady || !NativeInventoryTickGuardPatch.IsReady
            || !NativeInventoryMutationGuardPatch.IsReady || !EngineItemFlowPatch.IsInstalled
            || (Adventure.Enabled && (!EngineItemFlowPatch.IsFreshIdentityInstalled
                || EngineItemFlowPatch.AdventureSuppression?.Enabled != true)))
        {
            RefuseBoot("Required native item protection is unavailable.");
            return;
        }
        PlayerItemArchiveLoadResult result = Service.Load();
        if (!result.Success)
        {
            api.Logger.Error(
                "[VRPG/Inventory] Player-item persistence is unavailable: {0}",
                result.Error);
            RefuseBoot("The custody archive could not be validated.");
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
        if (api == null || Service?.IsLoaded != true || bootRefused) return;
        PlayerItemArchiveSaveResult result = Service.SaveDirty();
        if (!result.Success)
        {
            checkpointHealthy = false;
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

    private void RefuseBoot(string reason)
    {
        bootRefused = true;
        checkpointHealthy = false;
        api!.Logger.Fatal("[VRPG/Inventory] Server startup refused: {0} Preserve the custody archive for explicit recovery.", reason);
        // SaveGameLoaded runs after the database is open, before the native post-
        // LoadGamePre exit gate and gameplay admission. A readiness flag is insufficient.
        // The engine's soft shutdown invokes GameWorldSave; bootRefused keeps it read-only here.
        api.Server.ShutDown();
    }

    private void OnEntitySpawn(Entity entity)
    {
        if (entity.GetType() == typeof(EntityItem)) EngineItemPickupSource.Observe((EntityItem)entity, loaded: false);
    }

    private void OnEntityLoaded(Entity entity)
    {
        // Preserve legacy payloads. The Adventure pickup guard leaves these entities inert
        // to collection, without issuing delivery receipts or deleting world items.
        if (Adventure.Enabled && entity is EntityItem) return;
        if (entity.GetType() != typeof(EntityItem)) return;
        var item = (EntityItem)entity;
        EngineItemPickupSource.Observe(item, loaded: true);
        Bridges?.SuppressClaimedPickup(new EngineItemPickupSource(item));
    }

    private void OnPlayerDisconnect(IServerPlayer player)
    {
        try { Bridges?.Reconcile(player.PlayerUID); }
        finally { custody.Forget(player.PlayerUID); }
    }
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
