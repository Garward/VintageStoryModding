using System;
using Vintagestory.API.Common;
using VintageKinematics.Api.Storage;
using VintageKinematics.Storage.Terminal;

namespace VintageKinematics.Gui.Storage
{
    /// <summary>Client transfer intents, reversible prediction, and timeout recovery.</summary>
    public sealed partial class GuiDialogStorageTerminal
    {
        private void DepositHeld()
        {
            if (pendingActionRequestId > 0) return;
            ItemSlot source = capi.World.Player.InventoryManager.MouseItemSlot;
            if (source?.Empty != false)
            {
                source = capi.World.Player.InventoryManager.ActiveHotbarSlot;
            }
            inventoryPrediction = StorageTerminalInventoryPrediction.Deposit(
                capi,
                source,
                confirmedPage.Stats);
            SendAction(StorageTerminalAction.DepositHeldStack, 0);
        }

        private void WithdrawEntry(long entryId, bool wholeStack)
        {
            SendAction(
                wholeStack
                    ? StorageTerminalAction.WithdrawStackToInventory
                    : StorageTerminalAction.WithdrawOneToCursor,
                entryId);
        }

        private void SendAction(
            StorageTerminalAction action,
            long entryId,
            string sourceInventoryId = "",
            int sourceSlotId = -1)
        {
            if (pendingActionRequestId > 0) return;
            var refresh = new StorageTerminalQuery(
                ++nextRequestId,
                CurrentSearch(),
                page.Page,
                page.Sort,
                VisiblePageSize());
            pendingActionRequestId = refresh.RequestId;
            PredictAction(action, entryId);
            RefreshPageView();
            requestAction?.Invoke(new StorageTerminalActionRequest(
                action,
                entryId,
                refresh,
                sourceInventoryId,
                sourceSlotId));
            QueuePredictionTimeout(refresh.RequestId);
        }

        private void PredictAction(StorageTerminalAction action, long entryId)
        {
            if (action == StorageTerminalAction.WithdrawStackToInventory
                || action == StorageTerminalAction.WithdrawOneToCursor)
            {
                StoredEntry entry = FindEntry(entryId);
                inventoryPrediction = action == StorageTerminalAction.WithdrawOneToCursor
                    ? StorageTerminalInventoryPrediction.WithdrawOne(capi, entry?.Exemplar)
                    : StorageTerminalInventoryPrediction.WithdrawStack(
                        capi,
                        entry?.Exemplar,
                        (int)Math.Min(
                            entry?.Quantity ?? 0,
                            entry?.Exemplar?.Collectible?.MaxStackSize ?? 0));
                page = StorageTerminalPrediction.Withdraw(
                    confirmedPage,
                    entryId,
                    inventoryPrediction.Moved);
                return;
            }

            if (inventoryPrediction?.Moved > 0)
            {
                page = StorageTerminalPrediction.Deposit(
                    confirmedPage,
                    inventoryPrediction.Moved);
            }
        }

        private bool TryDepositInventorySlot(ItemSlot slot)
        {
            if (pendingActionRequestId > 0
                || slot?.Empty != false
                || slot.Inventory == null
                || slot is ItemSlotCreative)
            {
                return false;
            }

            int slotId = slot.Inventory.GetSlotId(slot);
            if (slotId < 0) return false;
            inventoryPrediction = StorageTerminalInventoryPrediction.Deposit(
                capi,
                slot,
                confirmedPage.Stats);
            SendAction(
                StorageTerminalAction.DepositInventorySlot,
                0,
                slot.Inventory.InventoryID,
                slotId);
            return true;
        }

        private StoredEntry FindEntry(long entryId)
        {
            foreach (StoredEntry entry in confirmedPage.Entries)
            {
                if (entry?.EntryId == entryId) return entry;
            }
            return null;
        }

        private void QueuePredictionTimeout(long requestId)
        {
            capi.Event.RegisterCallback(_ =>
            {
                if (!IsOpened() || pendingActionRequestId != requestId) return;
                pendingActionRequestId = 0;
                inventoryPrediction?.Rollback();
                inventoryPrediction = null;
                page = confirmedPage;
                RefreshPageView();
                SendQuery(CurrentSearch(), page.Page, page.Sort);
            }, PredictionTimeoutMs);
        }
    }
}
