using System;
using Vintagestory.API.Common;
using VintageKinematics.Api.Storage;

namespace VintageKinematics.BlockEntities.Storage
{
    /// <summary>Server-authoritative transactions used by warehouse automation ports.</summary>
    public partial class BEKineticWarehouseTerminal
    {
        internal StorageTransferResult TryAutomationInsert(ItemStack moving, int maxQuantity)
        {
            lock (persistenceSync)
            {
                return TryAutomationInsertSynchronized(moving, maxQuantity);
            }
        }

        private StorageTransferResult TryAutomationInsertSynchronized(
            ItemStack moving,
            int maxQuantity)
        {
            StorageTransferResult readiness = CheckDepositReadiness();
            if (readiness.Status != StorageTransferStatus.Ok) return readiness;
            if (moving?.Collectible == null || moving.StackSize <= 0)
            {
                return StorageTransferResult.Fail(StorageTransferStatus.EmptyInput);
            }

            byte[] before = CaptureItemSnapshot();
            ItemStack candidate = moving.Clone();
            StorageTransferResult inserted = itemIndex.TryInsert(
                Api.World,
                candidate,
                out _,
                Math.Max(1, maxQuantity));
            if (!inserted.Success) return inserted;

            int originalQuantity = moving.StackSize;
            try
            {
                moving.StackSize = Math.Max(0, moving.StackSize - inserted.Moved);
                CommitItemSnapshot();
                return inserted;
            }
            catch (Exception exception)
            {
                moving.StackSize = originalQuantity;
                RestoreItemSnapshot(before);
                Api.World.Logger.Error(
                    "[VintageKinematics] Warehouse automation input rolled back at {0}: {1}",
                    Pos,
                    exception);
                return StorageTransferResult.Fail(StorageTransferStatus.Busy);
            }
        }

        internal StorageTransferResult TryAutomationExtract(
            System.Func<ItemStack, bool> matches,
            int maxQuantity,
            long afterEntryId,
            out ItemStack extracted,
            out long selectedEntryId)
        {
            lock (persistenceSync)
            {
                return TryAutomationExtractSynchronized(
                    matches,
                    maxQuantity,
                    afterEntryId,
                    out extracted,
                    out selectedEntryId);
            }
        }

        private StorageTransferResult TryAutomationExtractSynchronized(
            System.Func<ItemStack, bool> matches,
            int maxQuantity,
            long afterEntryId,
            out ItemStack extracted,
            out long selectedEntryId)
        {
            extracted = null;
            selectedEntryId = 0;
            StorageTransferResult readiness = CheckWithdrawalReadiness();
            if (readiness.Status != StorageTransferStatus.Ok) return readiness;
            if (maxQuantity <= 0)
            {
                return StorageTransferResult.Fail(StorageTransferStatus.InvalidQuantity);
            }

            if (!itemIndex.TryFindNextEntry(afterEntryId, matches, out StoredEntry entry))
            {
                return StorageTransferResult.Fail(StorageTransferStatus.NotFound);
            }
            selectedEntryId = entry.EntryId;

            byte[] before = CaptureItemSnapshot();
            StorageTransferResult result = itemIndex.TryExtract(
                selectedEntryId,
                maxQuantity,
                out extracted);
            if (!result.Success) return result;
            try
            {
                CommitItemSnapshot();
                return result;
            }
            catch (Exception exception)
            {
                extracted = null;
                selectedEntryId = 0;
                RestoreItemSnapshot(before);
                Api.World.Logger.Error(
                    "[VintageKinematics] Warehouse automation output rolled back at {0}: {1}",
                    Pos,
                    exception);
                return StorageTransferResult.Fail(StorageTransferStatus.Busy);
            }
        }

        internal bool RestoreAutomationExtraction(ItemStack extracted)
        {
            lock (persistenceSync)
            {
                return RestoreAutomationExtractionSynchronized(extracted);
            }
        }

        private bool RestoreAutomationExtractionSynchronized(ItemStack extracted)
        {
            if (extracted?.Collectible == null || extracted.StackSize <= 0) return true;
            StorageTransferResult restored = itemIndex.TryInsert(
                Api.World,
                extracted,
                out _,
                extracted.StackSize);
            if (restored.Moved != extracted.StackSize) return false;
            try
            {
                CommitItemSnapshot();
                return true;
            }
            catch (Exception exception)
            {
                StructureState = StorageState.RecoveryRequired;
                MarkDirty();
                Api.World.Logger.Error(
                    "[VintageKinematics] Warehouse output rollback requires recovery at {0}: {1}",
                    Pos,
                    exception);
                return false;
            }
        }
    }
}
