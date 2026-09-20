using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using VintageKinematics.Api.Storage;

namespace VintageKinematics.Gui.Storage
{
    /// <summary>Reversible client visuals; authoritative inventory packets always win.</summary>
    internal sealed class StorageTerminalInventoryPrediction
    {
        private readonly ICoreClientAPI capi;
        private readonly List<PredictedSlot> slots = new();

        public int Moved { get; private set; }

        private StorageTerminalInventoryPrediction(ICoreClientAPI capi)
        {
            this.capi = capi;
        }

        public static StorageTerminalInventoryPrediction Deposit(
            ICoreClientAPI capi,
            ItemSlot source,
            StorageStats stats)
        {
            var prediction = new StorageTerminalInventoryPrediction(capi);
            long freeCapacity = System.Math.Max(0, stats.ItemCapacity - stats.StoredItems);
            int quantity = (int)System.Math.Min(source?.StackSize ?? 0, freeCapacity);
            if (quantity <= 0) return prediction;

            ItemStack before = Clone(source.Itemstack);
            source.TakeOut(quantity);
            source.MarkDirty();
            prediction.slots.Add(new PredictedSlot(source, before, Clone(source.Itemstack)));
            prediction.Moved = quantity;
            return prediction;
        }

        public static StorageTerminalInventoryPrediction WithdrawOne(
            ICoreClientAPI capi,
            ItemStack exemplar)
        {
            var prediction = new StorageTerminalInventoryPrediction(capi);
            ItemSlot cursor = capi.World.Player.InventoryManager.MouseItemSlot;
            if (cursor == null || exemplar?.Collectible == null) return prediction;

            ItemStack before = Clone(cursor.Itemstack);
            ItemStack offered = exemplar.Clone();
            offered.StackSize = 1;
            var source = new DummySlot(offered);
            var operation = new ItemStackMoveOperation(
                capi.World,
                EnumMouseButton.Right,
                0,
                EnumMergePriority.DirectMerge,
                1)
            {
                ActingPlayer = capi.World.Player
            };
            prediction.Moved = source.TryPutInto(cursor, ref operation);
            if (prediction.Moved <= 0) return prediction;

            cursor.MarkDirty();
            prediction.slots.Add(new PredictedSlot(cursor, before, Clone(cursor.Itemstack)));
            return prediction;
        }

        public static StorageTerminalInventoryPrediction WithdrawStack(
            ICoreClientAPI capi,
            ItemStack exemplar,
            int quantity)
        {
            var prediction = new StorageTerminalInventoryPrediction(capi);
            if (exemplar?.Collectible == null || quantity <= 0) return prediction;

            IPlayer player = capi.World.Player;

            ItemStack offered = exemplar.Clone();
            offered.StackSize = quantity;
            var source = new DummySlot(offered);
            var skipped = new List<ItemSlot>();
            var operation = new ItemStackMoveOperation(
                capi.World,
                EnumMouseButton.Left,
                0,
                EnumMergePriority.AutoMerge,
                quantity)
            {
                ActingPlayer = player
            };
            while (!source.Empty)
            {
                ItemSlot target = player.InventoryManager.GetBestSuitedSlot(
                    source,
                    true,
                    operation,
                    skipped);
                if (target == null) break;
                skipped.Add(target);
                ItemStack before = Clone(target.Itemstack);
                int beforeQuantity = source.StackSize;
                source.TryPutInto(target, ref operation);
                if (source.StackSize == beforeQuantity) break;
                // TryPutInto already notifies the target inventory. Snapshot only the
                // recipient: bag contents also change their containing bag's attributes,
                // and marking that bag dirty would rebuild and detach its content slots.
                prediction.slots.Add(new PredictedSlot(target, before, Clone(target.Itemstack)));
            }
            prediction.Moved = quantity - source.StackSize;
            return prediction;
        }

        public void Confirm()
        {
            slots.Clear();
        }

        public void Rollback()
        {
            foreach (PredictedSlot predicted in slots)
            {
                // Inventory packets and bag changes can replace slot objects while a
                // prediction is pending. Never restore or notify a detached slot.
                if (predicted.Slot.Inventory != null
                    && predicted.Slot.Inventory.GetSlotId(predicted.Slot) < 0) continue;
                if (!Same(capi.World, predicted.Slot.Itemstack, predicted.After)) continue;
                predicted.Slot.Itemstack = Clone(predicted.Before);
                predicted.Slot.MarkDirty();
            }
            slots.Clear();
        }

        private static bool Same(IWorldAccessor world, ItemStack left, ItemStack right)
        {
            if (left == null || right == null) return left == null && right == null;
            return left.StackSize == right.StackSize
                && left.Equals(world, right, GlobalConstants.IgnoredStackAttributes);
        }

        private static ItemStack Clone(ItemStack stack)
        {
            return stack?.Clone();
        }

        private sealed record PredictedSlot(ItemSlot Slot, ItemStack Before, ItemStack After);
    }
}
