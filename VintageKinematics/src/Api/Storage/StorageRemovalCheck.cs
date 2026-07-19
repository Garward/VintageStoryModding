using Vintagestory.API.MathTools;

namespace VintageKinematics.Api.Storage
{
    /// <summary>
    /// Result of asking a warehouse if a structural block may be removed.
    /// </summary>
    public readonly struct StorageRemovalCheck
    {
        public readonly bool Allowed;
        public readonly BlockPos CheckedPos;
        public readonly long StoredItems;
        public readonly long CurrentCapacity;
        public readonly long CapacityAfterRemoval;
        public readonly string MessageLangCode;

        public StorageRemovalCheck(
            bool allowed,
            BlockPos checkedPos,
            long storedItems,
            long currentCapacity,
            long capacityAfterRemoval,
            string messageLangCode = null)
        {
            Allowed = allowed;
            CheckedPos = checkedPos;
            StoredItems = storedItems;
            CurrentCapacity = currentCapacity;
            CapacityAfterRemoval = capacityAfterRemoval;
            MessageLangCode = messageLangCode;
        }

        public static StorageRemovalCheck Allow(BlockPos pos, long storedItems, long currentCapacity, long capacityAfterRemoval)
        {
            return new StorageRemovalCheck(true, pos?.Copy(), storedItems, currentCapacity, capacityAfterRemoval);
        }

        public static StorageRemovalCheck Deny(BlockPos pos, long storedItems, long currentCapacity, long capacityAfterRemoval, string messageLangCode)
        {
            return new StorageRemovalCheck(false, pos?.Copy(), storedItems, currentCapacity, capacityAfterRemoval, messageLangCode);
        }
    }
}
