using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using VintageKinematics.Api.Storage;
using VintageKinematics.Blocks;
using VintageKinematics.Storage.Recovery;
using VintageKinematics.Storage.Topology;

namespace VintageKinematics.BlockEntities.Storage
{
    public partial class BEKineticWarehouseTerminal
    {
        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            BlockPos persistedController = ControllerPos?.Copy();
            EnsureIdentity(allowGenerate: false);
            if (api.Side == EnumAppSide.Server && !string.IsNullOrWhiteSpace(WarehouseId))
            {
                api.ModLoader.GetModSystem<StorageTopologyRescanSystem>()?.Register(this);
                RequestRecoveryInitialization(
                    isNewController: false,
                    IsDifferentPosition(persistedController, Pos)
                        ? persistedController
                        : null);
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
                    StoragePlacementDecision placement = WorldStoragePlacementPolicy.Evaluate(
                        Api.World,
                        Pos,
                        StoragePlacementRole.Controller);
                    bool hasAdjacentOrphan = placement.Allowed
                        && !string.IsNullOrWhiteSpace(placement.WarehouseId)
                        && placement.Controller.HasValue;
                    string portableWarehouseId = null;
                    BlockPos portableController = null;
                    bool portableHasMembers = false;
                    bool hasPortableIdentity = !hasAdjacentOrphan
                        && BlockKineticWarehouseMember.TryReadPortableControllerIdentity(
                            byItemStack,
                            out portableWarehouseId,
                            out portableController,
                            out portableHasMembers)
                        && (!portableHasMembers
                            || !BlockKineticWarehouseMember.HasOrphanedWarehouseMembers(
                                Api.World,
                                portableWarehouseId,
                                portableController));

                    // Fresh identity and cleared reused-BE state are one persisted transition.
                    ResetForFreshPlacement();
                    BlockPos previousController = null;
                    if (hasAdjacentOrphan)
                    {
                        previousController = WorldStorageTopologySource.ToBlockPos(
                            placement.Controller.Value);
                        SetLink(placement.WarehouseId, Pos);
                    }
                    else if (hasPortableIdentity)
                    {
                        previousController = portableController;
                        SetLink(portableWarehouseId, Pos);
                    }
                    else
                    {
                        EnsureIdentity(allowGenerate: true);
                    }
                    Api.ModLoader.GetModSystem<StorageTopologyRescanSystem>()?.Register(this);
                    RequestRecoveryInitialization(
                        isNewController: previousController == null,
                        previousController);
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

        private void RequestRecoveryInitialization(
            bool isNewController,
            BlockPos previousController = null)
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
                if (previousController != null)
                {
                    try
                    {
                        recoverySystem.MoveControllerMirrors(
                            WarehouseId,
                            ToControllerLocation(previousController),
                            PhysicalLocation());
                        RelinkMovedWarehouse(previousController);
                    }
                    catch (Exception exception)
                    {
                        Api.World.Logger.Error(
                            "[VintageKinematics] Could not reattach moved warehouse {0} from {1} to {2}: {3}",
                            WarehouseId,
                            previousController,
                            Pos,
                            exception);
                        EnterRecoveryRequired(null);
                        return;
                    }
                }
                InitializeItemRecovery(recoverySystem, isNewController);
            });
        }

        private void RelinkMovedWarehouse(BlockPos previousController)
        {
            var visited = new HashSet<StorageTopologyPosition>();
            var open = new Queue<BlockPos>();
            foreach (BlockFacing face in BlockFacing.ALLFACES)
            {
                open.Enqueue(Pos.AddCopy(face));
            }

            int relinked = 0;
            while (open.Count > 0)
            {
                BlockPos position = open.Dequeue();
                StorageTopologyPosition topologyPosition =
                    WorldStorageTopologySource.FromBlockPos(position);
                if (!visited.Add(topologyPosition)) continue;
                if (Api.World.BlockAccessor.GetBlockEntity(position)
                    is not BEKineticStorageMember member
                    || member.IsController
                    || !member.RelinkController(WarehouseId, previousController, Pos))
                {
                    continue;
                }

                relinked++;
                if (relinked > StorageTopologyLimits.DefaultMaxNonControllerMembers)
                {
                    throw new InvalidOperationException(
                        "Moved warehouse exceeds the supported member limit.");
                }
                foreach (BlockFacing face in BlockFacing.ALLFACES)
                {
                    open.Enqueue(position.AddCopy(face));
                }
            }
        }

        private static StorageControllerLocation ToControllerLocation(BlockPos position)
        {
            return new StorageControllerLocation(
                position.X,
                position.InternalY,
                position.Z,
                position.dimension);
        }

        internal static bool IsDifferentPosition(BlockPos first, BlockPos second)
        {
            return first != null && second != null && !first.Equals(second);
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
