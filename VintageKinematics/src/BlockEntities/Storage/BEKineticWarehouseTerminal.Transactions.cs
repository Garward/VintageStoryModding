using System;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using VintageKinematics.Api.Storage;
using VintageKinematics.Storage.Persistence;
using VintageKinematics.Storage.Recovery;

namespace VintageKinematics.BlockEntities.Storage
{
    /// <summary>Server-thread item mutations and immediate recovery-mirror commits.</summary>
    public partial class BEKineticWarehouseTerminal
    {
        private StorageTransferResult DepositHeldStack(IServerPlayer player)
        {
            ItemSlot slot = ResolveDepositSlot(player);
            return DepositSlot(player, slot);
        }

        private StorageTransferResult DepositInventorySlot(
            IServerPlayer player,
            string inventoryId,
            int slotId)
        {
            IInventory inventory = player?.InventoryManager?.GetInventory(inventoryId);
            if (inventory == null
                || slotId < 0
                || slotId >= inventory.Count
                || !player.InventoryManager.OpenedInventories.Contains(inventory))
            {
                return StorageTransferResult.Fail(StorageTransferStatus.Locked);
            }

            ItemSlot slot = inventory[slotId];
            if (slot is ItemSlotCreative || slot?.CanTake() != true)
            {
                return StorageTransferResult.Fail(StorageTransferStatus.Locked);
            }
            return DepositSlot(player, slot);
        }

        private StorageTransferResult DepositSlot(IServerPlayer player, ItemSlot slot)
        {
            lock (persistenceSync)
            {
                return DepositSlotSynchronized(player, slot);
            }
        }

        private StorageTransferResult DepositSlotSynchronized(IServerPlayer player, ItemSlot slot)
        {
            StorageTransferResult readiness = CheckDepositReadiness();
            if (readiness.Status != StorageTransferStatus.Ok) return readiness;

            if (slot?.Itemstack == null || slot.Empty)
            {
                return StorageTransferResult.Fail(StorageTransferStatus.EmptyInput);
            }

            ItemStack original = slot.Itemstack.Clone();
            byte[] before = CaptureItemSnapshot();
            StorageTransferResult result = itemIndex.TryInsert(
                Api.World,
                original,
                out _,
                original.StackSize);
            if (!result.Success) return result;

            try
            {
                slot.TakeOut(result.Moved);
                slot.MarkDirty();
                CommitItemSnapshot();
                Api.World.Logger.Audit(
                    "{0} deposited {1}x {2} into kinetic warehouse {3} at {4}.",
                    player.PlayerName,
                    result.Moved,
                    original.Collectible.Code,
                    WarehouseId,
                    Pos);
                return result;
            }
            catch (Exception exception)
            {
                slot.Itemstack = original;
                slot.MarkDirty();
                RestoreItemSnapshot(before);
                Api.World.Logger.Error(
                    "[VintageKinematics] Storage deposit rolled back for {0} at {1}: {2}",
                    WarehouseId,
                    Pos,
                    exception);
                return StorageTransferResult.Fail(StorageTransferStatus.Busy);
            }
        }

        private static ItemSlot ResolveDepositSlot(IServerPlayer player)
        {
            ItemSlot carried = player?.InventoryManager?.MouseItemSlot;
            if (carried?.Empty == false && carried.Itemstack != null) return carried;
            return player?.InventoryManager?.ActiveHotbarSlot;
        }

        private StorageTransferResult WithdrawOneToCursor(IServerPlayer player, long entryId)
        {
            lock (persistenceSync)
            {
                return WithdrawOneToCursorSynchronized(player, entryId);
            }
        }

        private StorageTransferResult WithdrawOneToCursorSynchronized(
            IServerPlayer player,
            long entryId)
        {
            StorageTransferResult readiness = CheckWithdrawalReadiness();
            if (readiness.Status != StorageTransferStatus.Ok) return readiness;

            ItemSlot cursor = player?.InventoryManager?.MouseItemSlot;
            if (cursor == null) return StorageTransferResult.Fail(StorageTransferStatus.Full);

            byte[] before = CaptureItemSnapshot();
            StorageTransferResult extractedResult = itemIndex.TryExtract(
                entryId,
                1,
                out ItemStack extracted);
            if (!extractedResult.Success) return extractedResult;

            try
            {
                var source = new DummySlot(extracted);
                var operation = new ItemStackMoveOperation(
                    Api.World,
                    EnumMouseButton.Left,
                    0,
                    EnumMergePriority.DirectMerge,
                    1)
                {
                    ActingPlayer = player
                };
                int accepted = source.TryPutInto(cursor, ref operation);
                if (accepted != 1)
                {
                    RestoreItemSnapshot(before);
                    return StorageTransferResult.Fail(StorageTransferStatus.Full);
                }

                cursor.MarkDirty();
                CommitWithdrawal(player, extracted, accepted);
                return StorageTransferResult.Ok(accepted);
            }
            catch (Exception exception)
            {
                return FailWithdrawalCommit(before, exception);
            }
        }

        private StorageTransferResult WithdrawStackToInventory(IServerPlayer player, long entryId)
        {
            lock (persistenceSync)
            {
                return WithdrawStackToInventorySynchronized(player, entryId);
            }
        }

