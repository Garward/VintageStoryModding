using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VintageKinematics.Api.Storage
{
    /// <summary>
    /// Public storage backend contract for VK machines, hatches, funnels, tools, and downstream mods.
    /// Implementations should be server-authoritative and index-first.
    /// </summary>
    public interface IVKStorageNetwork
    {
        string WarehouseId { get; }
        BlockPos ControllerPos { get; }
        StorageStats Stats { get; }

        IReadOnlyCollection<StoredEntry> Entries { get; }

        StorageTransferResult TryInsert(ItemStack stack, out ItemStack remainder, int maxQuantity = int.MaxValue);
        StorageTransferResult TryExtract(ItemKey key, int quantity, out ItemStack extracted);
        StorageRemovalCheck CanRemoveStructuralBlock(BlockPos pos, long capacityContribution);

        void RebuildStructure(StorageChangeReason reason = StorageChangeReason.ManualRebuild);
        void MarkChanged(StorageChangeReason reason);
    }
}
