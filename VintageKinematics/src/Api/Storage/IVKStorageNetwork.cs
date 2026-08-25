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

        /// <summary>
        /// Inserts up to maxQuantity without mutating stack. Remainder is a new clone containing
        /// the unaccepted quantity, or null when the requested quantity was fully accepted.
        /// Callers are responsible for claims/authorization before invoking the trusted backend.
        /// </summary>
        StorageTransferResult TryInsert(ItemStack stack, out ItemStack remainder, int maxQuantity = int.MaxValue);

        /// <summary>
        /// Extracts from an opaque entry id returned by an entry snapshot. A single call returns
        /// at most one legal collectible stack. Stale or unknown ids return NotFound.
        /// </summary>
        StorageTransferResult TryExtract(long entryId, int quantity, out ItemStack extracted);
        StorageRemovalCheck CanRemoveStructuralBlock(BlockPos pos, long capacityContribution);

        void RebuildStructure(StorageChangeReason reason = StorageChangeReason.ManualRebuild);
        void MarkChanged(StorageChangeReason reason);
    }
}