        private StorageTransferResult WithdrawStackToInventorySynchronized(
            IServerPlayer player,
            long entryId)
        {
            StorageTransferResult readiness = CheckWithdrawalReadiness();
            if (readiness.Status != StorageTransferStatus.Ok) return readiness;

            byte[] before = CaptureItemSnapshot();
            StorageTransferResult extractedResult = itemIndex.TryExtract(
                entryId,
                int.MaxValue,
                out ItemStack extracted);
            if (!extractedResult.Success) return extractedResult;

            try
            {
                ItemStack unaccepted = extracted.Clone();
                player.InventoryManager.TryGiveItemstack(unaccepted, true);
                int accepted = extracted.StackSize - (unaccepted?.StackSize ?? 0);
                if (unaccepted?.StackSize > 0)
                {
                    StorageTransferResult restored = itemIndex.TryInsert(
                        Api.World,
                        unaccepted,
                        out _,
                        unaccepted.StackSize);
                    if (restored.Moved != unaccepted.StackSize)
                    {
                        throw new InvalidOperationException(
                            "Storage could not restore an unaccepted withdrawal remainder.");
                    }
                }
                if (accepted <= 0)
                {
                    RestoreItemSnapshot(before);
                    return StorageTransferResult.Fail(StorageTransferStatus.Full);
                }

                CommitWithdrawal(player, extracted, accepted);
                return StorageTransferResult.Ok(accepted);
            }
            catch (Exception exception)
            {
                return FailWithdrawalCommit(before, exception);
            }
        }

        private void CommitWithdrawal(IServerPlayer player, ItemStack extracted, int accepted)
        {
            CommitItemSnapshot();
            Api.World.Logger.Audit(
                "{0} withdrew {1}x {2} from kinetic warehouse {3} at {4}.",
                player.PlayerName,
                accepted,
                extracted.Collectible.Code,
                WarehouseId,
                Pos);
        }

        private StorageTransferResult FailWithdrawalCommit(byte[] before, Exception exception)
        {
            // The player inventory manager and this method run synchronously on the server
            // thread. A commit failure is fail-closed and loudly reported; the pre-image is
            // retained for admin recovery rather than risking silent item loss.
            StructureState = StorageState.RecoveryRequired;
            terminalEntrySnapshotRevision = -1;
            MarkDirty();
            Api.World.Logger.Error(
                "[VintageKinematics] Storage withdrawal requires recovery for {0} at {1}: {2}. Pre-image bytes: {3}",
                WarehouseId,
                Pos,
                exception,
                before.Length);
            return StorageTransferResult.Fail(StorageTransferStatus.RecoveryRequired);
        }

        private StorageTransferResult CheckDepositReadiness()
        {
            if (StructureState == StorageState.Online && itemIndex != null)
            {
                if (!IsOperationallyPowered)
                {
                    return StorageTransferResult.Fail(StorageTransferStatus.Unpowered);
                }
                return new StorageTransferResult(StorageTransferStatus.Ok, 1);
            }
            return StateFailure();
        }

        private StorageTransferResult CheckWithdrawalReadiness()
        {
            if (itemIndex != null
                && (StructureState == StorageState.Online
                    || StructureState == StorageState.OverCapacity
                    || StructureState == StorageState.StructureUnknown))
            {
                if (!IsOperationallyPowered)
                {
                    return StorageTransferResult.Fail(StorageTransferStatus.Unpowered);
                }
                return new StorageTransferResult(StorageTransferStatus.Ok, 1);
            }
            return StateFailure();
        }

        private StorageTransferResult StateFailure()
        {
            StorageTransferStatus status = StructureState switch
            {
                StorageState.StructureUnknown => StorageTransferStatus.StructureUnknown,
                StorageState.RecoveryRequired => StorageTransferStatus.RecoveryRequired,
                StorageState.Corrupt => StorageTransferStatus.Corrupt,
                _ => StorageTransferStatus.Locked
            };
            return StorageTransferResult.Fail(status);
        }

        private byte[] CaptureItemSnapshot()
        {
            var persistence = new KineticStoragePersistence(Api.World);
            return persistence.Encode(persistence.Capture(itemIndex, unresolvedEntries));
        }

        private void CommitItemSnapshot()
        {
            KineticStorageRecoverySystem recoverySystem =
                Api.ModLoader.GetModSystem<KineticStorageRecoverySystem>();
            if (activeRecoveryRecord == null
                || recoverySystem?.CanPersist != true
                || !recoverySystem.IsLoaded)
            {
                throw new InvalidOperationException("Recovery mirrors are unavailable.");
            }

            byte[] snapshot = CaptureItemSnapshot();
            StorageRecoveryRecord record = StorageRecoveryRecord.Create(
                WarehouseId,
                PhysicalLocation(),
                checked(activeRecoveryRecord.Revision + 1),
                snapshot);
            recoverySystem.UpsertMirrors(record);
            activeRecoveryRecord = record;
            persistedItemCopy = StorageSnapshotCopy.FromRecord(record);
            persistedItemHeader = new StorageRecoveryIndexEntry(record);
            persistedItemHeaderBytes = Array.Empty<byte>();
            LastReconciliation = StorageSnapshotReconciler.Reconcile(
                persistedItemCopy,
                StorageSnapshotCopy.FromRecord(record));
            terminalEntrySnapshotRevision = -1;
            MarkDirty();
        }

        private void RestoreItemSnapshot(byte[] snapshot)
        {
            var persistence = new KineticStoragePersistence(Api.World);
            StorageLoadResult loaded = persistence.Load(snapshot, CurrentIndexLimits());
            if (loaded.HasCorruption)
            {
                StructureState = StorageState.RecoveryRequired;
                MarkDirty();
                return;
            }
            itemIndex = loaded.Index;
            unresolvedEntries.Clear();
            unresolvedEntries.AddRange(loaded.UnresolvedEntries);
            terminalEntrySnapshotRevision = -1;
        }
    }
}
