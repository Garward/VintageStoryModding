using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace VintageKinematics.Storage.Recovery
{
    /// <summary>
    /// Server lifecycle adapter for the storage recovery registry.
    /// </summary>
    public sealed partial class KineticStorageRecoverySystem : ModSystem
    {
        private readonly StorageRecoveryPersistence recoveryPersistence =
            new StorageRecoveryPersistence(StorageRecoveryKeyspace.Recovery);
        private readonly StorageRecoveryPersistence controllerPersistence =
            new StorageRecoveryPersistence(StorageRecoveryKeyspace.Controller);
        private ICoreServerAPI serverApi;
        private IStorageRecoveryStore store;
        private bool canPersist = true;
        private readonly List<Action> registryReadyCallbacks = new List<Action>();

        public StorageRecoveryRegistry Registry { get; private set; } = new StorageRecoveryRegistry();
        public StorageRecoveryRegistry ControllerRegistry { get; private set; } =
            new StorageRecoveryRegistry();
        public IReadOnlyList<StorageRecoveryLoadIssue> LoadIssues { get; private set; } =
            Array.Empty<StorageRecoveryLoadIssue>();
        public bool CanPersist => canPersist;
        public bool IsLoaded { get; private set; }

        public void RunWhenRegistryReady(Action callback)
        {
            if (callback == null) throw new ArgumentNullException(nameof(callback));
            if (IsLoaded)
            {
                callback();
                return;
            }
            registryReadyCallbacks.Add(callback);
        }

        public bool ApplyExplicitRecovery(StorageRecoveryRecord record)
        {
            if (!IsLoaded || record == null) return false;
            Registry.ValidateExplicitRecovery(record);
            ControllerRegistry.ValidateExplicitRecovery(record);
            Registry.ReplaceAfterExplicitRecovery(record);
            ControllerRegistry.ReplaceAfterExplicitRecovery(record);
            LoadIssues = LoadIssues
                .Where(issue => !string.Equals(
                    issue.WarehouseId,
                    record.WarehouseId,
                    StringComparison.Ordinal))
                .ToArray();
            canPersist = LoadIssues.Count == 0;
            return true;
        }

        public void UpsertMirrors(StorageRecoveryRecord record)
        {
            Registry.ValidateUpsert(record);
            ControllerRegistry.ValidateUpsert(record);
            Registry.Upsert(record);
            ControllerRegistry.Upsert(record);
        }

        public void TombstoneMirrors(string warehouseId, long revision)
        {
            if (!Registry.TryGet(warehouseId, out StorageRecoveryRecord recovery)
                || !ControllerRegistry.TryGet(warehouseId, out StorageRecoveryRecord controller)
                || !recovery.IsEquivalentTo(controller))
            {
                throw new InvalidOperationException(
                    "Both mirrors must agree before a controller can be tombstoned.");
            }
            StorageRecoveryRecord tombstone = StorageRecoveryRecord.Create(
                recovery.WarehouseId,
                recovery.Controller,
                revision,
                Array.Empty<byte>(),
                isTombstone: true);
            Registry.ValidateUpsert(tombstone);
            ControllerRegistry.ValidateUpsert(tombstone);
            Registry.Upsert(tombstone);
            ControllerRegistry.Upsert(tombstone);
        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            base.StartServerSide(api);
            serverApi = api;
            store = new SaveGameStorageRecoveryStore(api.WorldManager.SaveGame);
            api.Event.SaveGameLoaded += OnSaveGameLoaded;
            api.Event.GameWorldSave += OnGameWorldSave;
            StartWorldAudit(api);
        }

        public override void Dispose()
        {
            if (serverApi != null)
            {
                serverApi.Event.SaveGameLoaded -= OnSaveGameLoaded;
                serverApi.Event.GameWorldSave -= OnGameWorldSave;
                StopWorldAudit(serverApi);
            }
            base.Dispose();
        }

        private void OnSaveGameLoaded()
        {
            try
            {
                StorageRecoveryLoadResult recovery = recoveryPersistence.Load(store);
                StorageRecoveryLoadResult controller = controllerPersistence.Load(store);
                Registry = recovery.Registry;
                ControllerRegistry = controller.Registry;
                LoadIssues = recovery.Issues.Concat(controller.Issues).ToArray();
                canPersist = recovery.CanPersist && controller.CanPersist;
                IsLoaded = true;
                LogLoadIssues();
                RunRegistryReadyCallbacks();
            }
            catch (Exception exception)
            {
                canPersist = false;
                IsLoaded = true;
                serverApi.Logger.Error(
                    "[VintageKinematics] Storage recovery registry load failed; persistence is disabled: {0}",
                    exception);
                RunRegistryReadyCallbacks();
            }
        }

        private void OnGameWorldSave()
        {
            if (!Registry.IsDirty
                && !ControllerRegistry.IsDirty
                && !recoveryPersistence.HasPendingRepair
                && !controllerPersistence.HasPendingRepair)
            {
                return;
            }
            if (!canPersist)
            {
                serverApi.Logger.Error(
                    "[VintageKinematics] Storage recovery registry is dirty but cannot be saved "
                    + "until its load issues are resolved.");
                return;
            }

            try
            {
                controllerPersistence.Save(store, ControllerRegistry);
                recoveryPersistence.Save(store, Registry);
            }
            catch (Exception exception)
            {
                serverApi.Logger.Error(
                    "[VintageKinematics] Storage recovery registry save failed; changes remain dirty: {0}",
                    exception);
            }
        }

        private void LogLoadIssues()
        {
            foreach (StorageRecoveryLoadIssue issue in LoadIssues)
            {
                serverApi.Logger.Error(
                    "[VintageKinematics] Storage recovery load issue {0} for warehouse {1}; "
                    + "registry persistence is disabled pending recovery.",
                    issue.Kind,
                    issue.WarehouseId ?? "<index>");
            }
        }

        private void RunRegistryReadyCallbacks()
        {
            Action[] callbacks = registryReadyCallbacks.ToArray();
            registryReadyCallbacks.Clear();
            foreach (Action callback in callbacks)
            {
                try
                {
                    callback();
                }
                catch (Exception exception)
                {
                    serverApi.Logger.Error(
                        "[VintageKinematics] Warehouse terminal recovery initialization failed: {0}",
                        exception);
                }
            }
        }
    }
}
