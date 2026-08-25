using Vintagestory.API.MathTools;
using VintageKinematics.Api.Storage;
using VintageKinematics.Storage.Index;
using VintageKinematics.Storage.Persistence;
using VintageKinematics.Storage.Recovery;

namespace VintageKinematics.BlockEntities.Storage
{
    public partial class BEKineticWarehouseTerminal
    {
        private void InitializeItemRecovery(
            KineticStorageRecoverySystem recoverySystem,
            bool isNewController)
        {
            lock (persistenceSync)
            {
                InitializeItemRecoverySynchronized(recoverySystem, isNewController);
            }
        }

        private void InitializeItemRecoverySynchronized(
            KineticStorageRecoverySystem recoverySystem,
            bool isNewController)
        {
            if (!recoverySystem.CanPersist || !HasPhysicalIdentity())
            {
                EnterRecoveryRequired(null);
                return;
            }
            recoverySystem.ObserveController(WarehouseId, PhysicalLocation());

            if (isNewController)
            {
                InitializeNewItemIndex();
                return;
            }

            StorageSnapshotCopy blockEntityCopy = BuildControllerOwnedCopy(
                recoverySystem.ControllerRegistry);
            StorageSnapshotCopy registryCopy = recoverySystem.Registry.TryGet(
                WarehouseId,
                out StorageRecoveryRecord registryRecord)
                    ? StorageSnapshotCopy.FromRecord(registryRecord)
                    : StorageSnapshotCopy.Missing();
            KineticStoragePersistence persistence = new KineticStoragePersistence(Api.World);
            StorageControllerRecoveryDecision decision = StorageControllerRecoveryLoader.Prepare(
                blockEntityCopy,
                registryCopy,
                persistence,
                CurrentIndexLimits());
            LastReconciliation = decision.Reconciliation;
            if (!decision.CanOpen)
            {
                EnterRecoveryRequired(decision.Reconciliation);
                return;
            }
            if (decision.Reconciliation.Outcome
                == StorageReconciliationOutcome.IdenticalMirrorsWithStaleHeader)
            {
                Api.World.Logger.Warning(
                    "[VintageKinematics] Repaired stale warehouse terminal header for {0} at {1}; both full mirrors agreed at revision {2}.",
                    WarehouseId,
                    Pos,
                    decision.Record.Revision);
            }

            itemIndex = decision.LoadedSnapshot.Index;
            unresolvedEntries.Clear();
            unresolvedEntries.AddRange(decision.LoadedSnapshot.UnresolvedEntries);
            activeRecoveryRecord = decision.Record;
            persistedItemCopy = StorageSnapshotCopy.FromRecord(activeRecoveryRecord);
            persistedItemHeader = new StorageRecoveryIndexEntry(activeRecoveryRecord);
            persistedItemHeaderBytes = System.Array.Empty<byte>();
            if (StructureState != StorageState.ManualLocked)
            {
                StructureState = StorageState.StructureUnknown;
            }
            itemIndex.UpdateLimits(CurrentIndexLimits());
            // Rewrites a stale compact BE header after both full mirrors prove the same
            // valid state, and is harmless for an ordinary identical load.
            MarkDirty();
            RequestStructureRebuild(StorageChangeReason.Recovery);
        }

        private void InitializeNewItemIndex()
        {
            KineticStoragePersistence persistence = new KineticStoragePersistence(Api.World);
            itemIndex = new KineticStorageIndex(Api.World, CurrentIndexLimits());
            byte[] snapshot = persistence.Encode(persistence.Capture(itemIndex));
            activeRecoveryRecord = StorageRecoveryRecord.Create(
                WarehouseId,
                PhysicalLocation(),
                1,
                snapshot);
            KineticStorageRecoverySystem recoverySystem =
                Api.ModLoader.GetModSystem<KineticStorageRecoverySystem>();
            recoverySystem.UpsertMirrors(activeRecoveryRecord);
            persistedItemCopy = StorageSnapshotCopy.FromRecord(activeRecoveryRecord);
            persistedItemHeader = new StorageRecoveryIndexEntry(activeRecoveryRecord);
            persistedItemHeaderBytes = System.Array.Empty<byte>();
            LastReconciliation = StorageSnapshotReconciler.Reconcile(
                persistedItemCopy,
                StorageSnapshotCopy.FromRecord(activeRecoveryRecord));
            MarkDirty();
        }

        private StorageSnapshotCopy BuildControllerOwnedCopy(StorageRecoveryRegistry controllerRegistry)
        {
            if (persistedItemCopy.State != StorageSnapshotCopyState.Missing)
            {
                return ValidatePhysicalIdentity(persistedItemCopy);
            }
            if (persistedItemHeader == null)
            {
                return controllerRegistry.TryGet(WarehouseId, out StorageRecoveryRecord unverified)
                    ? StorageSnapshotCopy.Invalid(System.Array.Empty<byte>(), unverified)
                    : StorageSnapshotCopy.Missing();
            }
            if (!controllerRegistry.TryGet(WarehouseId, out StorageRecoveryRecord record)
                || !persistedItemHeader.Matches(record))
            {
                return ValidatePhysicalIdentity(
                    StorageSnapshotCopy.Invalid(
                        persistedItemHeaderBytes,
                        record,
                        persistedItemHeader));
            }
            return ValidatePhysicalIdentity(StorageSnapshotCopy.FromRecord(
                record,
                persistedItemHeaderBytes));
        }

        private StorageSnapshotCopy ValidatePhysicalIdentity(StorageSnapshotCopy copy)
        {
            if (copy?.Record == null) return copy ?? StorageSnapshotCopy.Missing();
            return copy.Record.WarehouseId == WarehouseId
                && copy.Record.Controller == PhysicalLocation()
                    ? copy
                    : StorageSnapshotCopy.Invalid(copy.RawBytes, header: copy.Header);
        }

        private bool HasPhysicalIdentity()
        {
            return !string.IsNullOrWhiteSpace(WarehouseId) && Pos != null;
        }

        private StorageControllerLocation PhysicalLocation()
        {
            BlockPos position = Pos;
            return new StorageControllerLocation(
                position.X,
                position.InternalY,
                position.Z,
                position.dimension);
        }

        private void EnterRecoveryRequired(StorageReconciliationResult reconciliation)
        {
            lock (persistenceSync)
            {
                EnterRecoveryRequiredSynchronized(reconciliation);
            }
        }

        private void EnterRecoveryRequiredSynchronized(
            StorageReconciliationResult reconciliation)
        {
            LastReconciliation = reconciliation;
            itemIndex = null;
            activeRecoveryRecord = null;
            StructureState = StorageState.RecoveryRequired;
            MarkDirty();
        }
    }
}
