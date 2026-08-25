using System;
using Vintagestory.API.Common;
using VintageKinematics.Api.Storage;
using VintageKinematics.Storage.Recovery;
using VintageKinematics.Storage.Topology;

namespace VintageKinematics.BlockEntities.Storage
{
    public partial class BEKineticWarehouseTerminal
    {
        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            EnsureIdentity(allowGenerate: false);
            if (api.Side == EnumAppSide.Server && !string.IsNullOrWhiteSpace(WarehouseId))
            {
                api.ModLoader.GetModSystem<StorageTopologyRescanSystem>()?.Register(this);
                RequestRecoveryInitialization(isNewController: false);
                RequestStructureRebuild(StorageChangeReason.Loaded);
            }
        }

        public override void OnBlockUnloaded()
        {
            DisposeTerminalDialog();
            Api?.ModLoader.GetModSystem<StorageTopologyRescanSystem>()?.Unregister(this);
            base.OnBlockUnloaded();
        }

        public override void OnBlockRemoved()
        {
            DisposeTerminalDialog();
            RecordSafeControllerRemoval();
            Api?.ModLoader.GetModSystem<StorageTopologyRescanSystem>()?.Unregister(this);
            base.OnBlockRemoved();
        }

        public override void OnBlockPlaced(ItemStack byItemStack = null)
        {
            base.OnBlockPlaced(byItemStack);
            if (Api?.Side == EnumAppSide.Server)
            {
                lock (persistenceSync)
                {
                    // Fresh identity and cleared reused-BE state are one persisted transition.
                    ResetForFreshPlacement();
                    EnsureIdentity(allowGenerate: true);
                    Api.ModLoader.GetModSystem<StorageTopologyRescanSystem>()?.Register(this);
                    RequestRecoveryInitialization(isNewController: true);
                    RequestStructureRebuild(StorageChangeReason.StructureChanged);
                }
            }
        }

        internal void ResetForFreshPlacement()
        {
            lock (persistenceSync)
            {
                ResetForFreshPlacementSynchronized();
            }
        }

        private void ResetForFreshPlacementSynchronized()
        {
            WarehouseId = null;
            ControllerPos = null;
            StructureState = StorageState.StructureUnknown;
            VerifiedItemCapacity = 0;
            VerifiedTypeCapacity = 0;
            SyncedStoredItems = 0;
            SyncedEntryCount = 0;
            LastStructureSnapshot = null;
            knownMembers.Clear();
            itemIndex = null;
            unresolvedEntries.Clear();
            activeRecoveryRecord = null;
            persistedItemCopy = StorageSnapshotCopy.Missing();
            persistedItemHeader = null;
            persistedItemHeaderBytes = Array.Empty<byte>();
            LastReconciliation = null;
            recoveryInitializationRequested = false;
            rebuildScheduled = false;
            structureDirty = false;
            rebuildInProgress = false;
            poweredDrivePorts.Clear();
            syncedPowerAvailable = false;
        }

        private void RequestRecoveryInitialization(bool isNewController)
        {
            if (recoveryInitializationRequested || Api?.Side != EnumAppSide.Server) return;
            recoveryInitializationRequested = true;
            string requestedWarehouseId = WarehouseId;
            KineticStorageRecoverySystem recoverySystem =
                Api.ModLoader.GetModSystem<KineticStorageRecoverySystem>();
            if (recoverySystem == null)
            {
                EnterRecoveryRequired(null);
                return;
            }

            recoverySystem.RunWhenRegistryReady(() =>
            {
                if (Api?.Side != EnumAppSide.Server
                    || Api.World.BlockAccessor.GetBlockEntity(Pos) != this
                    || WarehouseId != requestedWarehouseId)
                {
                    return;
                }
                InitializeItemRecovery(recoverySystem, isNewController);
            });
        }

        public void RequestStructureRebuild(StorageChangeReason reason)
        {
            if (Api?.Side != EnumAppSide.Server || string.IsNullOrWhiteSpace(WarehouseId)) return;
            structureDirty = true;
            if (rebuildScheduled) return;

            rebuildScheduled = true;
            RegisterDelayedCallback(_ => RunScheduledRebuild(reason), 50);
        }

        private void RunScheduledRebuild(StorageChangeReason reason)
        {
            rebuildScheduled = false;
            if (!structureDirty || Api?.Side != EnumAppSide.Server) return;
            structureDirty = false;
            RebuildStructureNow(reason);
        }

        private void EnsureIdentity(bool allowGenerate)
        {
            lock (persistenceSync)
            {
                EnsureIdentitySynchronized(allowGenerate);
            }
        }

        private void EnsureIdentitySynchronized(bool allowGenerate)
        {
            bool generated = false;
            if (allowGenerate
                && string.IsNullOrWhiteSpace(WarehouseId)
                && Api?.Side == EnumAppSide.Server)
            {
                WarehouseId = Guid.NewGuid().ToString("D");
                generated = true;
            }
            ControllerPos = Pos?.Copy();
            if (generated) MarkDirty();
        }
    }
}
